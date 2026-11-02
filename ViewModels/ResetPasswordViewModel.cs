using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Hub.ViewModels;

public partial class StrongPasswordAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var password = value as string;
        if (string.IsNullOrWhiteSpace(password))
        {
            return new ValidationResult("Password is required.");
        }

        var regex = PasswordRegex();
        return !regex.IsMatch(password) 
            ? new ValidationResult("Use at least 8 characters, with an uppercase letter, a lowercase letter, a number and a symbol.")
            : ValidationResult.Success;
    }

    [GeneratedRegex(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^\da-zA-Z]).{8,}$")]
    private static partial Regex PasswordRegex();
}
public sealed class ResetPasswordViewModel
{
    [Required]
    [StringLength(100, ErrorMessage = "Password must be 8 to 100 characters.", MinimumLength = 8)]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    [StrongPassword]
    public string Password { get; init; } = string.Empty;

    [DataType(DataType.Password)]
    [Display(Name = "Confirm password")]
    [Compare("Password", ErrorMessage = "Passwords don't match.")]
    public string ConfirmPassword { get; init; } = string.Empty;

    [Required]
    public required string Code { get; init; }
}