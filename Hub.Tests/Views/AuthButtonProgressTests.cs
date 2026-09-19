namespace Hub.Tests.Views;

// Guards the login/reset button icons and the login-submit progress bar after
// the Bootstrap migration. The buttons deliberately use a `.button-label` span
// instead of the whole button's text so the JS "Logging in..."/"Processing..."
// swap (setSubmitting) updates the label WITHOUT clobbering the leading
// <dfrnt-icon> — a plain button.textContent assignment would delete the icon.
// These tests fail if that structure regresses.
public class AuthButtonProgressTests
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
    public void LoginView_HasIndeterminateProgressBar()
    {
        var view = Read("Views", "Account", "Login.cshtml");
        Assert.Contains("id=\"loginProgress\"", view);
        // The old <md-linear-progress indeterminate> is now a CSS-animated bar.
        Assert.Contains("class=\"auth-progress-bar\"", view);
        Assert.DoesNotContain("md-linear-progress", view);
    }

    [Fact]
    public void LoginButton_HasLeadingIcon_AndLabelSpan()
    {
        var view = Read("Views", "Account", "Login.cshtml");
        var buttonStart = view.IndexOf("id=\"loginButton\"", StringComparison.Ordinal);
        Assert.True(buttonStart >= 0, "loginButton not found");
        var buttonEnd = view.IndexOf("</button>", buttonStart, StringComparison.Ordinal);
        Assert.True(buttonEnd > buttonStart, "loginButton not closed");

        var button = view[buttonStart..buttonEnd];
        Assert.Contains("<dfrnt-icon set=\"lucide\" name=\"log-in\"", button);
        Assert.Contains("class=\"button-label\"", button);
    }

    [Fact]
    public void ResetButton_HasLeadingIcon_AndLabelSpan()
    {
        var view = Read("Views", "Account", "ResetPassword.cshtml");
        var buttonStart = view.IndexOf("id=\"resetButton\"", StringComparison.Ordinal);
        Assert.True(buttonStart >= 0, "resetButton not found");
        var buttonEnd = view.IndexOf("</button>", buttonStart, StringComparison.Ordinal);
        Assert.True(buttonEnd > buttonStart, "resetButton not closed");

        var button = view[buttonStart..buttonEnd];
        Assert.Contains("<dfrnt-icon set=\"lucide\" name=\"key-round\"", button);
        Assert.Contains("class=\"button-label\"", button);
    }

    [Fact]
    public void ResetButton_IsCentered()
    {
        // The submit button sits in a text-center wrapper, matching the login page.
        var view = Read("Views", "Account", "ResetPassword.cshtml");
        var wrapperStart = view.LastIndexOf("<div", view.IndexOf("id=\"resetButton\"", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.True(wrapperStart >= 0, "resetButton wrapper not found");
        var wrapperTag = view[wrapperStart..view.IndexOf('>', wrapperStart)];
        Assert.Contains("text-center", wrapperTag);
    }

    [Fact]
    public void SubmitScripts_UpdateLabelSpan_NotButtonTextContent()
    {
        // button.textContent = ... would wipe the leading <dfrnt-icon>. Both submit
        // handlers must drive the .button-label span instead.
        foreach (var script in new[] { "login.ts", "reset-password.ts" })
        {
            var source = Read("src", script);
            Assert.Contains(".button-label", source);
            Assert.DoesNotContain("button.textContent =", source);
        }
    }

    [Fact]
    public void LoginStyles_AnimateTheProgressBar()
    {
        var less = Read("wwwroot", "css", "login.less");
        Assert.Contains(".auth-progress", less);
        Assert.Contains("&.active", less);
        // The indeterminate sweep keyframes replace md-linear-progress.
        Assert.Contains("authProgressIndeterminate", less);
        Assert.DoesNotContain("md-linear-progress", less);
    }
}
