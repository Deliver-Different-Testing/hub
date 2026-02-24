using System.ComponentModel.DataAnnotations;
using FluentAssertions;
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
    public void NullPassword_ReturnsError() => Validate(null).Should().NotBe(ValidationResult.Success);

    [Fact]
    public void EmptyPassword_ReturnsError() => Validate("").Should().NotBe(ValidationResult.Success);

    [Fact]
    public void WhitespacePassword_ReturnsError() => Validate("   ").Should().NotBe(ValidationResult.Success);

    [Fact]
    public void MissingUppercase_ReturnsError() => Validate("password1!").Should().NotBe(ValidationResult.Success);

    [Fact]
    public void MissingLowercase_ReturnsError() => Validate("PASSWORD1!").Should().NotBe(ValidationResult.Success);

    [Fact]
    public void MissingDigit_ReturnsError() => Validate("Password!").Should().NotBe(ValidationResult.Success);

    [Fact]
    public void MissingSpecialChar_ReturnsError() => Validate("Password1").Should().NotBe(ValidationResult.Success);

    [Fact]
    public void TooShort_ReturnsError() => Validate("Pa1!").Should().NotBe(ValidationResult.Success);

    [Fact]
    public void ValidPassword_Exact8Chars_ReturnsSuccess() => Validate("Pa$$w0rd").Should().Be(ValidationResult.Success);

    [Fact]
    public void ValidPassword_Long_ReturnsSuccess() => Validate("MyStr0ng!Password2024").Should().Be(ValidationResult.Success);

    [Fact]
    public void ValidPassword_WithMultipleSpecialChars_ReturnsSuccess() => Validate("P@ssw0rd!#").Should().Be(ValidationResult.Success);
}
