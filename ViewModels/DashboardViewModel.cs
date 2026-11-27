using AcxiomCRM.Models;

namespace AcxiomCRM.ViewModels;

public class DashboardViewModel
{
    public string UserFullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;

    // Filter settings
    public string DateFilter { get; set; } = "All"; // All, Today, ThisWeek, ThisMonth, Custom
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    // Mandatory KPI Metrics (Section 17.11)
    public int TotalCustomers { get; set; }
    public int TotalLeads { get; set; }
    public int OpenLeads { get; set; }
    public int TotalOpportunities { get; set; }
    public int OpenOpportunities { get; set; }
    public int WonOpportunities { get; set; }
    public int LostOpportunities { get; set; }
    public decimal TotalPipelineValue { get; set; }
    public int PendingFollowUps { get; set; }

    // Chart.js Data (Section 17.12)
    // 1. Lead Status: New, Contacted, Qualified, Lost, Converted
    public List<string> LeadStatusLabels { get; set; } = new() { "New", "Contacted", "Qualified", "Lost", "Converted" };
    public List<int> LeadStatusCounts { get; set; } = new() { 0, 0, 0, 0, 0 };

    // 2. Opportunity Pipeline by Stage: Qualification, Proposal, Negotiation, Won, Lost
    public List<string> PipelineStageLabels { get; set; } = new() { "Qualification", "Proposal", "Negotiation", "Won", "Lost" };
    public List<int> PipelineStageCounts { get; set; } = new() { 0, 0, 0, 0, 0 };
    public List<decimal> PipelineStageAmounts { get; set; } = new() { 0m, 0m, 0m, 0m, 0m };

    // 3. Monthly Sales / Outcomes (Last 6 months)
    public List<string> MonthlySalesLabels { get; set; } = new();
    public List<decimal> MonthlySalesAmounts { get; set; } = new();

    // Recent audits
    public List<AuditLog> RecentAuditLogs { get; set; } = new();
}
