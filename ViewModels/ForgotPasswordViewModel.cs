using System.ComponentModel.DataAnnotations;

namespace Hub.ViewModels;

public class ForgotPasswordViewModel
{
    [Required]
    [Display(Name = "Email")]
    public string Email { get; init; }
}