using System.ComponentModel.DataAnnotations;
using FluentAssertions;
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

        ValidateModel(model).Should().BeEmpty();
    }

    [Fact]
    public void LoginViewModel_MissingEmail_HasError()
    {
        var model = new LoginViewModel { Email = null!, Password = "password" };

        ValidateModel(model).Should().Contain(r => r.MemberNames.Contains("Email"));
    }

    [Fact]
    public void LoginViewModel_MissingPassword_HasError()
    {
        var model = new LoginViewModel { Email = "user@test.com", Password = null! };

        ValidateModel(model).Should().Contain(r => r.MemberNames.Contains("Password"));
    }

    [Fact]
    public void LoginViewModel_InvalidEmailFormat_HasError()
    {
        var model = new LoginViewModel { Email = "not-an-email", Password = "password" };

        ValidateModel(model).Should().Contain(r => r.MemberNames.Contains("Email"));
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

        ValidateModel(model).Should().BeEmpty();
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

        ValidateModel(model).Should().NotBeEmpty();
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

        ValidateModel(model).Should().Contain(r => r.MemberNames.Contains("ConfirmPassword"));
    }

    [Fact]
    public void ResetPasswordViewModel_MissingEmail_HasError()
    {
        var model = new ResetPasswordViewModel
        {
            Email = null!,
            Password = "Pa$$w0rd!",
            ConfirmPassword = "Pa$$w0rd!"
        };

        ValidateModel(model).Should().Contain(r => r.MemberNames.Contains("Email"));
    }

    // ForgotPasswordViewModel tests
    [Fact]
    public void ForgotPasswordViewModel_Valid_NoErrors()
    {
        var model = new ForgotPasswordViewModel { Email = "user@test.com" };

        ValidateModel(model).Should().BeEmpty();
    }

    [Fact]
    public void ForgotPasswordViewModel_MissingEmail_HasError()
    {
        var model = new ForgotPasswordViewModel { Email = null! };

        ValidateModel(model).Should().Contain(r => r.MemberNames.Contains("Email"));
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

        ValidateModel(model).Should().NotBeEmpty();
    }
}
