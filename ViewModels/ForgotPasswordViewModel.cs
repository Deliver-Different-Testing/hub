using System.ComponentModel.DataAnnotations;

namespace UrgentHub.ViewModels
{
    public class ForgotViewModel
    {
        [Required]
        [Display(Name = "Email")]
        public string Email { get; set; }
    }
}
