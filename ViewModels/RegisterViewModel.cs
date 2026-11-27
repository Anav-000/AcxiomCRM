using System.ComponentModel.DataAnnotations;

namespace AcxiomCRM.ViewModels;

public class RegisterViewModel
{
    [Required(ErrorMessage = "Full Name is required.")]
    [StringLength(100, ErrorMessage = "Name cannot exceed 100 characters.")]
    [Display(Name = "Full Name")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
    [Display(Name = "Email Address")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "The password must be at least 8 characters long.")]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Display(Name = "Confirm Password")]
    [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
