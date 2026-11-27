using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using AcxiomCRM.Models;

namespace AcxiomCRM.ViewModels;

public class ActivityListViewModel
{
    public List<ActivityListItemViewModel> Activities { get; set; } = new();

    public string? ActivityTypeFilter { get; set; }
    public string? StatusFilter { get; set; }
    public DateTime? DateFilter { get; set; }
    public string? AssignedToFilter { get; set; }
    public string? SearchTerm { get; set; }

    public SelectList? TypeList { get; set; }
    public SelectList? StatusList { get; set; }
    public SelectList? UserList { get; set; }
}

public class ActivityListItemViewModel
{
    public int ActivityId { get; set; }
    public string ActivityType { get; set; } = "Call";
    public string Subject { get; set; } = string.Empty;
    public DateTime ActivityDate { get; set; }
    public string Status { get; set; } = "Completed";
    public int? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public int? LeadId { get; set; }
    public string? LeadName { get; set; }
    public int? OpportunityId { get; set; }
    public string? OpportunityName { get; set; }
    public string? AssignedTo { get; set; }
    public string? AssignedToName { get; set; }
}

public class ActivityCreateEditViewModel
{
    public int ActivityId { get; set; }

    [Required]
    [MaxLength(50)]
    [Display(Name = "Activity Type")]
    public string ActivityType { get; set; } = "Call";

    [Required]
    [MaxLength(150)]
    public string Subject { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    [Required]
    [DataType(DataType.Date)]
    [Display(Name = "Activity Date")]
    public DateTime ActivityDate { get; set; } = DateTime.Today;

    [Display(Name = "Customer")]
    public int? CustomerId { get; set; }

    [Display(Name = "Lead")]
    public int? LeadId { get; set; }

    [Display(Name = "Opportunity")]
    public int? OpportunityId { get; set; }

    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = "Completed";

    [Display(Name = "Assigned To")]
    public string? AssignedTo { get; set; }

    public SelectList? CustomerList { get; set; }
    public SelectList? LeadList { get; set; }
    public SelectList? OpportunityList { get; set; }
    public SelectList? UserList { get; set; }
}

public class ActivityDetailsViewModel
{
    public Activity Activity { get; set; } = null!;
    public string? CustomerName { get; set; }
    public string? LeadName { get; set; }
    public string? OpportunityName { get; set; }
    public string? AssignedToName { get; set; }
}
