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
}
