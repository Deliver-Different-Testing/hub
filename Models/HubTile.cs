namespace Hub.Models;

/// <summary>One launcher tile, fully resolved and ready to render.</summary>
/// <param name="Key">The <c>hub-tile-*</c> catalogue key this tile is gated on.</param>
/// <param name="Title">Label shown on the card.</param>
/// <param name="Icon">File name under <c>~/images/icons/</c>.</param>
/// <param name="Href">Resolved destination. Empty when <paramref name="IsDisabled"/>.</param>
/// <param name="IsDisabled">
/// Rendered as a "Coming soon" card rather than a link. Only Customer Success
/// Platform uses this: the tile is shown so staff know it is arriving, but the
/// product is not ready to be opened.
/// </param>
public sealed record HubTile(
    string Key,
    string Title,
    string Icon,
    string Href,
    bool IsDisabled = false);
