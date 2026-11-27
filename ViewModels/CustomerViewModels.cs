using System.ComponentModel.DataAnnotations;
using AcxiomCRM.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace AcxiomCRM.ViewModels;

public class CustomerListViewModel
{
    public List<Customer> Customers { get; set; } = new();
    public string? SearchTerm { get; set; }
    public string? StatusFilter { get; set; }
}

public class CustomerCreateEditViewModel
{
    public int CustomerId { get; set; }

    [Display(Name = "Customer Code")]
    public string? CustomerCode { get; set; }

    [Required(ErrorMessage = "Customer Name is required.")]
    [StringLength(100, ErrorMessage = "Customer Name cannot exceed 100 characters.")]
    [Display(Name = "Customer Name")]
    public string CustomerName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email address is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(150, ErrorMessage = "Email cannot exceed 150 characters.")]
    [Display(Name = "Email Address")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Phone number is required.")]
    [RegularExpression(@"^\d{10}$", ErrorMessage = "Enter a valid 10-digit phone number.")]
    [StringLength(20, ErrorMessage = "Phone number cannot exceed 20 characters.")]
    [Display(Name = "Phone Number (10 digits)")]
    public string Phone { get; set; } = string.Empty;

    [StringLength(100, ErrorMessage = "Company Name cannot exceed 100 characters.")]
    [Display(Name = "Company Name")]
    public string? CompanyName { get; set; }

    [StringLength(250, ErrorMessage = "Address cannot exceed 250 characters.")]
    public string? Address { get; set; }

    [StringLength(100, ErrorMessage = "City cannot exceed 100 characters.")]
    public string? City { get; set; }

    [StringLength(100, ErrorMessage = "State cannot exceed 100 characters.")]
    public string? State { get; set; }

    [Required(ErrorMessage = "Status is required.")]
    [Display(Name = "Status")]
    public string Status { get; set; } = "Active";

    [Display(Name = "Assigned To (Owner)")]
    public string? AssignedTo { get; set; }

    public bool CanReassign { get; set; }
    public List<SelectListItem> AssignableUsers { get; set; } = new();
}

public class CustomerDetailsViewModel
{
    public Customer Customer { get; set; } = null!;
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }
}
