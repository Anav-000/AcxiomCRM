using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using AcxiomCRM.ViewModels;

namespace AcxiomCRM.Controllers;

[Authorize]
public class ReportsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IScopedAccessService _scopedAccess;

    public ReportsController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IScopedAccessService scopedAccess)
    {
        _context = context;
        _userManager = userManager;
        _scopedAccess = scopedAccess;
    }

    private async Task<(string currentUserId, string role, bool isAdmin, bool isManager, List<string> teamUserIds, List<string> teamEmails)> GetUserScopeAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return (string.Empty, "SalesExecutive", false, false, new List<string>(), new List<string>());

        var roles = await _userManager.GetRolesAsync(user);
        var primaryRole = roles.FirstOrDefault() ?? "SalesExecutive";
        var isAdmin = primaryRole == "Admin";
        var isManager = primaryRole == "Manager";

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

        return (user.Id, primaryRole, isAdmin, isManager, teamUserIds, teamEmails);
    }

    // GET: /Reports
    public async Task<IActionResult> Index()
    {
        var scope = await GetUserScopeAsync();
        var vm = new ReportsIndexViewModel
        {
            UserRole = scope.role
        };
        return View(vm);
    }

    // 1. Customer Report: /Reports/Customers
    public async Task<IActionResult> Customers(string? status, string? owner, DateTime? fromDate, DateTime? toDate)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var query = _context.Customers.Where(c => !c.IsDeleted).AsQueryable();
        query = await _scopedAccess.ApplyScopeAsync(query, user, c => c.AssignedTo);

        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(c => c.Status == status);
        if (!string.IsNullOrWhiteSpace(owner)) query = query.Where(c => c.AssignedTo == owner);
        if (fromDate.HasValue) query = query.Where(c => c.CreatedDate >= fromDate.Value.Date);
        if (toDate.HasValue) query = query.Where(c => c.CreatedDate <= toDate.Value.Date.AddDays(1).AddTicks(-1));

        var list = await query.OrderByDescending(c => c.CreatedDate).ToListAsync();

        var vm = new CustomerReportViewModel
        {
            Customers = list,
            StatusFilter = status,
            OwnerFilter = owner,
            FromDate = fromDate,
            ToDate = toDate
        };

        return View(vm);
    }

    // 2. Lead Report: /Reports/Leads
    public async Task<IActionResult> Leads(string? status, string? source, string? owner)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var query = _context.Leads.Where(l => !l.IsDeleted).AsQueryable();
        query = await _scopedAccess.ApplyScopeAsync(query, user, l => l.AssignedTo);

        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(l => l.Status == status);
        if (!string.IsNullOrWhiteSpace(source)) query = query.Where(l => l.Source == source);
        if (!string.IsNullOrWhiteSpace(owner)) query = query.Where(l => l.AssignedTo == owner);

        var list = await query.OrderByDescending(l => l.CreatedDate).ToListAsync();

        var vm = new LeadReportViewModel
        {
            Leads = list,
            StatusFilter = status,
            SourceFilter = source,
            OwnerFilter = owner,
            TotalLeads = list.Count,
            ConvertedCount = list.Count(l => l.Status == "Converted"),
            TotalExpectedValue = list.Sum(l => l.ExpectedValue)
        };

        return View(vm);
    }

    // 3. Follow-Up Report: /Reports/FollowUps
    public async Task<IActionResult> FollowUps(string? status, string? owner)
    {
        var scope = await GetUserScopeAsync();
        var query = _context.FollowUps.Where(f => !f.IsDeleted).AsQueryable();

        if (!scope.isAdmin)
        {
            if (scope.isManager)
            {
                query = query.Where(f => f.AssignedTo != null && scope.teamUserIds.Contains(f.AssignedTo));
            }
            else
            {
                query = query.Where(f => f.AssignedTo == scope.currentUserId);
            }
        }

        var allItems = await query
            .Include(f => f.Customer)
            .Include(f => f.Lead)
            .Include(f => f.Opportunity)
            .OrderBy(f => f.FollowUpDate)
            .ToListAsync();

        var totalPlanned = allItems.Count(f => f.Status == "Planned");
        var totalCompleted = allItems.Count(f => f.Status == "Completed");
        var totalOverdue = allItems.Count(f => f.Status == "Planned" && f.FollowUpDate.Date < DateTime.Today);
        var totalCancelled = allItems.Count(f => f.Status == "Cancelled");

        var filtered = allItems;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (status == "Overdue") filtered = filtered.Where(f => f.Status == "Planned" && f.FollowUpDate.Date < DateTime.Today).ToList();
            else filtered = filtered.Where(f => f.Status == status).ToList();
        }
        if (!string.IsNullOrWhiteSpace(owner))
        {
            filtered = filtered.Where(f => f.AssignedTo == owner).ToList();
        }

        var vm = new FollowUpReportViewModel
        {
            FollowUps = filtered,
            TotalPlanned = totalPlanned,
            TotalCompleted = totalCompleted,
            TotalOverdue = totalOverdue,
            TotalCancelled = totalCancelled,
            StatusFilter = status,
            OwnerFilter = owner
        };

        return View(vm);
    }

    // 4. Opportunity Report: /Reports/Opportunities
    public async Task<IActionResult> Opportunities(string? stage, string? status, string? owner)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var query = _context.Opportunities
            .Include(o => o.Customer)
            .Where(o => !o.IsDeleted)
            .AsQueryable();

        query = await _scopedAccess.ApplyScopeAsync(query, user, o => o.AssignedTo);

        if (!string.IsNullOrWhiteSpace(stage)) query = query.Where(o => o.Stage == stage);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(o => o.Status == status);
        if (!string.IsNullOrWhiteSpace(owner)) query = query.Where(o => o.AssignedTo == owner);

        var list = await query.OrderByDescending(o => o.CreatedDate).ToListAsync();

        var vm = new OpportunityReportViewModel
        {
            Opportunities = list,
            StageFilter = stage,
            StatusFilter = status,
            OwnerFilter = owner,
            TotalAmount = list.Sum(o => o.Amount),
            TotalWeightedValue = list.Sum(o => o.WeightedValue)
        };

        return View(vm);
    }

    // 5. Pipeline Report: /Reports/Pipeline
    public async Task<IActionResult> Pipeline()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var query = _context.Opportunities
            .Include(o => o.Customer)
            .Where(o => !o.IsDeleted)
            .AsQueryable();

        query = await _scopedAccess.ApplyScopeAsync(query, user, o => o.AssignedTo);

        var opps = await query.ToListAsync();

        var openOpps = opps.Where(o => o.Status == "Open").ToList();
        var wonOpps = opps.Where(o => o.Status == "Won").ToList();

        var stageAmounts = opps.GroupBy(o => o.Stage).ToDictionary(g => g.Key, g => g.Sum(o => o.Amount));
        var stageCounts = opps.GroupBy(o => o.Stage).ToDictionary(g => g.Key, g => g.Count());
        var ownerAmounts = openOpps.GroupBy(o => o.AssignedTo ?? "Unassigned").ToDictionary(g => g.Key, g => g.Sum(o => o.Amount));

        var vm = new PipelineReportViewModel
        {
            TotalOpenAmount = openOpps.Sum(o => o.Amount),
            TotalWeightedAmount = openOpps.Sum(o => o.WeightedValue),
            TotalWonAmount = wonOpps.Sum(o => o.Amount),
            StageAmounts = stageAmounts,
            StageCounts = stageCounts,
            OwnerAmounts = ownerAmounts,
            OpenOpportunities = openOpps
        };

        return View(vm);
    }

    // 6. Sales / Conversion Report: /Reports/SalesConversion
    public async Task<IActionResult> SalesConversion()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var leadQuery = _context.Leads.Where(l => !l.IsDeleted).AsQueryable();
        leadQuery = await _scopedAccess.ApplyScopeAsync(leadQuery, user, l => l.AssignedTo);

        var oppQuery = _context.Opportunities.Where(o => !o.IsDeleted).AsQueryable();
        oppQuery = await _scopedAccess.ApplyScopeAsync(oppQuery, user, o => o.AssignedTo);

        var leads = await leadQuery.ToListAsync();
        var opps = await oppQuery.ToListAsync();

        var wonOpps = opps.Where(o => o.Stage == "Won" || o.Status == "Won").ToList();
        var lostOpps = opps.Where(o => o.Stage == "Lost" || o.Status == "Lost").ToList();

        var vm = new SalesConversionReportViewModel
        {
            TotalLeads = leads.Count,
            ConvertedLeads = leads.Count(l => l.Status == "Converted"),
            TotalOpportunities = opps.Count,
            WonOpportunities = wonOpps.Count,
            LostOpportunities = lostOpps.Count,
            WonAmount = wonOpps.Sum(o => o.Amount),
            LostAmount = lostOpps.Sum(o => o.Amount)
        };

        return View(vm);
    }

    // 7. User Activity Report: /Reports/UserActivity
    public async Task<IActionResult> UserActivity()
    {
        var scope = await GetUserScopeAsync();

        var usersQuery = _context.Users.AsQueryable();
        if (!scope.isAdmin)
        {
            if (scope.isManager)
            {
                usersQuery = usersQuery.Where(u => scope.teamUserIds.Contains(u.Id));
            }
            else
            {
                usersQuery = usersQuery.Where(u => u.Id == scope.currentUserId);
            }
        }

        var users = await usersQuery.ToListAsync();
        var userActivities = new List<UserActivityItem>();

        foreach (var u in users)
        {
            var uRoles = await _userManager.GetRolesAsync(u);
            var role = uRoles.FirstOrDefault() ?? "SalesExecutive";

            var calls = await _context.Activities.CountAsync(a => !a.IsDeleted && a.AssignedTo == u.Id && a.ActivityType == "Call");
            var meetings = await _context.Activities.CountAsync(a => !a.IsDeleted && a.AssignedTo == u.Id && a.ActivityType == "Meeting");
            var emails = await _context.Activities.CountAsync(a => !a.IsDeleted && a.AssignedTo == u.Id && a.ActivityType == "Email");
            var tasks = await _context.Activities.CountAsync(a => !a.IsDeleted && a.AssignedTo == u.Id && a.ActivityType == "Task");
            var audits = await _context.AuditLogs.CountAsync(a => a.UserId == u.Id);

            userActivities.Add(new UserActivityItem
            {
                UserId = u.Id,
                UserName = u.Name,
                Email = u.Email ?? string.Empty,
                Role = role,
                CallCount = calls,
                MeetingCount = meetings,
                EmailCount = emails,
                TaskCount = tasks,
                AuditLogCount = audits
            });
        }

        var vm = new UserActivityReportViewModel
        {
            UserActivities = userActivities
        };

        return View(vm);
    }

    // 8. Audit Report: /Reports/Audit
    public async Task<IActionResult> Audit(string? actionFilter, string? moduleFilter, DateTime? fromDate, DateTime? toDate)
    {
        var scope = await GetUserScopeAsync();
        var query = _context.AuditLogs.AsQueryable();

        if (!scope.isAdmin && !scope.isManager)
        {
            return Forbid();
        }

        if (scope.isManager)
        {
            query = query.Where(a => a.UserId != null && scope.teamUserIds.Contains(a.UserId));
        }

        if (!string.IsNullOrWhiteSpace(actionFilter)) query = query.Where(a => a.Action == actionFilter);
        if (!string.IsNullOrWhiteSpace(moduleFilter)) query = query.Where(a => a.EntityName == moduleFilter);
        if (fromDate.HasValue) query = query.Where(a => a.CreatedDate >= fromDate.Value.Date);
        if (toDate.HasValue) query = query.Where(a => a.CreatedDate <= toDate.Value.Date.AddDays(1).AddTicks(-1));

        var list = await query.OrderByDescending(a => a.CreatedDate).Take(200).ToListAsync();

        var vm = new AuditReportViewModel
        {
            Logs = list,
            ActionFilter = actionFilter,
            ModuleFilter = moduleFilter,
            FromDate = fromDate,
            ToDate = toDate
        };

        return View(vm);
    }
}
