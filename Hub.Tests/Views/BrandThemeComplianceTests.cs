using System.Text.RegularExpressions;

namespace Hub.Tests.Views;

// Guards the DFRNT brand theming after the migration from @material/web to
// Bootstrap 5.3:
//   1. the Bootstrap bridge points --bs-* core vars at the --dd-* brand tokens
//      (so Bootstrap components inherit brand colour AND dark mode);
//   2. no hardcoded hex leaks back into the themed surfaces (they must resolve
//      through --dd-* tokens);
//   3. the tenant-selector button + theme toggle carry the on-ink navbar chrome;
//   4. shape corners use the radius token scale, not magic numbers;
//   5. dark mode is present: a neutrals-only scheme gated on data-theme +
//      prefers-color-scheme, driven by a Light / Dark / System toggle;
//   6. @material/web is fully removed (no dependency, no <md-*> custom elements).
public partial class BrandThemeComplianceTests
{
    private readonly string _siteLess = Read("wwwroot", "css", "site.less");
    private readonly string _loginLess = Read("wwwroot", "css", "login.less");

    private static string Read(params string[] segments)
    {
        var path = FindFile(segments);
        if (path is null)
        {
            Assert.Skip($"{string.Join('/', segments)} not found relative to test output directory");
        }

        return File.ReadAllText(path);
    }

    private static string? FindFile(params string[] pathSegments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine([dir.FullName, .. pathSegments]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }
        return null;
    }

    // ---- Rec #1: Bootstrap bridge maps core vars onto the brand tokens -------

    [Theory]
    [InlineData("--bs-body-bg", "--dd-surface")]
    [InlineData("--bs-body-color", "--dd-on-surface")]
    [InlineData("--bs-body-color-rgb", "--dd-on-surface-rgb")]
    [InlineData("--bs-border-color", "--dd-outline")]
    [InlineData("--bs-primary", "--dd-primary")]
    [InlineData("--bs-link-color", "--dd-primary")]
    public void BootstrapBridge_MapsCoreVarToBrandToken(string bsVar, string ddToken)
    {
        Assert.Matches($@"{Regex.Escape(bsVar)}\s*:\s*var\({Regex.Escape(ddToken)}\)", _siteLess);
    }

    [Fact]
    public void OnSurfaceRgb_DefinedForBothThemes()
    {
        // Bootstrap's .form-floating label + muted text build their colour from
        // --bs-body-color-rgb (mapped to --dd-on-surface-rgb). The rgb triple must
        // flip with the theme, or floating labels render dark-on-dark in dark mode.
        Assert.Matches(@"--dd-on-surface-rgb\s*:\s*13,\s*12,\s*44", _siteLess);
        var mixin = DarkSchemeMixinRegex().Match(_siteLess).Groups["b"].Value;
        Assert.Matches(@"--dd-on-surface-rgb\s*:\s*230,\s*225,\s*233", mixin);
    }

    [Theory]
    [InlineData("--dd-on-secondary")]
    [InlineData("--dd-on-error")]
    public void Palette_DefinesOnTokens(string token)
    {
        Assert.Matches($@"{Regex.Escape(token)}\s*:\s*#", _siteLess);
    }

    // ---- Rec #6: @material/web is gone ---------------------------------------

    [Fact]
    public void PackageJson_HasNoMaterialWebDependency()
    {
        Assert.DoesNotContain("@material/web", Read("package.json"));
    }

    [Fact]
    public void NoView_UsesMaterialCustomElements()
    {
        var repoRoot = FindFile("wwwroot", "css", "site.less");
        Assert.NotNull(repoRoot);
        var viewsDir = Path.Combine(new FileInfo(repoRoot).Directory!.Parent!.Parent!.FullName, "Views");
        foreach (var view in Directory.EnumerateFiles(viewsDir, "*.cshtml", SearchOption.AllDirectories))
        {
            var html = File.ReadAllText(view);
            Assert.DoesNotMatch(MdCustomElement(), html);
        }
    }

    [Fact]
    public void NoStylesheet_ReferencesMaterialTokens()
    {
        Assert.DoesNotContain("--md-", _siteLess);
        Assert.DoesNotContain("--md-", _loginLess);
    }

    // ---- Rec #2: no hardcoded hex on themed surfaces ------------------------

    [Fact]
    public void AuthCard_BackgroundUsesSurfaceToken()
    {
        Assert.Matches(@"\.auth-card\s*\{[^}]*background:\s*var\(--dd-surface-container-lowest\)", _siteLess);
        Assert.DoesNotMatch(@"\.auth-card\s*\{[^}]*background:\s*#ffffff", _siteLess);
    }

