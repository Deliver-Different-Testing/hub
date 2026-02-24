using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Hub.ViewModels;

public partial class StrongPasswordAttribute : ValidationAttribute
{
    protected override ValidationResult IsValid(object value, ValidationContext validationContext)
    {
        var password = value as string;
        if (string.IsNullOrWhiteSpace(password))
            return new ValidationResult("Password is required.");

        var regex = PasswordRegex();
        return !regex.IsMatch(password) ? new ValidationResult("Password must be at least 8 characters long, contain at least one uppercase letter, one lowercase letter, one number, and one special character.") : ValidationResult.Success;
    }

    [GeneratedRegex(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^\da-zA-Z]).{8,}$")]
    private static partial Regex PasswordRegex();
}
public class ResetPasswordViewModel
{
    [Required]
    [EmailAddress]
    [Display(Name = "Email")]
    public string Email { get; init; }

    [Required]
    [StringLength(100, ErrorMessage = "The {0} must be at least {2} characters long.", MinimumLength = 8)]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    [StrongPassword]
    public string Password { get; init; }

    [DataType(DataType.Password)]
    [Display(Name = "Confirm password")]
    [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
    public string ConfirmPassword { get; init; }

    public string Code { get; init; }
}