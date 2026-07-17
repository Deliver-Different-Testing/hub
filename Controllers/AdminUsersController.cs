using Hub.Interfaces;
using Hub.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;

namespace Hub.Controllers;

// Phase 5+28a §B.1 — server-to-server endpoint called by
// DfrntDriveConfigurator's NP creation cascade. Provisions a Master DB
// User row with IsNetworkPartner=true and triggers an invite email
// (re-uses the existing password-reset stored proc as the email-send
// mechanism per the brief decisions; a dedicated invite template is a
// separate follow-on slice).
//
// Authentication is the same header-based API-key pattern that
// PartnerDirectoryController + TenantsController already use, but with a
// DEDICATED env var (`ConfiguratorApiKey`) so the configurator's trust
// boundary is separate from the partner-directory consumer's. Different
// rotation policy, different audit trail.
[Route("api/admin/users")]
[AllowAnonymous]                            // gated by ApiKey, not cookie
[EnableRateLimiting("api")]
public class AdminUsersController(
    IConnectionStringManager connectionStringManager,
    IAuthenticationRepository authenticationRepository,
    IDespatchRepository despatchRepository) : Controller
{
    public record CreateNpUserRequest(string Email, int CurrentTenantId);

    public record CreateNpUserResponse(int UserId, string Email, bool InviteEmailSent);

    // Network Partner provisioning — the original §B.1 cascade. Stamps
    // IsNetworkPartner=true. Left on the bare POST route for back-compat with
    // the existing configurator NP cascade.
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        [FromBody] CreateNpUserRequest request)
        => await ProvisionAsync(apiKey, request, isNetworkPartner: true, kind: "CreateNpUser");

    // Tenant-user provisioning — called by the configurator's Team-page create
    // cascade. Same flow as the NP path but IsNetworkPartner=false. This closes
    // the gap where tenant users got a tucClientContact + role rows but NO
    // Master.User, leaving them unable to log in or reset their password.
    [HttpPost("tenant")]
    public async Task<IActionResult> CreateTenantUser(
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        [FromBody] CreateNpUserRequest request)
        => await ProvisionAsync(apiKey, request, isNetworkPartner: false, kind: "CreateTenantUser");

    // ── Staff password management (configurator Team & Users → Access tab) ───
    // Item 7b — let a DF-admin reset/set a staff user's Hub password directly,
    // or trigger the standard reset-email flow, from the configurator. Same
    // X-Api-Key trust boundary as the provisioning endpoints above. Both look
    // up the staff (non-courier) Master.User by email; courier logins are a
    // separate scheme and are explicitly NOT touched here.

    public record SetPasswordRequest(string Email, string Password);
    public record SetPasswordResponse(int UserId, string Email);

    public record SendResetRequest(string Email);
    public record SendResetResponse(int UserId, string Email, bool ResetEmailSent);

    // Direct set — hashes and stores the password immediately (operator hands it
    // to the user out-of-band). Master-DB only; no tenant proc / email involved.
    [HttpPost("set-password")]
    public async Task<IActionResult> SetPassword(
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        [FromBody] SetPasswordRequest request)
    {
        if (!IsApiKeyValid(apiKey))
        {
            return Unauthorized();
        }

        if (request is null
            || string.IsNullOrWhiteSpace(request.Email)
            || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { error = "Email and Password are required." });
        }
        if (request.Password.Trim().Length < 8)
        {
            return BadRequest(new { error = "Password must be at least 8 characters." });
        }

        try
        {
            var user = await authenticationRepository.GetUserByEmailAsync(request.Email.Trim(), isCourier: false);
            if (user is null)
            {
                Log.Warning("SetPassword: no staff Master.User for {Email}", request.Email);
                return NotFound(new { error = $"No Hub login exists for \"{request.Email}\". Invite the user first." });
            }

            var hashed = PasswordHelper.SaltHashNewPassword(request.Password.Trim());
            user.Password = hashed.Hashed;
            user.Salt = hashed.Salt;
            user.IsLegacyHash = false;     // freshly hashed with the modern scheme
            user.ResetKey = null;          // any outstanding reset link is now void
            await authenticationRepository.SaveAsync();

            Log.Information("SetPassword: set password for Master.User {UserId} ({Email}).", user.UserId, user.Email);
            return Ok(new SetPasswordResponse(user.UserId, user.Email));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SetPassword unexpected failure for {Email}", request?.Email);
            return StatusCode(500);
        }
    }

    // Reset email — generates a fresh ResetKey and re-uses the tenant-DB
    // NET_stpContact_ResetPassword proc (same mechanism as ForgotPassword and
    // the invite cascade) to email the user a link to set their own password.
    // Partial-success contract: 200 with ResetEmailSent=false when the key was
    // stored but the email couldn't be dispatched.
    [HttpPost("send-reset")]
    public async Task<IActionResult> SendReset(
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        [FromBody] SendResetRequest request)
    {
        if (!IsApiKeyValid(apiKey))
        {
            return Unauthorized();
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest(new { error = "Email is required." });
        }

        var email = request.Email.Trim();
        try
        {
            var user = await authenticationRepository.GetUserByEmailAsync(email, isCourier: false);
            if (user is null)
            {
                Log.Warning("SendReset: no staff Master.User for {Email}", email);
                return NotFound(new { error = $"No Hub login exists for \"{email}\". Invite the user first." });
            }

            user.ResetKey = Guid.NewGuid().ToString();
            await authenticationRepository.SaveAsync();

            var dbConnection = user.CurrentTenant?.Dbconnection;
            if (string.IsNullOrEmpty(dbConnection))
            {
                Log.Error("SendReset: Master.User {UserId} has no CurrentTenant connection; reset email NOT sent.", user.UserId);
                return Ok(new SendResetResponse(user.UserId, user.Email, ResetEmailSent: false));
            }
            SetTenantConnectionString(dbConnection);

            var contact = await despatchRepository.FetchUserByUsernameAsync(email);
            if (contact is null)
            {
                Log.Error("SendReset: Master.User {UserId} reset key set, but no tucClientContact UserName={Email}; reset email NOT sent.",
                    user.UserId, email);
                return Ok(new SendResetResponse(user.UserId, user.Email, ResetEmailSent: false));
            }

            var reply = Environment.GetEnvironmentVariable("ReplyEmail") ?? string.Empty;
            var baseLink = Environment.GetEnvironmentVariable("ResetBaseLink") ?? string.Empty;
            var link = $"{baseLink}?code={user.ResetKey}";

            try
            {
                await despatchRepository.InitiatePasswordResetAsync(contact.UcctId, email, reply, link);
                Log.Information("SendReset: dispatched reset email for Master.User {UserId} ({Email}).", user.UserId, user.Email);
                return Ok(new SendResetResponse(user.UserId, user.Email, ResetEmailSent: true));
            }
            catch (Exception emailEx)
            {
                Log.Error(emailEx, "SendReset: reset email send failed for Master.User {UserId}.", user.UserId);
                return Ok(new SendResetResponse(user.UserId, user.Email, ResetEmailSent: false));
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SendReset unexpected failure for {Email}", email);
            return StatusCode(500);
        }
    }

    private async Task<IActionResult> ProvisionAsync(
        string? apiKey, CreateNpUserRequest request, bool isNetworkPartner, string kind)
    {
        if (!IsApiKeyValid(apiKey))
        {
            return Unauthorized();
        }

        if (request is null
            || string.IsNullOrWhiteSpace(request.Email)
            || request.CurrentTenantId <= 0)
        {
            return BadRequest(new { error = "Email and CurrentTenantId are required." });
        }

        try
        {
            // Step 1 — Master DB user row + ResetKey.
            var user = await authenticationRepository.CreateUserAsync(
                request.Email, request.CurrentTenantId, isNetworkPartner);
            if (user is null)
            {
                Log.Warning("{Kind}: email {Email} already exists in Master.User", kind, request.Email);
                return Conflict(new { error = $"A Hub user with email \"{request.Email}\" already exists." });
            }

            // Step 2 — switch to the tenant's Despatch DB so we can look
            // up the tucClientContact (created upstream by the
            // configurator §A cascade — UserName=email is the join key).
            var tenantConnection = await authenticationRepository.GetTenantConnectionStringAsync(request.CurrentTenantId);
            if (string.IsNullOrEmpty(tenantConnection))
            {
                // Master.User is now created but tenant lookup failed —
                // surface partial state to caller per the partial-failure
                // UX decision (no rollback; caller surfaces + retries).
                Log.Error("{Kind}: created Master.User {UserId} but tenant {TenantId} has no connection string; invite email NOT sent.",
                    kind, user.UserId, request.CurrentTenantId);
                return Ok(new CreateNpUserResponse(user.UserId, user.Email, InviteEmailSent: false));
            }
            SetTenantConnectionString(tenantConnection);

            var contact = await despatchRepository.FetchUserByUsernameAsync(request.Email);
            if (contact is null)
            {
                Log.Error("{Kind}: Master.User {UserId} created, but no tucClientContact with UserName={Email} on tenant {TenantId}; invite email NOT sent.",
                    kind, user.UserId, request.Email, request.CurrentTenantId);
                return Ok(new CreateNpUserResponse(user.UserId, user.Email, InviteEmailSent: false));
            }

            // Step 3 — invite email via the existing tenant-DB stored
            // proc. NET_stpContact_ResetPassword reads SMTP config from
            // TblSetting and sends the email; we pass the link with the
            // ResetKey embedded so the recipient lands on the existing
            // /Account/ResetPassword page where they set their initial
            // password.
            var reply = Environment.GetEnvironmentVariable("ReplyEmail") ?? string.Empty;
            var baseLink = Environment.GetEnvironmentVariable("ResetBaseLink") ?? string.Empty;
            var link = $"{baseLink}?code={user.ResetKey}";

            try
            {
                await despatchRepository.InitiatePasswordResetAsync(contact.UcctId, request.Email, reply, link);
                Log.Information("{Kind}: provisioned Master.User {UserId} ({Email}) on tenant {TenantId}; invite email dispatched via tenant proc.",
                    kind, user.UserId, request.Email, request.CurrentTenantId);
                return CreatedAtAction(nameof(Create), null,
                    new CreateNpUserResponse(user.UserId, user.Email, InviteEmailSent: true));
            }
            catch (Exception emailEx)
            {
                // Master.User exists and contact bridge is set up; only
                // the email-send fell over. Configurator surfaces this
                // to the operator with a retry-invite affordance.
                Log.Error(emailEx,
                    "{Kind}: Master.User {UserId} created but invite-email send failed for tenant {TenantId}.",
                    kind, user.UserId, request.CurrentTenantId);
                return Ok(new CreateNpUserResponse(user.UserId, user.Email, InviteEmailSent: false));
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "{Kind} unexpected failure for {Email} on tenant {TenantId}",
                kind, request?.Email, request?.CurrentTenantId);
            return StatusCode(500);
        }
    }

    private void SetTenantConnectionString(string dbConnection)
    {
        var credentials = Environment.GetEnvironmentVariable("SQLCredentials") ?? string.Empty;
        if (string.IsNullOrEmpty(credentials))
        {
            throw new InvalidOperationException("Could not find a environment variable string named 'SQLCredentials'.");
        }

        connectionStringManager.SetConnectionString(dbConnection + credentials);
    }

    private static bool IsApiKeyValid(string? apiKey)
    {
        var expectedKey = Environment.GetEnvironmentVariable("ConfiguratorApiKey") ?? string.Empty;
        if (!string.IsNullOrEmpty(expectedKey) &&
            string.Equals(apiKey, expectedKey, StringComparison.Ordinal))
        {
            return true;
        }

        Log.Warning("Admin user-create request rejected: invalid or missing ConfiguratorApiKey");
        return false;
    }
}