    [Fact]
    public void FuelChipCurrent_UsesSuccessTokensNotHex()
    {
        var block = FuelChipCurrentRegex().Match(_siteLess).Groups["b"].Value;
        Assert.Contains("rgba(var(--dd-success-rgb)", block);
        Assert.DoesNotContain("rgba(76, 175, 80", block);

        Assert.Matches(@"@color_9\s*:\s*var\(--dd-success-text\)", _siteLess);
        Assert.Matches(@"--dd-success-text\s*:\s*#0e8f4d", _siteLess);
        Assert.Single(ColorRegex().Matches(_siteLess));
    }

    [Fact]
    public void PasswordStrength_AmberUsesWarningToken()
    {
        // The score-2 strength bar (Bootstrap .progress-bar) must reference the token...
        Assert.Matches(
            """data-score="2"\][^{]*\.progress-bar\s*\{[^}]*background-color:\s*var\(--dd-warning-strong\)""",
            _siteLess);
        // ...and the #e6740f hex must survive ONLY as the token declaration.
        Assert.Matches(@"--dd-warning-strong\s*:\s*#e6740f", _siteLess);
        Assert.Single(Regex.Matches(_siteLess, "#e6740f"));
    }

    [Fact]
    public void LoginButton_TextUsesOnPrimaryNotWhite()
    {
        Assert.DoesNotContain("#fff", _loginLess);
        Assert.Contains("var(--dd-on-primary)", _loginLess);
    }

    [Fact]
    public void Autofill_MaskedToThemedSurfaceAndText()
    {
        // Chrome's :-webkit-autofill layer paints a pale-blue bg + dark text that
        // ignores the theme, hiding the light floating label in dark mode. The
        // inset shadow must track --dd-surface (flips with theme) and the text
        // fill must be forced to --dd-on-surface.
        Assert.Matches(
            @"\.form-control:-webkit-autofill[^{]*\{[^}]*-webkit-box-shadow:\s*0 0 0 1000px var\(--dd-surface\) inset",
            _siteLess);
        Assert.Matches(
            @"\.form-control:-webkit-autofill[^{]*\{[^}]*-webkit-text-fill-color:\s*var\(--dd-on-surface\)",
            _siteLess);
    }

    // ---- Dark mode: neutrals-only scheme + Light/Dark/System toggle ---------

    [Fact]
    public void DarkScheme_DefinesNeutralOverrides()
    {
        Assert.Contains("prefers-color-scheme", _siteLess);
        Assert.Matches(@"--dd-surface\s*:\s*#2c2a30", _siteLess);
        Assert.Matches(@"--dd-on-surface\s*:\s*rgba\(230, 225, 233, \.92\)", _siteLess);
    }

    [Fact]
    public void DarkScheme_GatedByBothTriggers()
    {
        Assert.Contains("[data-theme=\"dark\"]", _siteLess);
        Assert.Contains(":not([data-theme=\"light\"])", _siteLess);
    }

    [Fact]
    public void DarkScheme_KeepsBrandLight()
    {
        var mixin = DarkSchemeMixinRegex().Match(_siteLess);
        Assert.True(mixin.Success, "dark-scheme mixin not found");
        Assert.DoesNotMatch(@"--dd-primary\b\s*:", mixin.Groups["b"].Value);
    }

    [Fact]
    public void DarkScheme_AssetSwapsLocalLogo()
    {
        var body = DarkSchemeMixinRegex().Match(_siteLess).Groups["b"].Value;
        Assert.Matches(@"\.hub-brand-logo\.logo-light\s*\{[^}]*display:\s*none", body);
        Assert.Matches(@"\.hub-brand-logo\.logo-dark\s*\{[^}]*display:\s*block", body);
        Assert.DoesNotMatch(@"\.hub-brand-logo\s*\{[^}]*background", body);
        Assert.Matches(@"\.logo-dark\s*\{\s*display:\s*none", _siteLess);
        Assert.DoesNotContain("--dd-plate", _siteLess);
    }

    [Fact]
    public void Navbar_AppBarLogoAlwaysUsesDarkVariant()
    {
        Assert.Matches(@"\.navbar \.logo\.logo-light\s*\{[^}]*display:\s*none", _siteLess);
        Assert.Matches(@"\.navbar \.logo\.logo-dark\s*\{[^}]*display:\s*block", _siteLess);
        Assert.Matches(@"\.navbar \.s3-logo\s*\{[^}]*background:\s*var\(--dd-surface-container-lowest\)", _siteLess);
    }

