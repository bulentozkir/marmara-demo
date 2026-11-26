using System.ComponentModel.DataAnnotations;

namespace DotNetCoreSqlDb.Models
{
    public class LoginViewModel
    {
        [Required, StringLength(64, MinimumLength = 3)]
        public string Username { get; set; } = "";

        [Required, StringLength(128), DataType(DataType.Password)]
        public string Password { get; set; } = "";

        public string? ReturnUrl { get; set; }
    }

    public class RegisterViewModel : LoginViewModel
    {
        [Required, DataType(DataType.Password), Compare(nameof(Password))]
        [Display(Name = "Confirm password")]
        public string ConfirmPassword { get; set; } = "";
    }

    public class UpdateAccountViewModel
    {
        [Required, StringLength(64, MinimumLength = 3)]
        public string Username { get; set; } = "";

        [Required, StringLength(128), DataType(DataType.Password)]
        [Display(Name = "Current password")]
        public string CurrentPassword { get; set; } = "";
    }

    public class ChangePasswordViewModel
    {
        [Required, StringLength(128), DataType(DataType.Password)]
        [Display(Name = "Current password")]
        public string CurrentPassword { get; set; } = "";

        [Required, StringLength(128, MinimumLength = 12), DataType(DataType.Password)]
        [Display(Name = "New password")]
        public string NewPassword { get; set; } = "";

        [Required, DataType(DataType.Password), Compare(nameof(NewPassword))]
        [Display(Name = "Confirm new password")]
        public string ConfirmPassword { get; set; } = "";
    }

    public class DeleteAccountViewModel
    {
        [Required, StringLength(128), DataType(DataType.Password)]
        [Display(Name = "Current password")]
        public string CurrentPassword { get; set; } = "";

        [Display(Name = "I understand this permanently deletes my account and all my Todos.")]
        public bool ConfirmDeletion { get; set; }
    }

    public class ProfileViewModel
    {
        public UpdateAccountViewModel Account { get; set; } = new();
        public ChangePasswordViewModel Password { get; set; } = new();
        public DeleteAccountViewModel Deletion { get; set; } = new();
    }
}
