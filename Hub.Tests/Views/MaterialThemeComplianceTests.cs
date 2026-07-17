using System.Text.RegularExpressions;

namespace Hub.Tests.Views;

// Guards the MD3-compliance fixes applied to the @material/web migration:
//   1. the --md-sys-* -> --dd-* colour bridge exposes every on-token pair used
//      by the imported components (missing on-tokens break tonal pairing);
//   2. no hardcoded hex leaks back into the themed surfaces (they must resolve
//      through --dd-* tokens so per-tenant theming tracks them);
//   3. the tenant-selector filled button paints a contrasting container so it
//      separates from the (primary-coloured) navbar;
//   4. shape corners use the radius token scale, not magic numbers;
//   5. dark mode is present: a neutrals-only scheme gated on data-theme +
//      prefers-color-scheme, driven by a Light / Dark / System toggle.
public partial class MaterialThemeComplianceTests
{
    private readonly string _siteLess = Read("wwwroot", "css", "site.less");
    private readonly string _materialTheme = Read("wwwroot", "css", "material-theme.less");
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

    // ---- Rec #1: bridge exposes the previously-missing on-token pairs --------

    [Theory]
    [InlineData("--md-sys-color-on-secondary", "--dd-on-secondary")]
    [InlineData("--md-sys-color-on-error", "--dd-on-error")]
    [InlineData("--md-sys-color-surface-container-lowest", "--dd-surface-container-lowest")]
    public void Bridge_MapsMissingRole(string sysToken, string ddToken)
    {
        Assert.Matches($@"{Regex.Escape(sysToken)}\s*:\s*var\({Regex.Escape(ddToken)}\)", _materialTheme);
    }

    [Theory]
    [InlineData("--dd-on-secondary")]
    [InlineData("--dd-on-error")]
    public void Palette_DefinesNewOnTokens(string token)
    {
        // The bridge references these; they must be declared in the :root palette
        // or the mapped role resolves to nothing.
        Assert.Matches($@"{Regex.Escape(token)}\s*:\s*#", _siteLess);
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
        // Source .less: the chip background uses the rgb token directly, and its
        // text colour goes through the @color_9 LESS var which must resolve to the
        // success-text token (not the old #2e7d32 hex).
        var block = FuelChipCurrentRegex().Match(_siteLess).Groups["b"].Value;
        Assert.Contains("rgba(var(--dd-success-rgb)", block);
        Assert.DoesNotContain("rgba(76, 175, 80", block);

        // @color_9 (the chip text colour) must map to the token, and the #2e7d32
        // hex must survive ONLY as the token declaration.
        Assert.Matches(@"@color_9\s*:\s*var\(--dd-success-text\)", _siteLess);
        Assert.Matches(@"--dd-success-text\s*:\s*#2e7d32", _siteLess);
        Assert.Single(ColorRegex().Matches(_siteLess));
    }

    [Fact]
    public void PasswordStrength_AmberUsesWarningToken()
    {
        // The score-2 strength bar must reference the token...
        Assert.Matches(
            """data-score="2"\][^{]*\{[^}]*--md-linear-progress-active-indicator-color:\s*var\(--dd-warning-strong\)""",
            _siteLess);
        // ...and the #f59e0b hex must survive ONLY as the token declaration.
        Assert.Matches(@"--dd-warning-strong\s*:\s*#f59e0b", _siteLess);
        Assert.Single(Regex.Matches(_siteLess, "#f59e0b"));
    }

    [Fact]
    public void LoginButton_TextUsesOnPrimaryNotWhite()
    {
        // The three #fff literals on #loginButton (spinner + hover/active) must
        // resolve through the token so the yellow tenant gets dark-on-yellow text.
        Assert.DoesNotContain("#fff", _loginLess);
        Assert.Contains("var(--dd-on-primary)", _loginLess);
    }

    // ---- Rec #3: tenant button gets a contrasting container -----------------

    [Fact]
    public void TenantDropdown_OverridesContainerColor()
    {
        var block = TenantDropdownRegex().Match(_siteLess).Groups["b"].Value;
        Assert.Matches(
            @"--md-filled-button-container-color\s*:\s*rgba\(var\(--dd-on-primary-rgb\)",
            block);
    }

    // ---- Dark mode: neutrals-only scheme + Light/Dark/System toggle ---------

    [Fact]
    public void DarkScheme_DefinesNeutralOverrides()
    {
        // The dark scheme flips the neutral surface roles and follows the OS.
        Assert.Contains("prefers-color-scheme", _siteLess);
        Assert.Matches(@"--dd-surface\s*:\s*#2c2a30", _siteLess);
        Assert.Matches(@"--dd-on-surface\s*:\s*rgba\(255, 255, 255, \.9\)", _siteLess);
    }

    [Fact]
    public void DarkScheme_GatedByBothTriggers()
    {
        // Explicit user choice via [data-theme=dark] wins; absent an explicit
        // light override, prefers-color-scheme drives the default.
        Assert.Contains("[data-theme=\"dark\"]", _siteLess);
        Assert.Contains(":not([data-theme=\"light\"])", _siteLess);
    }

