using System.ComponentModel.DataAnnotations;

namespace AcxiomCRM.Models;

public class Activity
{
    [Key]
    public int ActivityId { get; set; }

    [Required]
    [MaxLength(50)]
    public string ActivityType { get; set; } = "Call";

    [Required]
    [MaxLength(150)]
    public string Subject { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    public DateTime ActivityDate { get; set; } = DateTime.UtcNow;

    public int? CustomerId { get; set; }
    public virtual Customer? Customer { get; set; }

    public int? LeadId { get; set; }
    public virtual Lead? Lead { get; set; }

    public int? OpportunityId { get; set; }
    public virtual Opportunity? Opportunity { get; set; }

    [MaxLength(100)]
    public string? AssignedTo { get; set; }

    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = "Completed";

    public bool IsDeleted { get; set; } = false;
}
