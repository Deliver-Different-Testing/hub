using System.Security.Claims;
using Hub.Interfaces;
using Hub.Models;
using Hub.ViewModels;
using Serilog;

namespace Hub.Services;

public class HomeService(
    IConnectionStringManager connectionStringManager,
    IDespatchRepository despatchRepository,
    IFeatureResolver featureResolver,
    ITileAccessResolver tileAccessResolver,
    ITenantService tenantService) : IHomeService
{
    private const string FuelSurchargeUrl = "/FuelSurcharge";

    public async Task<HomeResponse?> GetHomeAsync(ClaimsPrincipal user)
    {
        var cid = user.Claims.FirstOrDefault(x => x.Type == "ContactID")?.Value;
        var connectionString = user.Claims.FirstOrDefault(x => x.Type == "Connection")?.Value;
        var internalTenantUser = user.Claims.FirstOrDefault(x => x.Type == "Internal")?.Value;
        var userEmail = user.Claims.FirstOrDefault(x => x.Type == ClaimTypes.Name)?.Value;
        var tenantCode = user.Claims.FirstOrDefault(x => x.Type == "TenantCode")?.Value;
        var tenantIdClaim = user.Claims.FirstOrDefault(x => x.Type == "CurrentTenantID")?.Value;
        // Stamped at login from CurrentTenant.CountryCode. Feeds the per-feature
        // country scope; absent means "do not filter" rather than "show nothing".
        var countryCode = user.Claims.FirstOrDefault(x => x.Type == "CountryCode")?.Value;
        if (cid == null || connectionString == null)
        {
            return null;
        }

        Log.Debug("Found Identity for ContactID:{Cid}", cid);
        SetTenantConnectionString(connectionString);

        var contactId = int.Parse(cid);
        var internetPermissions = await despatchRepository.GetDespatchWebInternetPermissionsAsync(contactId);

        var tenantCodeValue = tenantCode ?? string.Empty;
        var isCourier = string.Equals(user.FindFirst("IsCourier")?.Value, "True",
            StringComparison.OrdinalIgnoreCase);

        // Phase 5+31 R2 §2 — resolve the user's visible hub-tile-* feature keys
        // against the ClientType × Feature matrix. Drives EVERY non-courier
        // tile as of 2026-09-01: this set plus the role gate below is the whole answer.
        var clientIdClaim = user.Claims.FirstOrDefault(x => x.Type == "ClientID")?.Value;
        int? clientId = int.TryParse(clientIdClaim, out var ci) && ci > 0 ? ci : null;
        // DF-admin (ClientType=5) bypass is determined inside the resolver from
        // the client's ClientType — no longer the legacy UserGroupID==1 check.
        //
        // isInternal is passed so the tenant's own staff resolve as Tenant
        // (ClientType 4) rather than Customer (2) — they sit on Customer clients
        // carrying ucclInternal=1, and resolving that literally would strip most
        // of their tiles. See FeatureResolver for the full reasoning.
        var isInternalStaff = bool.TryParse(internalTenantUser, out var iu) && iu;
        var visibleFeatures = await featureResolver.ResolveForClientAsync(
            clientId, isInternalStaff, countryCode);

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

        // The conditions passed in are real runtime facts the matrix cannot
        // express: a legacy per-contact permission, a page that exists on one
        // tenant only, and one account with a bespoke rule. They stay as AND
        // conditions on top of the matrix, never as a way to grant.
        var isAsureUser = string.Equals(userEmail, "asure@urgent.co.nz", StringComparison.OrdinalIgnoreCase)
                          && string.Equals(tenantCodeValue, "urgent", StringComparison.OrdinalIgnoreCase);

        var tiles = HubTileCatalogue.Resolve(
            new HubTileContext(
                AppUrl: TenantAppUrl,
                FuelSurchargeUrl: FuelSurchargeUrl,
                BookingPath: isAsureUser ? "/#/asure" : "/#/login/",
                HasBulkUploadPermission: GetPermission(internetPermissions, 11),
                ShowFuelSurcharge: !isCourier
                                   && string.Equals(tenantCodeValue, "urgent", StringComparison.OrdinalIgnoreCase),
                IsAsureUser: isAsureUser),
            visibleFeatures);

        // Header date must be tenant wall-clock, not the server clock: the host
        // can be on a different calendar day to the tenant.
        var tenantId = int.TryParse(tenantIdClaim, out var parsedTenantId) ? parsedTenantId : 0;
        var today = await tenantService.GetCurrentTenantTimeAsync(tenantId);

        var showAfterHours = await IsAfterHoursAuthorizedAsync(user);

        // Couriers are the one audience the matrix cannot drive: they have no
        // ClientType, so FeatureResolver returns an empty set for them and there is
        // no audience row to configure. Their tiles stay hardcoded.
        var responseTiles = isCourier
            ? CourierTiles(showAfterHours)
            : tiles.Select(t => new HomeTileResponse(t.Key, t.Title, IconPath(t.Icon), t.Href, t.IsDisabled)).ToList();

        return new HomeResponse(userEmail, tenantCode, today, isCourier, showAfterHours, responseTiles);
    }

    private static string IconPath(string icon) => $"/images/icons/{Uri.EscapeDataString(icon)}";

    private static List<HomeTileResponse> CourierTiles(bool showAfterHours)
    {
        List<HomeTileResponse> tiles = [];
        if (showAfterHours)
        {
            tiles.Add(new HomeTileResponse("hub-tile-afterhours", "After Hours", IconPath("dispatch.svg"),
                TenantAppUrl("afterhours"), false));
        }

        tiles.Add(new HomeTileResponse("hub-tile-courier-portal", "Courier Portal", IconPath("courier-portal.svg"),
            TenantAppUrl("courierportal"), false));

        // Courier-portal cutover: the new React portal is served by the configurator
        // (dfrntdriveconfig). It is added alongside the legacy Courier Portal tile
        // rather than swapping, so couriers can use either while it is validated.
        tiles.Add(new HomeTileResponse("hub-tile-dfrnt-drive", "DFRNT Drive", IconPath("workflow.png"),
            TenantAppUrl("dfrntdriveconfig"), false));
        return tiles;
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

    /// <summary>Per-tenant app URL from an app slug.</summary>
    private static string TenantAppUrl(string appName) =>
        (Environment.GetEnvironmentVariable("TenantURL") ?? string.Empty).Replace("app_name", appName);

    private async Task<bool> IsAfterHoursAuthorizedAsync(ClaimsPrincipal user)
    {
        try
        {
            var isCourierClaim = user.Claims.FirstOrDefault(x => x.Type == "IsCourier")?.Value;
            var courierIdClaim = user.Claims.FirstOrDefault(x => x.Type == "CourierID")?.Value;

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