    [Fact]
    public void DarkScheme_KeepsBrandLight()
    {
        // Neutrals-only: the dark mixin must not redefine the brand/primary
        // tokens — the navbar chrome assumes a light-branded bar.
        var mixin = DarkSchemeMixinRegex().Match(_siteLess);
        Assert.True(mixin.Success, "dark-scheme mixin not found");
        Assert.DoesNotMatch(@"--dd-primary\b\s*:", mixin.Groups["b"].Value);
    }

    [Fact]
    public void DarkScheme_AssetSwapsLocalLogo()
    {
        // The in-page hub logo is asset-swapped (light-wordmark variant), NOT
        // plated: the dark mixin toggles the two .hub-brand-logo <img> variants
        // and puts no box behind them.
        var body = DarkSchemeMixinRegex().Match(_siteLess).Groups["b"].Value;
        Assert.Matches(@"\.hub-brand-logo\.logo-light\s*\{[^}]*display:\s*none", body);
        Assert.Matches(@"\.hub-brand-logo\.logo-dark\s*\{[^}]*display:\s*block", body);
        Assert.DoesNotMatch(@"\.hub-brand-logo\s*\{[^}]*background", body);
        // The light artwork is the default (hidden dark variant) outside the mixin.
        Assert.Matches(@"\.logo-dark\s*\{\s*display:\s*none", _siteLess);
        // The old white --dd-plate token (the box that read as "white on black")
        // is retired along with its usages.
        Assert.DoesNotContain("--dd-plate", _siteLess);
    }

    [Fact]
    public void DarkScheme_SwapsAppBarLogoForDefaultTenant()
    {
        // In dark mode the app-bar .logo swaps to its white-wordmark variant so it
        // reads on the blue default bar. The (?<![\w-]) guard keeps these from
        // matching the .hub-brand-logo.logo-* rules asserted above.
        var body = DarkSchemeMixinRegex().Match(_siteLess).Groups["b"].Value;
        Assert.Matches(@"(?<![\w-])\.logo\.logo-light\s*\{[^}]*display:\s*none", body);
        Assert.Matches(@"(?<![\w-])\.logo\.logo-dark\s*\{[^}]*display:\s*block", body);
        // Non-US tenants keep the yellow bar, where the white wordmark washes out —
        // the swap is re-hidden for them so they stay on the black artwork.
        Assert.Matches(
            """body:not\(\[data-tenant-country="US"\]\):not\(\[data-tenant-country=""\]\)\s*\{\s*\.logo\.logo-light\s*\{[^{}]*\}\s*\.logo\.logo-dark\s*\{[^{}]*display:\s*none""",
            body);
    }

    [Fact]
    public void DarkScheme_AppBarChromeUsesOnPrimary()
    {
        // The bare navbar chrome (text + theme-toggle icon) flips from pinned black
        // to --dd-on-primary in dark mode: white on the blue bar, dark on yellow.
        var body = DarkSchemeMixinRegex().Match(_siteLess).Groups["b"].Value;
        Assert.Matches(
            @"\.tenant-label,\s*\.username,\s*\.dropdown-arrow\s*\{[^}]*color:\s*var\(--dd-on-primary\)",
            body);
        Assert.Matches(
            @"#themeToggle\s*\{[^}]*--md-icon-button-icon-color:\s*var\(--dd-on-primary\)",
            body);
        // The organisation selector button follows the same chrome colour so it
        // doesn't read as black text next to the white navbar chrome.
        Assert.Matches(
            @"#tenantDropdown\s*\{[^}]*--md-filled-button-label-text-color:\s*var\(--dd-on-primary\)",
            body);
    }

    [Fact]
    public void DarkScheme_LiftsAppCardHoverTitle()
    {
        // On the dark cards the app-card hover title lifts from the dark
        // --dd-primary-active to the brighter --dd-primary so it stays legible.
        // The icon tiles are intentionally left untouched.
        var body = DarkSchemeMixinRegex().Match(_siteLess).Groups["b"].Value;
        Assert.Matches(
            @"\.app-card:hover\s*\.app-card-title\s*\{[^}]*color:\s*var\(--dd-primary\)",
            body);
        Assert.DoesNotMatch(@"\.app-card-icon\s*\{", body);
    }

    [Fact]
    public void DarkScheme_PlatesArbitraryS3LogoOnly()
    {
        // Arbitrary S3 art we can't recolour sits on the MD3 inverse-surface chip
        // (a deliberate light container), not the old white --dd-plate box.
        var body = DarkSchemeMixinRegex().Match(_siteLess).Groups["b"].Value;
        Assert.Matches(@"\.s3-logo\s*\{[^}]*background:\s*var\(--dd-inverse-surface\)", body);
    }

