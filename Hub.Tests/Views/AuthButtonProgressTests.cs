namespace Hub.Tests.Views;

// Guards the MD3 login/reset button icons and the login-submit progress bar.
// The buttons deliberately use a `.button-label` span instead of the whole
// button's text so the JS "Logging in..."/"Processing..." swap (setSubmitting)
// updates the label WITHOUT clobbering the slotted <md-icon> — a plain
// button.textContent assignment would delete the icon. These tests fail if that
// structure regresses.
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
        Assert.Contains("<md-linear-progress indeterminate", view);
    }

    [Fact]
    public void LoginButton_HasLeadingIcon_AndLabelSpan()
    {
        var view = Read("Views", "Account", "Login.cshtml");
        var buttonStart = view.IndexOf("id=\"loginButton\"", StringComparison.Ordinal);
        Assert.True(buttonStart >= 0, "loginButton not found");
        var buttonEnd = view.IndexOf("</md-filled-button>", buttonStart, StringComparison.Ordinal);
        Assert.True(buttonEnd > buttonStart, "loginButton not closed");

        var button = view[buttonStart..buttonEnd];
        Assert.Contains("<md-icon slot=\"icon\"", button);
        Assert.Contains(">login</md-icon>", button);
        Assert.Contains("class=\"button-label\"", button);
    }

    [Fact]
    public void ResetButton_HasLeadingIcon_AndLabelSpan()
    {
        var view = Read("Views", "Account", "ResetPassword.cshtml");
        var buttonStart = view.IndexOf("id=\"resetButton\"", StringComparison.Ordinal);
        Assert.True(buttonStart >= 0, "resetButton not found");
        var buttonEnd = view.IndexOf("</md-filled-button>", buttonStart, StringComparison.Ordinal);
        Assert.True(buttonEnd > buttonStart, "resetButton not closed");

        var button = view[buttonStart..buttonEnd];
        Assert.Contains("<md-icon slot=\"icon\"", button);
        Assert.Contains(">lock_reset</md-icon>", button);
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
        // button.textContent = ... would wipe the slotted <md-icon>. Both submit
        // handlers must drive the .button-label span instead.
        foreach (var script in new[] { "login.ts", "reset-password.ts" })
        {
            var source = Read("src", script);
            Assert.Contains(".button-label", source);
            Assert.DoesNotContain("button.textContent =", source);
        }
    }

    [Fact]
    public void Material_ImportsLinearProgress()
    {
        var material = Read("src", "material.ts");
        Assert.Contains("@material/web/progress/linear-progress.js", material);
    }

    [Fact]
    public void LoginStyles_StyleTheProgressBar()
    {
        var less = Read("wwwroot", "css", "login.less");
        Assert.Contains(".auth-progress", less);
        Assert.Contains("&.active", less);
    }
}
