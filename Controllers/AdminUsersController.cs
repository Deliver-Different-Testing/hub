using Hub.Interfaces;
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

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        [FromBody] CreateNpUserRequest request)
    {
        if (!IsApiKeyValid(apiKey))
            return Unauthorized();

        if (request is null
            || string.IsNullOrWhiteSpace(request.Email)
            || request.CurrentTenantId <= 0)
        {
            return BadRequest(new { error = "Email and CurrentTenantId are required." });
        }

        try
        {
            // Step 1 — Master DB user row + ResetKey.
            var user = await authenticationRepository.CreateNpUserAsync(request.Email, request.CurrentTenantId);
            if (user is null)
            {
                Log.Warning("CreateNpUser: email {Email} already exists in Master.User", request.Email);
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
                Log.Error("CreateNpUser: created Master.User {UserId} but tenant {TenantId} has no connection string; invite email NOT sent.",
                    user.UserId, request.CurrentTenantId);
                return Ok(new CreateNpUserResponse(user.UserId, user.Email, InviteEmailSent: false));
            }
            SetTenantConnectionString(tenantConnection);

            var contact = await despatchRepository.FetchUserByUsername(request.Email);
            if (contact is null)
            {
                Log.Error("CreateNpUser: Master.User {UserId} created, but no tucClientContact with UserName={Email} on tenant {TenantId}; invite email NOT sent.",
                    user.UserId, request.Email, request.CurrentTenantId);
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
                await despatchRepository.InitiatePasswordReset(contact.UcctId, request.Email, reply, link);
                Log.Information("CreateNpUser: provisioned Master.User {UserId} ({Email}) on tenant {TenantId}; invite email dispatched via tenant proc.",
                    user.UserId, request.Email, request.CurrentTenantId);
                return CreatedAtAction(nameof(Create), null,
                    new CreateNpUserResponse(user.UserId, user.Email, InviteEmailSent: true));
            }
            catch (Exception emailEx)
            {
                // Master.User exists and contact bridge is set up; only
                // the email-send fell over. Configurator surfaces this
                // to the operator with a retry-invite affordance.
                Log.Error(emailEx,
                    "CreateNpUser: Master.User {UserId} created but invite-email send failed for tenant {TenantId}.",
                    user.UserId, request.CurrentTenantId);
                return Ok(new CreateNpUserResponse(user.UserId, user.Email, InviteEmailSent: false));
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CreateNpUser unexpected failure for {Email} on tenant {TenantId}",
                request?.Email, request?.CurrentTenantId);
            return StatusCode(500);
        }
    }

    private void SetTenantConnectionString(string dbConnection)
    {
        var credentials = Environment.GetEnvironmentVariable("SQLCredentials") ?? string.Empty;
        if (string.IsNullOrEmpty(credentials))
            throw new InvalidOperationException("Could not find a environment variable string named 'SQLCredentials'.");
        connectionStringManager.SetConnectionString(dbConnection + credentials);
    }

    private static bool IsApiKeyValid(string? apiKey)
    {
        var expectedKey = Environment.GetEnvironmentVariable("ConfiguratorApiKey") ?? string.Empty;
        if (!string.IsNullOrEmpty(expectedKey) &&
            string.Equals(apiKey, expectedKey, StringComparison.Ordinal))
            return true;
        Log.Warning("Admin user-create request rejected: invalid or missing ConfiguratorApiKey");
        return false;
    }
}
