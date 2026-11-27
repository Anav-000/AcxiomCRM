using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AcxiomCRM.Models;

public class Opportunity
{
    [Key]
    public int OpportunityId { get; set; }

    [Required]
    [MaxLength(150)]
    public string OpportunityName { get; set; } = string.Empty;

    public int CustomerId { get; set; }
    public virtual Customer? Customer { get; set; }

    public int? LeadId { get; set; }
    public virtual Lead? Lead { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    [Required]
    [MaxLength(50)]
    public string Stage { get; set; } = "Qualification";

    public int Probability { get; set; }

    public DateTime ExpectedCloseDate { get; set; }

    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = "Open";

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    [MaxLength(100)]
    public string? AssignedTo { get; set; }

    public bool IsDeleted { get; set; } = false;

    [NotMapped]
    public decimal WeightedValue => (Amount * Probability) / 100m;
}