    [Fact]
    public void Navbar_IsInkBlueWithOnInkChrome()
    {
        // The app bar is painted Ink Blue and its chrome (label text + theme-toggle
        // icon) is light (--dd-on-ink) in BOTH themes — no per-theme flip.
        Assert.Matches(@"\.navbar\s*\{[^}]*background:\s*var\(--dd-ink\)\s*!important", _siteLess);
        Assert.Matches(@"\.tenant-label\s*\{[^}]*color:\s*var\(--dd-on-ink\)", _siteLess);
        Assert.Matches(@"#themeToggle\s*\{[^}]*color:\s*var\(--dd-on-ink\)", _siteLess);
        // The dark scheme must NOT re-flip navbar chrome any more.
        var mixin = DarkSchemeMixinRegex().Match(_siteLess).Groups["b"].Value;
        Assert.DoesNotMatch(@"\.tenant-label,\s*\.username,\s*\.dropdown-arrow", mixin);
    }

    [Fact]
    public void DarkScheme_LiftsAppCardHoverTitle()
    {
        var body = DarkSchemeMixinRegex().Match(_siteLess).Groups["b"].Value;
        Assert.Matches(@"\.app-card:hover\s*\.app-card-title\s*\{[^}]*color:\s*var\(--dd-primary\)", body);
        Assert.DoesNotMatch(@"\.app-card-icon\s*\{", body);
    }

    [Fact]
    public void DarkScheme_LiftsActiveStatusChip()
    {
        // The primary brand block isn't flipped in dark mode, so the "Active"
        // chip's --dd-on-primary-container text (#1834c4) and 12% fill go
        // dark-on-dark. The dark scheme must override it to a legible light-blue
        // text + stronger fill (mirrors the --dd-success-text lift for Current).
        var body = DarkSchemeMixinRegex().Match(_siteLess).Groups["b"].Value;
        Assert.Matches(@"\.fuel-chip-active\s*\{[^}]*color:\s*#aebdff", body);
        Assert.Matches(@"\.fuel-chip-active\s*\{[^}]*background:\s*rgba\(var\(--dd-primary-rgb\), \.22\)", body);
    }

    [Fact]
    public void DarkScheme_PlatesArbitraryS3LogoOnly()
    {
        var body = DarkSchemeMixinRegex().Match(_siteLess).Groups["b"].Value;
        Assert.Matches(@"\.s3-logo\s*\{[^}]*background:\s*var\(--dd-inverse-surface\)", body);
        // The light chip carries a soft elevation shadow so it reads as a
        // deliberate lifted tile, not a flat glaring box on the dark page.
        Assert.Matches(@"\.s3-logo\s*\{[^}]*box-shadow:\s*0 1px 3px rgba\(0, 0, 0, \.35\)", body);
    }

    [Fact]
    public void DarkScheme_SwapsLoginMascotVariant()
    {
        var body = DarkSchemeMixinRegex().Match(_siteLess).Groups["b"].Value;
        Assert.Matches(@"\.auth-lottie\.auth-lottie-light\s*\{[^}]*display:\s*none", body);
        Assert.Matches(@"\.auth-lottie\.auth-lottie-dark\s*\{[^}]*display:\s*block", body);
        Assert.DoesNotMatch(@"\.auth-lottie[^{]*\{[^}]*background:\s*var\(--dd-inverse-surface\)", body);
        Assert.Matches(@"\.auth-lottie\.auth-lottie-dark\s*\{\s*display:\s*none", _siteLess);
    }

    [Fact]
    public void LoginView_RendersBothMascotVariants()
    {
        var view = Read("Views", "Account", "Login.cshtml");
        Assert.Contains("auth-lottie-light", view);
        Assert.Contains("auth-lottie-dark", view);
        Assert.Contains("(2)_dark.json", view);
        // Both mascot variants loop: playback is JS-triggered (respecting
        // reduced-motion) but the `loop` attribute keeps it running rather than
        // stopping after a single pass.
        Assert.Matches(@"<lottie-player[^>]*auth-lottie-light[^>]*\bloop\b", view);
        Assert.Matches(@"<lottie-player[^>]*auth-lottie-dark[^>]*\bloop\b", view);
    }

    [Fact]
    public void LoginScript_PlaysMascotOnPlayerReadyNotImmediately()
    {
        // The <lottie-player> is already upgraded by the time login.ts runs, so a
        // bare `typeof play === 'function'` gate fires play() before the animation
        // JSON has loaded — a silent no-op that leaves the mascot frozen on frame
        // one. Playback must instead be wired to the player's ready/load events,
        // and still be suppressed for reduced-motion users.
        var script = Read("src", "login.ts");
        Assert.Matches(@"addEventListener\('ready',\s*play", script);
        Assert.Matches(@"addEventListener\('load',\s*play", script);
        Assert.Contains("prefers-reduced-motion: reduce", script);
        Assert.DoesNotContain("typeof lottie.play === 'function'", script);
    }

