using System.ComponentModel.DataAnnotations;

namespace UrgentHub.ViewModels
{
    public class ForgotPasswordViewModel
    {
        [Required]
        [Display(Name = "Email")]
        public string Email { get; set; }
    }
}
