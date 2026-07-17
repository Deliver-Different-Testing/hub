namespace Hub.Tests.Views;

// Guards the zxcvbn-ts password-strength wiring on the reset-password flow.
// The strength gate is layered ON TOP of the composition regex (which mirrors
// the backend [StrongPassword] attribute) — both must survive, or the client
// would accept passwords the server rejects, or drop the guessability check.
public class ResetPasswordStrengthTests
{
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

    [Fact]
    public void ResetView_HasStrengthMeterMarkup()
    {
        var view = Read("Views", "Account", "ResetPassword.cshtml");
        Assert.Contains("id=\"passwordStrength\"", view);
        Assert.Contains("id=\"strengthBar\"", view);
        Assert.Contains("<md-linear-progress", view);
        Assert.Contains("id=\"strengthLabel\"", view);
        Assert.Contains("id=\"strengthFeedback\"", view);
    }

    [Fact]
    public void ResetScript_LazyLoadsZxcvbn_ViaDynamicImport()
    {
        // The heavy English dictionary must be pulled in on demand via dynamic
        // import() (code-split chunk), not statically imported into the entry.
        var source = Read("src", "reset-password.ts");
        Assert.Matches(@"import\('@zxcvbn-ts/core'\)", source);
        Assert.Matches(@"import\('@zxcvbn-ts/language-common'\)", source);
        Assert.Matches(@"import\('@zxcvbn-ts/language-en'\)", source);
        Assert.Contains("new ZxcvbnFactory", source);

        // A static value import of core would defeat the splitting — only the
        // type-only import is allowed.
        Assert.DoesNotContain("import { ZxcvbnFactory }", source);
    }

    [Fact]
    public void ResetView_LoadsScriptAsModule()
    {
        // ESM output (required for code splitting) must be loaded as a module.
        var view = Read("Views", "Account", "ResetPassword.cshtml");
        Assert.Matches("""
                       <script type="module" src="~/dist/reset-password\.js"
                       """, view);
    }

    [Fact]
    public void ResetScript_GatesSubmit_OnStrengthScore()
    {
        // The submit handler must require the minimum zxcvbn score in addition
        // to the composition regex and the confirm match.
        var source = Read("src", "reset-password.ts");
        Assert.Contains("MIN_STRENGTH_SCORE", source);
        Assert.Matches("score < MIN_STRENGTH_SCORE", source);
    }

    [Fact]
    public void ResetScript_KeepsCompositionRegex_MatchingBackend()
    {
        // Backend StrongPasswordAttribute rejects non-compliant passwords, so the
        // client must keep enforcing the same composition rule.
        var source = Read("src", "reset-password.ts");
        Assert.Contains("PASSWORD_REGEX", source);
        Assert.Contains("isStrongPassword", source);
    }

    [Fact]
    public void PackageJson_DependsOnZxcvbnTs()
    {
        var pkg = Read("package.json");
        Assert.Contains("@zxcvbn-ts/core", pkg);
        Assert.Contains("@zxcvbn-ts/language-common", pkg);
        Assert.Contains("@zxcvbn-ts/language-en", pkg);
    }

    [Fact]
    public void SiteStyles_StyleTheStrengthMeter()
    {
        var less = Read("wwwroot", "css", "site.less");
        Assert.Contains(".password-strength", less);
        Assert.Contains("data-score", less);
    }
}