using System.ComponentModel.DataAnnotations;

namespace AcxiomCRM.DTOs;

// --- Auth DTOs ---
public class LoginRequestDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

public class LoginResponseDto
{
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Message { get; set; } = "Login successful";
}

// --- Customer DTOs ---
public class CustomerDto
{
    public int CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? CompanyName { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string Status { get; set; } = "Active";
    public string? AssignedTo { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedDate { get; set; }
}

public class CreateCustomerDto
{
    [Required(ErrorMessage = "Customer Name is required.")]
    [StringLength(100)]
    public string CustomerName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(150)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Phone number is required.")]
    [RegularExpression(@"^\d{10}$", ErrorMessage = "Enter a valid 10-digit phone number.")]
    public string Phone { get; set; } = string.Empty;

    [StringLength(100)]
    public string? CompanyName { get; set; }

    [StringLength(250)]
    public string? Address { get; set; }

    [StringLength(100)]
    public string? City { get; set; }

    [StringLength(100)]
    public string? State { get; set; }

    public string? AssignedTo { get; set; }
}

public class UpdateCustomerDto
{
    [Required(ErrorMessage = "Customer Name is required.")]
    [StringLength(100)]
    public string CustomerName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(150)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Phone number is required.")]
    [RegularExpression(@"^\d{10}$", ErrorMessage = "Enter a valid 10-digit phone number.")]
    public string Phone { get; set; } = string.Empty;

    [StringLength(100)]
    public string? CompanyName { get; set; }

    [StringLength(250)]
    public string? Address { get; set; }

    [StringLength(100)]
    public string? City { get; set; }

    [StringLength(100)]
    public string? State { get; set; }

    public string Status { get; set; } = "Active";

    public string? AssignedTo { get; set; }
}

// --- Lead DTOs ---
public class LeadDto
{
    public int LeadId { get; set; }
    public string LeadCode { get; set; } = string.Empty;
    public string LeadName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? CompanyName { get; set; }
    public string? Source { get; set; }
    public string Status { get; set; } = "New";
    public decimal ExpectedValue { get; set; }
    public string? AssignedTo { get; set; }
    public DateTime CreatedDate { get; set; }
    public int? ConvertedCustomerId { get; set; }
}

public class CreateLeadDto
{
    [Required(ErrorMessage = "Lead Name is required.")]
    [StringLength(100)]
    public string LeadName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress]
    [StringLength(150)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Phone number is required.")]
    public string Phone { get; set; } = string.Empty;

    [StringLength(100)]
    public string? CompanyName { get; set; }

    [StringLength(50)]
    public string? Source { get; set; }

    [Range(0.01, 100000000.00, ErrorMessage = "Expected Value must be greater than 0.")]
    public decimal ExpectedValue { get; set; } = 1000m;

    public string? AssignedTo { get; set; }
}

// --- Opportunity DTOs ---
public class OpportunityDto
{
    public int OpportunityId { get; set; }
    public string OpportunityName { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public int? LeadId { get; set; }
    public decimal Amount { get; set; }
    public string Stage { get; set; } = "Qualification";
    public int Probability { get; set; }
    public decimal WeightedValue { get; set; }
    public DateTime ExpectedCloseDate { get; set; }
    public string Status { get; set; } = "Open";
    public string? AssignedTo { get; set; }
    public DateTime CreatedDate { get; set; }
}

public class CreateOpportunityDto
{
    [Required(ErrorMessage = "Opportunity Name is required.")]
    [StringLength(150)]
    public string OpportunityName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Customer ID is required.")]
    public int CustomerId { get; set; }

    public int? LeadId { get; set; }

    [Range(0.01, 100000000.00, ErrorMessage = "Opportunity Amount must be greater than 0.")]
    public decimal Amount { get; set; }

    [Required]
    public string Stage { get; set; } = "Qualification";

    [Range(0, 100, ErrorMessage = "Probability must be between 0 and 100.")]
    public int Probability { get; set; } = 50;

    [Required]
    public DateTime ExpectedCloseDate { get; set; }

    public string? AssignedTo { get; set; }
}

// --- FollowUp DTOs ---
public class FollowUpDto
{
    public int FollowUpId { get; set; }
    public int? CustomerId { get; set; }
    public int? LeadId { get; set; }
    public int? OpportunityId { get; set; }
    public DateTime FollowUpDate { get; set; }
    public string FollowUpType { get; set; } = "Call";
    public string? Remarks { get; set; }
    public string Status { get; set; } = "Planned";
    public string? AssignedTo { get; set; }
}

public class CreateFollowUpDto
{
    public int? CustomerId { get; set; }
    public int? LeadId { get; set; }
    public int? OpportunityId { get; set; }

    [Required]
    public DateTime FollowUpDate { get; set; } = DateTime.Today.AddDays(1);

    [Required]
    [StringLength(50)]
    public string FollowUpType { get; set; } = "Call";

    [StringLength(500)]
    public string? Remarks { get; set; }

    [Required]
    [StringLength(50)]
    public string Status { get; set; } = "Planned";

    public string? AssignedTo { get; set; }
}

// --- Pipeline Report DTO ---
public class PipelineReportDto
{
    public decimal TotalOpenPipelineAmount { get; set; }
    public decimal TotalWeightedPipelineAmount { get; set; }
    public decimal TotalWonAmount { get; set; }
    public Dictionary<string, decimal> AmountByStage { get; set; } = new();
    public Dictionary<string, int> CountByStage { get; set; } = new();
    public Dictionary<string, decimal> AmountByOwner { get; set; } = new();
}
