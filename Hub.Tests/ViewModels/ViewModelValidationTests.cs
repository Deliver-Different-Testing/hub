using System.ComponentModel.DataAnnotations;
using Hub.ViewModels;

namespace Hub.Tests.ViewModels;

public class ViewModelValidationTests
{
    private static List<ValidationResult> ValidateModel(object model)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(model);
        Validator.TryValidateObject(model, context, results, true);
        return results;
    }

    // LoginViewModel tests
    [Fact]
    public void LoginViewModel_Valid_NoErrors()
    {
        var model = new LoginViewModel { Email = "user@test.com", Password = "password" };

        Assert.Empty(ValidateModel(model));
    }

    [Fact]
    public void LoginViewModel_MissingEmail_HasError()
    {
        var model = new LoginViewModel { Email = null!, Password = "password" };

        Assert.Contains(ValidateModel(model), r => r.MemberNames.Contains("Email"));
    }

    [Fact]
    public void LoginViewModel_MissingPassword_HasError()
    {
        var model = new LoginViewModel { Email = "user@test.com", Password = null! };

        Assert.Contains(ValidateModel(model), r => r.MemberNames.Contains("Password"));
    }

    [Fact]
    public void LoginViewModel_InvalidEmailFormat_HasError()
    {
        var model = new LoginViewModel { Email = "not-an-email", Password = "password" };

        Assert.Contains(ValidateModel(model), r => r.MemberNames.Contains("Email"));
    }

    // ResetPasswordViewModel tests
    [Fact]
    public void ResetPasswordViewModel_Valid_NoErrors()
    {
        var model = new ResetPasswordViewModel
        {
            Email = "user@test.com",
            Password = "Pa$$w0rd!",
            ConfirmPassword = "Pa$$w0rd!",
            Code = "abc"
        };

        Assert.Empty(ValidateModel(model));
    }

    [Fact]
    public void ResetPasswordViewModel_PasswordTooShort_HasError()
    {
        var model = new ResetPasswordViewModel
        {
            Email = "user@test.com",
            Password = "Pa$1",
            ConfirmPassword = "Pa$1",
            Code = "abc"
        };

        Assert.NotEmpty(ValidateModel(model));
    }

    [Fact]
    public void ResetPasswordViewModel_PasswordMismatch_HasError()
    {
        var model = new ResetPasswordViewModel
        {
            Email = "user@test.com",
            Password = "Pa$$w0rd!",
            ConfirmPassword = "DifferentPa$$w0rd!",
            Code = "abc"
        };

        Assert.Contains(ValidateModel(model), r => r.MemberNames.Contains("ConfirmPassword"));
    }

    [Fact]
    public void ResetPasswordViewModel_MissingEmail_HasError()
    {
        var model = new ResetPasswordViewModel
        {
            Email = null!,
            Password = "Pa$$w0rd!",
            ConfirmPassword = "Pa$$w0rd!",
            Code = "test-code"
        };

        Assert.Contains(ValidateModel(model), r => r.MemberNames.Contains("Email"));
    }

    // ForgotPasswordViewModel tests
    [Fact]
    public void ForgotPasswordViewModel_Valid_NoErrors()
    {
        var model = new ForgotPasswordViewModel { Email = "user@test.com" };

        Assert.Empty(ValidateModel(model));
    }

    [Fact]
    public void ForgotPasswordViewModel_MissingEmail_HasError()
    {
        var model = new ForgotPasswordViewModel { Email = null! };

        Assert.Contains(ValidateModel(model), r => r.MemberNames.Contains("Email"));
    }

    [Fact]
    public void ResetPasswordViewModel_WeakPassword_HasError()
    {
        var model = new ResetPasswordViewModel
        {
            Email = "user@test.com",
            Password = "weakpassword",
            ConfirmPassword = "weakpassword",
            Code = "abc"
        };

        Assert.NotEmpty(ValidateModel(model));
    }
}
