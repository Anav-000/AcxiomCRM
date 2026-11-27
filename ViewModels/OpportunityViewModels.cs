using System.ComponentModel.DataAnnotations;
using AcxiomCRM.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace AcxiomCRM.ViewModels;

public class OpportunityListViewModel
{
    public List<Opportunity> Opportunities { get; set; } = new();
    public string? SearchTerm { get; set; }
    public string? StageFilter { get; set; }
    public string? StatusFilter { get; set; }

    public decimal TotalOpenAmount { get; set; }
    public decimal TotalWeightedAmount { get; set; }
    public decimal TotalWonAmount { get; set; }

    public List<SelectListItem> AvailableStages { get; set; } = new();
    public List<SelectListItem> AvailableStatuses { get; set; } = new();
}

public class OpportunityCreateEditViewModel : IValidatableObject
{
    public int OpportunityId { get; set; }

    [Required(ErrorMessage = "Opportunity Name is required.")]
    [StringLength(150, ErrorMessage = "Opportunity Name cannot exceed 150 characters.")]
    [Display(Name = "Opportunity Name")]
    public string OpportunityName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please select a Customer.")]
    [Display(Name = "Customer")]
    public int CustomerId { get; set; }

    [Display(Name = "Originating Lead (Optional)")]
    public int? LeadId { get; set; }

    [Required(ErrorMessage = "Opportunity Amount is required.")]
    [Range(0.01, 100000000.00, ErrorMessage = "Opportunity Amount must be greater than 0.")]
    [Display(Name = "Amount ($)")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "Stage is required.")]
    [Display(Name = "Pipeline Stage")]
    public string Stage { get; set; } = "Qualification";

    [Required(ErrorMessage = "Probability is required.")]
    [Range(0, 100, ErrorMessage = "Probability must be between 0 and 100.")]
    [Display(Name = "Win Probability (%)")]
    public int Probability { get; set; } = 50;

    [Required(ErrorMessage = "Expected Close Date is required.")]
    [DataType(DataType.Date)]
    [Display(Name = "Expected Close Date")]
    public DateTime ExpectedCloseDate { get; set; } = DateTime.Today.AddDays(30);

    [Display(Name = "Assigned Owner")]
    public string? AssignedTo { get; set; }

    public bool CanReassign { get; set; }
    public List<SelectListItem> AvailableCustomers { get; set; } = new();
    public List<SelectListItem> AvailableLeads { get; set; } = new();
    public List<SelectListItem> AssignableUsers { get; set; } = new();
    public List<SelectListItem> AvailableStages { get; set; } = new();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Amount <= 0)
        {
            yield return new ValidationResult("Opportunity Amount must be greater than 0.", new[] { nameof(Amount) });
        }
        if (Probability < 0 || Probability > 100)
        {
            yield return new ValidationResult("Probability must be between 0 and 100.", new[] { nameof(Probability) });
        }
        if (Stage != "Won" && Stage != "Lost" && ExpectedCloseDate.Date < DateTime.Today)
        {
            yield return new ValidationResult("Expected Close Date cannot be in the past for active opportunities.", new[] { nameof(ExpectedCloseDate) });
        }
    }
}

public class OpportunityDetailsViewModel
{
    public Opportunity Opportunity { get; set; } = null!;
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }
}