    [Fact]
    public void LottiePlayer_BundledViaNpmNotCdn()
    {
        // The lottie-player component is vendored through npm + esbuild (bundled
        // into login.js), not pulled from a CDN. Guards against the cdnjs <script>
        // creeping back into _Layout or the side-effect import being dropped
        // (either would leave <lottie-player> unregistered and the mascot inert).
        var layout = Read("Views", "Shared", "_Layout.cshtml");
        Assert.DoesNotContain("lottie-player", layout);
        Assert.DoesNotContain("cdnjs.cloudflare.com", layout);
        Assert.Contains("import '@lottiefiles/lottie-player'", Read("src", "login.ts"));
        Assert.Contains("@lottiefiles/lottie-player", Read("package.json"));
    }

    [Fact]
    public void TenantLogoView_RendersBothVariantsForLocalLogo()
    {
        var view = Read("Views", "Shared", "Components", "TenantLogo", "Default.cshtml");
        Assert.Contains("logo-light", view);
        Assert.Contains("logo-dark", view);
        Assert.Contains("Model.DarkLogoUrl", view);
        Assert.Contains("s3-logo", view);
    }

    [Fact]
    public void TenantLogoView_S3ImageFallsBackOnLoadError()
    {
        var view = Read("Views", "Shared", "Components", "TenantLogo", "Default.cshtml");
        // A failed S3 load must swap to the tenant fallback, not leave a broken icon.
        Assert.Contains("Model.FallbackLogoUrl", view);
        Assert.Contains("this.src='", view);
        Assert.Contains("this.onerror=null", view);
    }

    [Fact]
    public void DarkScheme_KeepsHomePattern()
    {
        var body = DarkSchemeMixinRegex().Match(_siteLess).Groups["b"].Value;
        var home = HomePatternRegex().Match(body);
        Assert.True(home.Success, "dark body#home rule not found");
        Assert.DoesNotMatch(@"background-image\s*:\s*none", home.Groups["b"].Value);
        Assert.Matches(@"background-image:\s*url\(""data:image/svg\+xml", home.Groups["b"].Value);
        Assert.Contains("stroke='%23ffffff'", home.Groups["b"].Value);
    }

    [Fact]
    public void ThemeToggle_PresentInLayout()
    {
        var layout = Read("Views", "Shared", "_Layout.cshtml");
        Assert.Contains("id=\"themeToggle\"", layout);
        Assert.Contains("id=\"themeMenu\"", layout);
        Assert.Contains("data-theme-choice=\"system\"", layout);
        Assert.Contains("localStorage.getItem('theme')", layout);
        Assert.Contains("document.documentElement.dataset.theme", layout);
    }

    // ---- Rec #4: shape tokens instead of magic numbers ---------------------

    [Fact]
    public void Table_TextColourUsesOnSurfaceToken()
    {
        Assert.Matches(@"\.table\s*\{[^}]*--bs-table-color:\s*var\(--dd-on-surface\)", _siteLess);
    }

    [Fact]
    public void ScrollbarThumb_UsesRadiusToken()
    {
        Assert.Matches(@"::-webkit-scrollbar-thumb\s*\{[^}]*border-radius:\s*var\(--radius-xs\)", _siteLess);
    }

    [Fact]
    public void ProfileMenuBadgeIcon_UsesCircleToken()
    {
        // The profile menu's email-header badge icon (nested .profile-email >
        // .dfrnt-icon) carries the circle token.
        Assert.Matches(
            @"\.profile-email\s*\{[\s\S]*?\.dfrnt-icon\s*\{[^}]*border-radius:\s*var\(--radius-circle\)",
            _siteLess);
    }

    [GeneratedRegex("<md-[a-z-]+")]
    private static partial Regex MdCustomElement();

    // Captures the .dd-dark-scheme() mixin body, up to the unique comment that
    // follows its closing brace.
    [GeneratedRegex(@"\.dd-dark-scheme\(\)\s*\{(?<b>.*?)// Explicit user choice", RegexOptions.Singleline)]
    private static partial Regex DarkSchemeMixinRegex();
    [GeneratedRegex("#0e8f4d")]
    private static partial Regex ColorRegex();
    [GeneratedRegex(@"\.fuel-chip-current\s*\{(?<b>[^}]*)\}")]
    private static partial Regex FuelChipCurrentRegex();
    [GeneratedRegex(@"body#home\s*\{(?<b>[^}]*)\}", RegexOptions.Singleline)]
    private static partial Regex HomePatternRegex();
}
