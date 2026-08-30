using Hub.Interfaces;
using Hub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using Hub.Models;

namespace Hub.Controllers;

[Authorize]
public class HomeController(
    IConnectionStringManager connectionStringManager,
    IDespatchRepository despatchRepository,
    IFeatureResolver featureResolver,
    ITileAccessResolver tileAccessResolver)
    : Controller
{
    public async Task<IActionResult> Index()
    {
        var cid = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "ContactID")?.Value;
        var connectionString = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "Connection")?.Value;
        var internalTenantUser = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "Internal")?.Value;
        var userEmail = HttpContext.User.Claims.FirstOrDefault(x => x.Type == System.Security.Claims.ClaimTypes.Name)
            ?.Value;
        var tenantCode = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "TenantCode")?.Value;
        if (cid == null || connectionString == null)
        {
            return RedirectToAction("Login", "Account");
        }

        Log.Debug("Found Identity for ContactID:{Cid}", cid);
        SetTenantConnectionString(connectionString);

        var contactId = int.Parse(cid);
        var internetPermissions = await despatchRepository.GetDespatchWebInternetPermissionsAsync(contactId);

        // Phase 5+31 R2 §2 — resolve the user's visible hub-tile-* feature
        // keys against the ClientType × Feature matrix. Drives the
        // non-courier non-internal-staff tile rendering in Index.cshtml.
        // Internal-staff branch (clientInternal=true) ignores this set and
        // shows all tiles unconditionally for R2 closeout; matrix-driving
        // internal too is future work after the seed gains keys for the
        // ~10 unkeyed tiles (Accounts, AdminManager, Bulk Import, etc.).
        var clientIdClaim = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "ClientID")?.Value;
        int? clientId = int.TryParse(clientIdClaim, out var ci) && ci > 0 ? ci : null;
        // DF-admin (ClientType=5) bypass is determined inside the resolver from
        // the client's ClientType — no longer the legacy UserGroupID==1 check.
        //
        // isInternal is passed so the tenant's own staff resolve as Tenant
        // (ClientType 4) rather than Customer (2) — they sit on Customer clients
        // carrying ucclInternal=1, and resolving that literally would strip most
        // of their tiles. See FeatureResolver for the full reasoning.
        var isInternalStaff = bool.TryParse(internalTenantUser, out var iu) && iu;
        var visibleFeatures = await featureResolver.ResolveForClientAsync(clientId, isInternalStaff);

        // Gate 2 - tile-level access (Steve 2026-08-26). Gate 1 above says which
        // features DF Admin enabled for the tenant; this says which of the hub
        // tiles the contact's ROLES may see. A tile needs both to render.
        //
        // Only hub-tile-* keys are removed: everything else in the set is
        // sub-tile detail that the tile model does not govern.
        var tileDecisions = await tileAccessResolver.ResolveAsync(contactId, clientId, visibleFeatures);
        foreach (var denied in tileDecisions.Where(d => !d.Granted))
        {
            visibleFeatures.Remove(denied.TileKey);
        }

        // Debug-logged because it is the only way to answer "why can't this user
        // see that tile". The tile is simply absent either way, and which gate
        // closed is not recoverable after the fact.
        Log.Debug("Tile access for ContactID:{Cid} - {Decisions}",
            contactId,
            string.Join(", ", tileDecisions.Select(d => $"{d.TileKey}={d.Reason}")));

        var model = new HomeViewModel
        {
            ContactId = contactId,

            DespatchWebPermission = GetPermission(internetPermissions, 12),
            BookJobPermission = GetPermission(internetPermissions, 2),
            BulkUploadPermission = GetPermission(internetPermissions, 11),
            UserEmail = userEmail,
            TenantCode = tenantCode,
            ClientInternal = internalTenantUser,
            ShowAfterHours = await IsAfterHoursAuthorizedAsync(),
            VisibleFeatures = visibleFeatures
        };

        return View(model);
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

    private static bool GetPermission(List<RVW_stpValidateInternetPermissionsResult> internetPermissions,
        int internetPermissionId) => internetPermissions.Any(i => i.InternetPermissionID == internetPermissionId);

    private async Task<bool> IsAfterHoursAuthorizedAsync()
    {
        try
        {
            var isCourierClaim = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "IsCourier")?.Value;
            var courierIdClaim = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CourierID")?.Value;

            var isCourier = !string.IsNullOrEmpty(isCourierClaim) &&
                            bool.TryParse(isCourierClaim, out var courierFlag) && courierFlag;

            if (!isCourier || string.IsNullOrEmpty(courierIdClaim) || !int.TryParse(courierIdClaim, out var courierId))
            {
                return false;
            }

            var isAuthorized = await despatchRepository.IsAfterHoursAuthorizedAsync(courierId);

            Log.Information("AfterHours authorization check for courier {CourierId}: {IsAuthorized}",
                courierId, isAuthorized);

            return isAuthorized;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error checking after-hours authorization");
            return false;
        }
    }
}