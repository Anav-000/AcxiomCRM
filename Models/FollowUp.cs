using System.ComponentModel.DataAnnotations;

namespace AcxiomCRM.Models;

public class FollowUp
{
    [Key]
    public int FollowUpId { get; set; }

    public int? CustomerId { get; set; }
    public virtual Customer? Customer { get; set; }

    public int? LeadId { get; set; }
    public virtual Lead? Lead { get; set; }

    public int? OpportunityId { get; set; }
    public virtual Opportunity? Opportunity { get; set; }

    public DateTime FollowUpDate { get; set; }

    [Required]
    [MaxLength(50)]
    public string FollowUpType { get; set; } = "Call";

    [MaxLength(500)]
    public string? Remarks { get; set; }

    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = "Planned";

    [MaxLength(100)]
    public string? AssignedTo { get; set; }

    public bool IsDeleted { get; set; } = false;
}
