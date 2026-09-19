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

    // TenantId is the CALLER's tenant (Master TenantId), not the user's. See the
    // comment in SendReset. Optional so the configurator and Hub can deploy in
    // either order; absent means fall back to the old Master.CurrentTenant path.
    public record SendResetRequest(string Email, int? TenantId = null);
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

    // Reset email - re-uses the tenant-DB NET_stpContact_ResetPassword proc
    // (the same mechanism as ForgotPassword and the invite cascade) to email the
    // user a link to set their own password.
    //
    // ORDER MATTERS HERE: the tenant contact is resolved BEFORE the ResetKey is
    // rotated, so an attempt that cannot succeed does not invalidate a link the
    // user already holds. It was the other way round until 2026-09-04.
    //
    // Partial-success contract: 200 with ResetEmailSent=false when the send could
    // not be made. The response deliberately does not distinguish the reasons -
    // the log does, and the caller only needs to know it must hand the link over
    // another way.
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

            // WHICH TENANT DATABASE TO SEARCH (2026-09-04).
            //
            // The caller's tenant wins. This used to resolve solely from
            // user.CurrentTenant, which is where the user last signed in - not
            // where the administrator is working. On 2026-09-03 an admin in
            // urgent tried to reset a contact whose Master record pointed at
            // medical: Hub searched medical, found nothing, and returned
            // "email not sent" with no indication it had looked in the wrong
            // database. Nothing on the urgent side could have fixed that.
            //
            // Only DFRNT staff spanning several tenants are affected - a
            // single-tenant customer's pointer always matches - which is why it
            // went unnoticed for so long.
            //
            // TenantId is OPTIONAL so the two apps can deploy in either order:
            // an older configurator that sends nothing keeps the previous
            // behaviour, and says so in the log rather than silently differing.
            string? dbConnection;
            string tenantSource;
            if (request.TenantId is > 0)
            {
                dbConnection = await authenticationRepository.GetTenantConnectionStringAsync(request.TenantId.Value);
                tenantSource = $"caller tenant {request.TenantId.Value}";

                if (string.IsNullOrEmpty(dbConnection))
                {
                    Log.Error("SendReset: caller tenant {TenantId} has no connection string; reset email NOT sent for {Email}.",
                        request.TenantId.Value, email);
                    return Ok(new SendResetResponse(user.UserId, user.Email, ResetEmailSent: false));
                }
            }
            else
            {
                dbConnection = user.CurrentTenant?.Dbconnection;
                tenantSource = $"Master.CurrentTenant {user.CurrentTenantId} (caller sent no tenant)";
                Log.Warning(
                    "SendReset: no TenantId on the request for {Email}; falling back to Master.CurrentTenant {TenantId}. "
                    + "This is the pre-2026-09-04 behaviour and fails when the admin is in a different tenant.",
                    email, user.CurrentTenantId);

                if (string.IsNullOrEmpty(dbConnection))
                {
                    Log.Error("SendReset: Master.User {UserId} has no CurrentTenant connection; reset email NOT sent.", user.UserId);
                    return Ok(new SendResetResponse(user.UserId, user.Email, ResetEmailSent: false));
                }
            }

            SetTenantConnectionString(dbConnection);

            // THE CONTACT LOOKUP HAPPENS BEFORE THE RESET KEY IS WRITTEN.
            //
            // It used to be the other way round, so a send that could never
            // succeed still rotated the key - invalidating any reset link the
            // user already held. Two failed clicks on 2026-09-03 did exactly
            // that. Nothing is now written until the send is actually possible.
            var contact = await despatchRepository.FetchUserByUsernameAsync(email);
            if (contact is null)
            {
                // The tenant is in the message deliberately. Without it this
                // read as "the contact does not exist", which was false and
                // cost real time during the 2026-09-03 diagnosis.
                Log.Error("SendReset: no active tucClientContact with UserName={Email} in {TenantSource}; "
                    + "reset email NOT sent for Master.User {UserId}. Reset key NOT rotated.",
                    email, tenantSource, user.UserId);
                return Ok(new SendResetResponse(user.UserId, user.Email, ResetEmailSent: false));
            }

            user.ResetKey = Guid.NewGuid().ToString();
            await authenticationRepository.SaveAsync();

            var reply = Environment.GetEnvironmentVariable("ReplyEmail") ?? string.Empty;
            var baseLink = Environment.GetEnvironmentVariable("ResetBaseLink") ?? string.Empty;
            var link = $"{baseLink}?code={user.ResetKey}";

            try
            {
                await despatchRepository.InitiatePasswordResetAsync(contact.UcctId, email, reply, link);
                Log.Information("SendReset: dispatched reset email for Master.User {UserId} ({Email}) via {TenantSource}.",
                    user.UserId, user.Email, tenantSource);
                return Ok(new SendResetResponse(user.UserId, user.Email, ResetEmailSent: true));
            }
            catch (Exception emailEx)
            {
                Log.Error(emailEx, "SendReset: reset email send failed for Master.User {UserId} via {TenantSource}.",
                    user.UserId, tenantSource);
                return Ok(new SendResetResponse(user.UserId, user.Email, ResetEmailSent: false));
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SendReset unexpected failure for {Email}", email);
            return StatusCode(500);
        }
    }

    // ── Login-email change (configurator contact edit) ──────────────────────
    // 2026-08-18 — closes a single-tenant-era hole. The configurator's contact
    // edit surfaces write tucClientContact.UserName (the login key) but had no
    // way to tell Hub, so Master.User.Email went stale. Because Hub resolves
    // the tenant contact with `WHERE UserName = Master.User.Email`
    // (FetchUserByUsernameAsync), a stale address silently breaks that user's
    // password reset — it fails contact validation and no email is sent.
    // Observed on urgent-prod 2026-08-17: Master said Operations@taxisgb.co.nz
    // while the tenant row had already moved to janelle@taxisgb.co.nz.
    //
    // Staff (non-courier) only, matching set-password / send-reset. 404 when
    // there is no Hub identity for the old address — that is a legitimate
    // state (contact never invited, or the invite failed) and the caller
    // treats it as "nothing to sync", not as an error.

    public record ChangeEmailRequest(string CurrentEmail, string NewEmail);
    public record ChangeEmailResponse(int UserId, string Email);

    [HttpPost("change-email")]
    public async Task<IActionResult> ChangeEmail(
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        [FromBody] ChangeEmailRequest request)
    {
        if (!IsApiKeyValid(apiKey))
        {
            return Unauthorized();
        }

        if (request is null
            || string.IsNullOrWhiteSpace(request.CurrentEmail)
            || string.IsNullOrWhiteSpace(request.NewEmail))
        {
            return BadRequest(new { error = "CurrentEmail and NewEmail are required." });
        }

        var currentEmail = request.CurrentEmail.Trim();
        var newEmail = request.NewEmail.Trim();

        if (string.Equals(currentEmail, newEmail, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "CurrentEmail and NewEmail are the same." });
        }

        try
        {
            var user = await authenticationRepository.GetUserByEmailAsync(currentEmail, isCourier: false);
            if (user is null)
            {
                Log.Warning("ChangeEmail: no staff Master.User for {Email}", currentEmail);
                return NotFound(new { error = $"No Hub login exists for \"{currentEmail}\"." });
            }

            if (await authenticationRepository.EmailExistsAsync(newEmail))
            {
                Log.Warning("ChangeEmail: {NewEmail} is already held by another Master.User", newEmail);
                return Conflict(new { error = $"A Hub user with email \"{newEmail}\" already exists." });
            }

            var previousEmail = user.Email;
            user.Email = newEmail;
            // Any outstanding reset link was issued against the old identity.
            user.ResetKey = null;
            await authenticationRepository.SaveAsync();

            Log.Information("ChangeEmail: Master.User {UserId} moved from {OldEmail} to {NewEmail}.",
                user.UserId, previousEmail, newEmail);
            return Ok(new ChangeEmailResponse(user.UserId, user.Email));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ChangeEmail unexpected failure for {Email}", currentEmail);
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
