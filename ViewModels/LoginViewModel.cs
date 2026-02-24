using System.ComponentModel.DataAnnotations;

namespace Hub.ViewModels;

public class LoginViewModel
{
    [Required]
    [Display(Name = "Email")]
    [EmailAddress]
    public string Email { get; init; }

    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; init; }

    [Display(Name = "Remember me?")] public bool RememberMe { get; init; }

    [Display(Name = "Login Type")] public bool IsCourierLogin { get; init; } // Default to Customer Login
}