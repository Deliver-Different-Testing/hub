using System.Text.RegularExpressions;

namespace Hub.Tests.Views;

public partial class EmailValidationRegexTests
{
    private readonly Regex _loginRegex;
    private readonly Regex _forgotPasswordRegex;

    public EmailValidationRegexTests()
    {
        var loginPath = FindFile("src", "login.ts");
        if (loginPath is null)
        {
            Assert.Skip("login.ts not found relative to test output directory");
        }

        var forgotPasswordPath = FindFile("src", "forgot-password.ts");
        if (forgotPasswordPath is null)
        {
            Assert.Skip("forgot-password.ts not found relative to test output directory");
        }

        _loginRegex = ExtractEmailRegex(File.ReadAllText(loginPath));
        _forgotPasswordRegex = ExtractEmailRegex(File.ReadAllText(forgotPasswordPath));
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

    private static Regex ExtractEmailRegex(string fileContent)
    {
        // Match the regex literal assigned in validateEmail: /^...$/
        var match = MyRegex().Match(fileContent);
        Assert.True(match.Success, "Could not find email validation regex in file");
        return new Regex(match.Groups[1].Value);
    }

    // Regression: a typo \.[[^ instead of \.([^ broke emails with periods in the local part
    [Theory]
    [InlineData("john.doe@email.com")]
    [InlineData("first.last@example.co.nz")]
    [InlineData("a.b.c@test.com")]
    public void LoginRegex_AcceptsEmailsWithPeriods(string email) => Assert.Matches(_loginRegex, email);

    [Theory]
    [InlineData("john.doe@email.com")]
    [InlineData("first.last@example.co.nz")]
    [InlineData("a.b.c@test.com")]
    public void ForgotPasswordRegex_AcceptsEmailsWithPeriods(string email) => Assert.Matches(_forgotPasswordRegex, email);

    [Theory]
    [InlineData("user@example.com")]
    [InlineData("test@test.co.nz")]
    public void LoginRegex_AcceptsSimpleEmails(string email) => Assert.Matches(_loginRegex, email);

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("@missing-local.com")]
    [InlineData("missing-domain@")]
    public void LoginRegex_RejectsInvalidEmails(string email) => Assert.DoesNotMatch(_loginRegex, email);
    // Accept either name — login.ts uses EMAIL_REGEX; forgot-password.ts still uses `re`.
    [GeneratedRegex("const (?:re|EMAIL_REGEX) = /(.+)/;")]
    private static partial Regex MyRegex();
}
