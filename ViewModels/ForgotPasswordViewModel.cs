using System.ComponentModel.DataAnnotations;

namespace Hub.ViewModels;

public sealed class ForgotPasswordViewModel
{
    [Required]
    [Display(Name = "Email")]
    public required string Email { get; init; }
}