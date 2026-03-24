using System.ComponentModel.DataAnnotations;
using Hub.ViewModels;

namespace Hub.Tests.Shared;

public class StrongPasswordAttributeTests
{
    private readonly StrongPasswordAttribute _attribute = new();

    private ValidationResult? Validate(string? password)
    {
        var context = new ValidationContext(new object()) { MemberName = "Password" };
        return _attribute.GetValidationResult(password, context);
    }

    [Fact]
    public void NullPassword_ReturnsError() => Assert.NotEqual(ValidationResult.Success, Validate(null));

    [Fact]
    public void EmptyPassword_ReturnsError() => Assert.NotEqual(ValidationResult.Success, Validate(""));

    [Fact]
    public void WhitespacePassword_ReturnsError() => Assert.NotEqual(ValidationResult.Success, Validate("   "));

    [Fact]
    public void MissingUppercase_ReturnsError() => Assert.NotEqual(ValidationResult.Success, Validate("password1!"));

    [Fact]
    public void MissingLowercase_ReturnsError() => Assert.NotEqual(ValidationResult.Success, Validate("PASSWORD1!"));

    [Fact]
    public void MissingDigit_ReturnsError() => Assert.NotEqual(ValidationResult.Success, Validate("Password!"));

    [Fact]
    public void MissingSpecialChar_ReturnsError() => Assert.NotEqual(ValidationResult.Success, Validate("Password1"));

    [Fact]
    public void TooShort_ReturnsError() => Assert.NotEqual(ValidationResult.Success, Validate("Pa1!"));

    [Fact]
    public void ValidPassword_Exact8Chars_ReturnsSuccess() => Assert.Equal(ValidationResult.Success, Validate("Pa$$w0rd"));

    [Fact]
    public void ValidPassword_Long_ReturnsSuccess() => Assert.Equal(ValidationResult.Success, Validate("MyStr0ng!Password2024"));

    [Fact]
    public void ValidPassword_WithMultipleSpecialChars_ReturnsSuccess() => Assert.Equal(ValidationResult.Success, Validate("P@ssw0rd!#"));
}
