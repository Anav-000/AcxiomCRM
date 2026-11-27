using System.ComponentModel.DataAnnotations;
using AcxiomCRM.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace AcxiomCRM.ViewModels;

public class LeadListViewModel
{
    public List<Lead> Leads { get; set; } = new();
    public string? NameFilter { get; set; }
    public string? CompanyFilter { get; set; }
    public string? StatusFilter { get; set; }
    public string? AssignedUserFilter { get; set; }

    public List<SelectListItem> AvailableStatuses { get; set; } = new();
    public List<SelectListItem> AvailableUsers { get; set; } = new();
}

public class LeadCreateEditViewModel
{
    public int LeadId { get; set; }

    [Display(Name = "Lead Code")]
    public string? LeadCode { get; set; }

    [Required(ErrorMessage = "Lead Name is required.")]
    [StringLength(100, ErrorMessage = "Lead Name cannot exceed 100 characters.")]
    [Display(Name = "Lead Name")]
    public string LeadName { get; set; } = string.Empty;

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

    [StringLength(50, ErrorMessage = "Source cannot exceed 50 characters.")]
    [Display(Name = "Lead Source")]
    public string? Source { get; set; } = "Website";

    [Required(ErrorMessage = "Status is required.")]
    [Display(Name = "Status")]
    public string Status { get; set; } = "New";

    [Required(ErrorMessage = "Expected Value is required.")]
    [Range(0.01, 100000000.00, ErrorMessage = "Expected Value must be greater than 0.")]
    [Display(Name = "Expected Value ($)")]
    public decimal ExpectedValue { get; set; }

    [Display(Name = "Assigned To")]
    public string? AssignedTo { get; set; }

    public bool CanReassign { get; set; }
    public List<SelectListItem> AssignableUsers { get; set; } = new();
    public List<SelectListItem> AvailableStatuses { get; set; } = new();
}

public class LeadDetailsViewModel
{
    public Lead Lead { get; set; } = null!;
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }
    public bool CanConvert { get; set; }
}

public class LeadConvertViewModel
{
    public int LeadId { get; set; }

    [Display(Name = "Lead Code")]
    public string LeadCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Customer Name is required.")]
    [StringLength(100, ErrorMessage = "Customer Name cannot exceed 100 characters.")]
    [Display(Name = "Customer Name")]
    public string CustomerName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(150, ErrorMessage = "Email cannot exceed 150 characters.")]
    [Display(Name = "Email Address")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Phone is required.")]
    [RegularExpression(@"^\d{10}$", ErrorMessage = "Enter a valid 10-digit phone number.")]
    [StringLength(20, ErrorMessage = "Phone cannot exceed 20 characters.")]
    [Display(Name = "Phone Number (10 digits)")]
    public string Phone { get; set; } = string.Empty;

    [StringLength(100, ErrorMessage = "Company cannot exceed 100 characters.")]
    [Display(Name = "Company Name")]
    public string? CompanyName { get; set; }

    [StringLength(250, ErrorMessage = "Address cannot exceed 250 characters.")]
    [Display(Name = "Address")]
    public string? Address { get; set; }

    [StringLength(100, ErrorMessage = "City cannot exceed 100 characters.")]
    [Display(Name = "City")]
    public string? City { get; set; }

    [StringLength(100, ErrorMessage = "State cannot exceed 100 characters.")]
    [Display(Name = "State")]
    public string? State { get; set; }

    [Display(Name = "Owner (Assigned To)")]
    public string? AssignedTo { get; set; }

    // Optional Opportunity fields
    [Display(Name = "Create an Opportunity for this customer now")]
    public bool CreateOpportunity { get; set; } = true;

    [StringLength(150, ErrorMessage = "Opportunity Name cannot exceed 150 characters.")]
    [Display(Name = "Opportunity Name")]
    public string? OpportunityName { get; set; }

    [Range(0.01, 100000000.00, ErrorMessage = "Opportunity Amount must be greater than 0.")]
    [Display(Name = "Amount ($)")]
    public decimal? OpportunityAmount { get; set; }

    [Range(0, 100, ErrorMessage = "Probability must be between 0 and 100.")]
    [Display(Name = "Probability (%)")]
    public int? OpportunityProbability { get; set; } = 50;

    [DataType(DataType.Date)]
    [Display(Name = "Expected Close Date")]
    public DateTime? ExpectedCloseDate { get; set; } = DateTime.Today.AddDays(30);
}