    [Fact]
    public void DarkScheme_SwapsLoginMascotVariant()
    {
        // The mascot is asset-swapped like the logo: a dark-variant Lottie whose
        // baked white backing is knocked back to a translucent panel, revealed only
        // in dark mode — so no hard plate sits behind it.
        var body = DarkSchemeMixinRegex().Match(_siteLess).Groups["b"].Value;
        Assert.Matches(@"\.auth-lottie\.auth-lottie-light\s*\{[^}]*display:\s*none", body);
        Assert.Matches(@"\.auth-lottie\.auth-lottie-dark\s*\{[^}]*display:\s*block", body);
        Assert.DoesNotMatch(@"\.auth-lottie[^{]*\{[^}]*background:\s*var\(--dd-inverse-surface\)", body);
        // The dark player is hidden by default (light mode), out-specifying login.css.
        Assert.Matches(@"\.auth-lottie\.auth-lottie-dark\s*\{\s*display:\s*none", _siteLess);
    }

    [Fact]
    public void LoginView_RendersBothMascotVariants()
    {
        var view = Read("Views", "Account", "Login.cshtml");
        Assert.Contains("auth-lottie-light", view);
        Assert.Contains("auth-lottie-dark", view);
        Assert.Contains("(2)_dark.json", view);
    }

    [Fact]
    public void TenantLogoView_RendersBothVariantsForLocalLogo()
    {
        // The component ships light+dark <img>s for a local logo (CSS reveals the
        // right one) and a single plain <img> for arbitrary S3 art.
        var view = Read("Views", "Shared", "Components", "TenantLogo", "Default.cshtml");
        Assert.Contains("logo-light", view);
        Assert.Contains("logo-dark", view);
        Assert.Contains("Model.DarkLogoUrl", view);
        Assert.Contains("s3-logo", view);
    }

    [Fact]
    public void DarkScheme_KeepsHomePattern()
    {
        // Dark mode keeps the App Hub geometric backdrop rather than dropping to a
        // flat surface: the dark body#home override must set a real background-image
        // (white-stroke variant of the light pattern) and must NOT blank it out.
        var body = DarkSchemeMixinRegex().Match(_siteLess).Groups["b"].Value;
        var home = HomePatternRegex().Match(body);
        Assert.True(home.Success, "dark body#home rule not found");
        Assert.DoesNotMatch(@"background-image\s*:\s*none", home.Groups["b"].Value);
        // The tiled SVG pattern flips to white strokes so it reads on the dark surface.
        Assert.Matches(@"background-image:\s*url\(""data:image/svg\+xml", home.Groups["b"].Value);
        Assert.Contains("stroke='%23ffffff'", home.Groups["b"].Value);
    }

    [Fact]
    public void ThemeToggle_PresentInLayout()
    {
        var layout = Read("Views", "Shared", "_Layout.cshtml");
        // The toggle trigger, its menu, and the three choices.
        Assert.Contains("id=\"themeToggle\"", layout);
        Assert.Contains("id=\"themeMenu\"", layout);
        Assert.Contains("data-theme-choice=\"system\"", layout);
        // The no-flash script applies the saved theme before first paint.
        Assert.Contains("localStorage.getItem('theme')", layout);
        Assert.Contains("document.documentElement.dataset.theme", layout);
    }

    // ---- Rec #5: shape tokens instead of magic numbers ---------------------

    [Fact]
    public void ScrollbarThumb_UsesRadiusToken()
    {
        Assert.Matches(@"::-webkit-scrollbar-thumb\s*\{[^}]*border-radius:\s*var\(--radius-xs\)", _siteLess);
    }

    [Fact]
    public void ProfileMenuBadgeIcon_UsesCircleToken()
    {
        // The profile menu migrated from the custom .profile-menu markup to an
        // md-menu; the email-header badge icon (the disabled item's start icon)
        // is the element that now carries the circle token.
        Assert.Matches(
            """md-menu-item\[disabled\] md-icon\[slot="start"\]\s*\{[^}]*border-radius:\s*var\(--radius-circle\)""",
            _siteLess);
    }

    // Anchor to the top-level (column-0) navbar rule so the match isn't stolen by
    // the indented `#tenantDropdown` override nested inside the dark-mode scheme.
    [GeneratedRegex(@"^#tenantDropdown\s*\{(?<b>.*?)^\}", RegexOptions.Singleline | RegexOptions.Multiline)]
    private static partial Regex TenantDropdownRegex();

    // Captures the .dd-dark-scheme() mixin body, up to the unique comment that
    // follows its closing brace.
    [GeneratedRegex(@"\.dd-dark-scheme\(\)\s*\{(?<b>.*?)// Explicit user choice", RegexOptions.Singleline)]
    private static partial Regex DarkSchemeMixinRegex();
    [GeneratedRegex("#2e7d32")]
    private static partial Regex ColorRegex();
    [GeneratedRegex(@"\.fuel-chip-current\s*\{(?<b>[^}]*)\}")]
    private static partial Regex FuelChipCurrentRegex();
    [GeneratedRegex(@"body#home\s*\{(?<b>[^}]*)\}", RegexOptions.Singleline)]
    private static partial Regex HomePatternRegex();
}
