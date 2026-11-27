using System.ComponentModel.DataAnnotations;

namespace AcxiomCRM.Models;

public class Customer
{
    [Key]
    public int CustomerId { get; set; }

    [Required]
    [MaxLength(50)]
    public string CustomerCode { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string CustomerName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [MaxLength(150)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(150)]
    public string NormalizedEmail { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string Phone { get; set; } = string.Empty;

    [MaxLength(20)]
    public string NormalizedPhone { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? CompanyName { get; set; }

    [MaxLength(250)]
    public string? Address { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(100)]
    public string? State { get; set; }

    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = "Active";

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    [MaxLength(100)]
    public string? CreatedBy { get; set; }

    [MaxLength(100)]
    public string? AssignedTo { get; set; }

    public bool IsDeleted { get; set; } = false;

    public virtual ICollection<Opportunity> Opportunities { get; set; } = new List<Opportunity>();
    public virtual ICollection<FollowUp> FollowUps { get; set; } = new List<FollowUp>();
    public virtual ICollection<Activity> Activities { get; set; } = new List<Activity>();
}
