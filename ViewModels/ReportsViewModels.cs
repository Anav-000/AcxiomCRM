using AcxiomCRM.Models;

namespace AcxiomCRM.ViewModels;

public class ReportsIndexViewModel
{
    public string UserRole { get; set; } = "SalesExecutive";
}

public class CustomerReportViewModel
{
    public List<Customer> Customers { get; set; } = new();
    public string? StatusFilter { get; set; }
    public string? OwnerFilter { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}

public class LeadReportViewModel
{
    public List<Lead> Leads { get; set; } = new();
    public string? StatusFilter { get; set; }
    public string? SourceFilter { get; set; }
    public string? OwnerFilter { get; set; }
    public int TotalLeads { get; set; }
    public int ConvertedCount { get; set; }
    public decimal TotalExpectedValue { get; set; }
}

public class FollowUpReportViewModel
{
    public List<FollowUp> FollowUps { get; set; } = new();
    public int TotalPlanned { get; set; }
    public int TotalCompleted { get; set; }
    public int TotalOverdue { get; set; }
    public int TotalCancelled { get; set; }
    public string? StatusFilter { get; set; }
    public string? OwnerFilter { get; set; }
}

public class OpportunityReportViewModel
{
    public List<Opportunity> Opportunities { get; set; } = new();
    public string? StageFilter { get; set; }
    public string? StatusFilter { get; set; }
    public string? OwnerFilter { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal TotalWeightedValue { get; set; }
}

public class PipelineReportViewModel
{
    public decimal TotalOpenAmount { get; set; }
    public decimal TotalWeightedAmount { get; set; }
    public decimal TotalWonAmount { get; set; }
    public Dictionary<string, decimal> StageAmounts { get; set; } = new();
    public Dictionary<string, int> StageCounts { get; set; } = new();
    public Dictionary<string, decimal> OwnerAmounts { get; set; } = new();
    public List<Opportunity> OpenOpportunities { get; set; } = new();
}

public class SalesConversionReportViewModel
{
    public int TotalLeads { get; set; }
    public int ConvertedLeads { get; set; }
    public decimal LeadConversionRate => TotalLeads > 0 ? (decimal)ConvertedLeads * 100m / TotalLeads : 0m;

    public int TotalOpportunities { get; set; }
    public int WonOpportunities { get; set; }
    public int LostOpportunities { get; set; }
    public decimal WonAmount { get; set; }
    public decimal LostAmount { get; set; }
    public decimal OpportunityWinRate => (WonOpportunities + LostOpportunities) > 0
        ? (decimal)WonOpportunities * 100m / (WonOpportunities + LostOpportunities)
        : 0m;
}

public class UserActivityReportViewModel
{
    public List<UserActivityItem> UserActivities { get; set; } = new();
}

public class UserActivityItem
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public int CallCount { get; set; }
    public int MeetingCount { get; set; }
    public int EmailCount { get; set; }
    public int TaskCount { get; set; }
    public int TotalActivities => CallCount + MeetingCount + EmailCount + TaskCount;
    public int AuditLogCount { get; set; }
}

public class AuditReportViewModel
{
    public List<AuditLog> Logs { get; set; } = new();
    public string? ActionFilter { get; set; }
    public string? ModuleFilter { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}
