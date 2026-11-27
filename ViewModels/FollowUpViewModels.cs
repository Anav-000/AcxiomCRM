using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using AcxiomCRM.Models;

namespace AcxiomCRM.ViewModels;

public class FollowUpListViewModel
{
    public List<FollowUpListItemViewModel> FollowUps { get; set; } = new();

    public string? StatusFilter { get; set; }
    public string? FollowUpTypeFilter { get; set; }
    public DateTime? DateFilter { get; set; }
    public string? OwnerFilter { get; set; }
    public int? CustomerIdFilter { get; set; }
    public string? LeadSearch { get; set; }
    public bool OverdueOnly { get; set; }

    public int TotalPlannedCount { get; set; }
    public int TotalOverdueCount { get; set; }
    public int TotalCompletedCount { get; set; }

    public SelectList? StatusList { get; set; }
    public SelectList? TypeList { get; set; }
    public SelectList? UserList { get; set; }
    public SelectList? CustomerList { get; set; }
}

public class FollowUpListItemViewModel
{
    public int FollowUpId { get; set; }
    public int? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public int? LeadId { get; set; }
    public string? LeadName { get; set; }
    public int? OpportunityId { get; set; }
    public string? OpportunityName { get; set; }
    public DateTime FollowUpDate { get; set; }
    public string FollowUpType { get; set; } = "Call";
    public string? Remarks { get; set; }
    public string Status { get; set; } = "Planned";
    public string? AssignedTo { get; set; }
    public string? AssignedToName { get; set; }
    public bool IsOverdue => Status == "Planned" && FollowUpDate.Date < DateTime.Today;
}

public class FollowUpCreateEditViewModel : IValidatableObject
{
    public int FollowUpId { get; set; }

    public int? CustomerId { get; set; }
    public int? LeadId { get; set; }
    public int? OpportunityId { get; set; }

    [Required]
    [DataType(DataType.Date)]
    [Display(Name = "Follow-Up Date")]
    public DateTime FollowUpDate { get; set; } = DateTime.Today.AddDays(1);

    [Required]
    [MaxLength(50)]
    [Display(Name = "Follow-Up Type")]
    public string FollowUpType { get; set; } = "Call";

    [MaxLength(500)]
    public string? Remarks { get; set; }

    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = "Planned";

    [Display(Name = "Assigned To")]
    public string? AssignedTo { get; set; }

    public SelectList? CustomerList { get; set; }
    public SelectList? LeadList { get; set; }
    public SelectList? OpportunityList { get; set; }
    public SelectList? UserList { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Reject past dates for any new follow-up regardless of status
        if (FollowUpId == 0 && FollowUpDate.Date < DateTime.Today)
        {
            yield return new ValidationResult("Follow-up date cannot be in the past for new follow-ups.", new[] { nameof(FollowUpDate) });
        }
        else if (Status == "Planned" && FollowUpDate.Date < DateTime.Today)
        {
            yield return new ValidationResult("Follow-up date cannot be in the past for planned follow-ups.", new[] { nameof(FollowUpDate) });
        }
    }
}

public class FollowUpDetailsViewModel
{
    public FollowUp FollowUp { get; set; } = null!;
    public string? CustomerName { get; set; }
    public string? LeadName { get; set; }
    public string? OpportunityName { get; set; }
    public string? AssignedToName { get; set; }
    public bool IsOverdue => FollowUp.Status == "Planned" && FollowUp.FollowUpDate.Date < DateTime.Today;
}
