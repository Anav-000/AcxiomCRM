using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using AcxiomCRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Controllers;

[Authorize]
public class DashboardController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IScopedAccessService _scopedAccess;

    public DashboardController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IScopedAccessService scopedAccess)
    {
        _context = context;
        _userManager = userManager;
        _scopedAccess = scopedAccess;
    }

    public async Task<IActionResult> Index(string? dateFilter = "All", DateTime? startDate = null, DateTime? endDate = null)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return Challenge();
        }

        var roles = await _userManager.GetRolesAsync(user);
        var primaryRole = roles.FirstOrDefault() ?? DbInitializer.RoleSalesExecutive;
        var isAdmin = primaryRole == DbInitializer.RoleAdmin;
        var isManager = primaryRole == DbInitializer.RoleManager;

        // Resolve date boundaries if filter is applied
        DateTime? filterStart = null;
        DateTime? filterEnd = null;

        var now = DateTime.UtcNow;
        var today = DateTime.Today;

        switch (dateFilter?.ToLowerInvariant())
        {
            case "today":
                filterStart = today;
                filterEnd = today.AddDays(1).AddTicks(-1);
                break;
            case "thisweek":
                int diff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
                filterStart = today.AddDays(-1 * diff);
                filterEnd = filterStart.Value.AddDays(7).AddTicks(-1);
                break;
            case "thismonth":
                filterStart = new DateTime(today.Year, today.Month, 1);
                filterEnd = filterStart.Value.AddMonths(1).AddTicks(-1);
                break;
            case "custom":
                if (startDate.HasValue) filterStart = startDate.Value.Date;
                if (endDate.HasValue) filterEnd = endDate.Value.Date.AddDays(1).AddTicks(-1);
                break;
            default:
                dateFilter = "All";
                break;
        }

        // Team members for manager scope
        var teamUserIds = new List<string> { user.Id };
        var teamEmails = new List<string> { user.Email ?? string.Empty };
        if (isManager)
        {
            var reports = await _context.Users
                .Where(u => u.ManagerId == user.Id)
                .ToListAsync();
            teamUserIds.AddRange(reports.Select(r => r.Id));
            teamEmails.AddRange(reports.Where(r => !string.IsNullOrEmpty(r.Email)).Select(r => r.Email!));
        }

        // 1. Scoped base queries
        var customerQuery = _context.Customers.Where(c => !c.IsDeleted).AsQueryable();
        var leadQuery = _context.Leads.Where(l => !l.IsDeleted).AsQueryable();
        var oppQuery = _context.Opportunities.Where(o => !o.IsDeleted).AsQueryable();
        var followUpQuery = _context.FollowUps.Where(f => !f.IsDeleted).AsQueryable();

        customerQuery = await _scopedAccess.ApplyScopeAsync(customerQuery, user, c => c.AssignedTo);
        leadQuery = await _scopedAccess.ApplyScopeAsync(leadQuery, user, l => l.AssignedTo);
        oppQuery = await _scopedAccess.ApplyScopeAsync(oppQuery, user, o => o.AssignedTo);

        if (!isAdmin)
        {
            if (isManager)
            {
                followUpQuery = followUpQuery.Where(f => f.AssignedTo != null && teamUserIds.Contains(f.AssignedTo));
            }
            else
            {
                followUpQuery = followUpQuery.Where(f => f.AssignedTo == user.Id);
            }
        }

        // Apply date filtering to KPI queries if specified
        var filteredCustQuery = customerQuery;
        var filteredLeadQuery = leadQuery;
        var filteredOppQuery = oppQuery;
        var filteredFollowUpQuery = followUpQuery;

        if (filterStart.HasValue)
        {
            filteredCustQuery = filteredCustQuery.Where(c => c.CreatedDate >= filterStart.Value);
            filteredLeadQuery = filteredLeadQuery.Where(l => l.CreatedDate >= filterStart.Value);
            filteredOppQuery = filteredOppQuery.Where(o => o.CreatedDate >= filterStart.Value);
            filteredFollowUpQuery = filteredFollowUpQuery.Where(f => f.FollowUpDate >= filterStart.Value);
        }
        if (filterEnd.HasValue)
        {
            filteredCustQuery = filteredCustQuery.Where(c => c.CreatedDate <= filterEnd.Value);
            filteredLeadQuery = filteredLeadQuery.Where(l => l.CreatedDate <= filterEnd.Value);
            filteredOppQuery = filteredOppQuery.Where(o => o.CreatedDate <= filterEnd.Value);
            filteredFollowUpQuery = filteredFollowUpQuery.Where(f => f.FollowUpDate <= filterEnd.Value);
        }

        // 2. Compute Mandatory KPIs (Section 17.11)
        var totalCustomers = await filteredCustQuery.CountAsync();
        var totalLeads = await filteredLeadQuery.CountAsync();
        var openLeads = await filteredLeadQuery.CountAsync(l => l.Status != "Lost" && l.Status != "Converted" && l.Status != "Closed");

        var totalOpportunities = await filteredOppQuery.CountAsync();
        var openOpportunities = await filteredOppQuery.CountAsync(o => o.Status == "Open" || (o.Stage != "Won" && o.Stage != "Lost"));
        var wonOpportunities = await filteredOppQuery.CountAsync(o => o.Stage == "Won" || o.Status == "Won");
        var lostOpportunities = await filteredOppQuery.CountAsync(o => o.Stage == "Lost" || o.Status == "Lost");

        var openAmounts = await filteredOppQuery
            .Where(o => o.Status == "Open" || (o.Stage != "Won" && o.Stage != "Lost"))
            .Select(o => o.Amount)
            .ToListAsync();
        var totalPipelineValue = openAmounts.Sum();

        var pendingFollowUps = await filteredFollowUpQuery
            .CountAsync(f => f.Status == "Planned");

        // 3. Chart 1: Lead Status Breakdown
        var leadStatuses = new[] { "New", "Contacted", "Qualified", "Lost", "Converted" };
        var leadStatusGroups = await filteredLeadQuery
            .GroupBy(l => l.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();
        var leadStatusCounts = leadStatuses
            .Select(s => leadStatusGroups.FirstOrDefault(g => string.Equals(g.Status, s, StringComparison.OrdinalIgnoreCase))?.Count ?? 0)
            .ToList();

        // 4. Chart 2: Opportunity Pipeline by Stage (evaluated in-memory for SQLite decimal support)
        var oppList = await filteredOppQuery
            .Select(o => new { o.Stage, o.Amount, o.ExpectedCloseDate, o.Status })
            .ToListAsync();

        var pipelineStages = new[] { "Qualification", "Proposal", "Negotiation", "Won", "Lost" };
        var oppStageGroups = oppList
            .GroupBy(o => o.Stage)
            .Select(g => new { Stage = g.Key, Count = g.Count(), Amount = g.Sum(o => o.Amount) })
            .ToList();
        var pipelineStageCounts = pipelineStages
            .Select(s => oppStageGroups.FirstOrDefault(g => string.Equals(g.Stage, s, StringComparison.OrdinalIgnoreCase))?.Count ?? 0)
            .ToList();
        var pipelineStageAmounts = pipelineStages
            .Select(s => oppStageGroups.FirstOrDefault(g => string.Equals(g.Stage, s, StringComparison.OrdinalIgnoreCase))?.Amount ?? 0m)
            .ToList();

        // 5. Chart 3: Monthly Sales / Won Revenue (Last 6 Months, in-memory)
        var monthlyLabels = new List<string>();
        var monthlyAmounts = new List<decimal>();

        var allWonOpps = await oppQuery
            .Where(o => o.Stage == "Won" || o.Status == "Won")
            .Select(o => new { o.ExpectedCloseDate, o.Amount })
            .ToListAsync();

        for (int i = 5; i >= 0; i--)
        {
            var monthDate = today.AddMonths(-i);
            var mStart = new DateTime(monthDate.Year, monthDate.Month, 1);
            var mEnd = mStart.AddMonths(1).AddTicks(-1);

            monthlyLabels.Add(mStart.ToString("MMM yyyy"));

            var monthWonTotal = allWonOpps
                .Where(o => o.ExpectedCloseDate >= mStart && o.ExpectedCloseDate <= mEnd)
                .Sum(o => o.Amount);

            monthlyAmounts.Add(monthWonTotal);
        }

        // Recent Audit logs
        List<AuditLog> recentAuditLogs = new();
        if (isAdmin)
        {
            recentAuditLogs = await _context.AuditLogs
                .OrderByDescending(a => a.CreatedDate)
                .Take(10)
                .ToListAsync();
        }

        var model = new DashboardViewModel
        {
            UserFullName = string.IsNullOrWhiteSpace(user.Name) ? (user.Email ?? "User") : user.Name,
            Email = user.Email ?? string.Empty,
            RoleName = primaryRole,
            DateFilter = dateFilter ?? "All",
            StartDate = startDate,
            EndDate = endDate,
            TotalCustomers = totalCustomers,
            TotalLeads = totalLeads,
            OpenLeads = openLeads,
            TotalOpportunities = totalOpportunities,
            OpenOpportunities = openOpportunities,
            WonOpportunities = wonOpportunities,
            LostOpportunities = lostOpportunities,
            TotalPipelineValue = totalPipelineValue,
            PendingFollowUps = pendingFollowUps,
            LeadStatusLabels = leadStatuses.ToList(),
            LeadStatusCounts = leadStatusCounts,
            PipelineStageLabels = pipelineStages.ToList(),
            PipelineStageCounts = pipelineStageCounts,
            PipelineStageAmounts = pipelineStageAmounts,
            MonthlySalesLabels = monthlyLabels,
            MonthlySalesAmounts = monthlyAmounts,
            RecentAuditLogs = recentAuditLogs
        };

        return View(model);
    }
}
