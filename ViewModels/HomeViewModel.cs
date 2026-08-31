using Hub.Models;

namespace Hub.ViewModels;

public sealed record HomeViewModel
{
    public int ContactId { get; init; }

    public bool DespatchWebPermission { get; init; }
    public bool BookJobPermission { get; init; }
    public bool BulkUploadPermission { get; init; }
    public string? UserEmail { get; init; }
    public string? TenantCode { get; init; }
    public string? ClientInternal { get; init; }
    public bool ShowAfterHours { get; init; }

    // Phase 5+31 R2 §2 — current user's visible hub-tile-* feature keys
    // resolved via IFeatureResolver against the ClientType × Feature matrix.
    // Consulted by Views/Home/Index.cshtml for matrix-driven tile rendering.
    // Empty set for courier logins (no client context) — courier branch
    // doesn't consult this field anyway.
    public HashSet<string> VisibleFeatures { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The launcher tiles this caller gets, resolved and ordered.
    /// </summary>
    /// <remarks>
    /// Replaced five hardcoded per-audience blocks in Index.cshtml on
    /// 2026-09-01. Those blocks meant the matrix could only hide a tile a block
    /// already listed, never add one — a customer could reach 6 of 17 tiles
    /// whatever Tile Access said. Now the matrix decides.
    ///
    /// Empty for couriers, who have no ClientType and therefore no audience row
    /// to configure; the view keeps a hardcoded courier block for them.
    /// </remarks>
    public IReadOnlyList<HubTile> Tiles { get; init; } = [];
}
