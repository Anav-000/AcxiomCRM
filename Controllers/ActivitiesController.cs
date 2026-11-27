using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using AcxiomCRM.ViewModels;

namespace AcxiomCRM.Controllers;

[Authorize]
public class ActivitiesController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditService _auditService;

    public ActivitiesController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IAuditService auditService)
    {
        _context = context;
        _userManager = userManager;
        _auditService = auditService;
    }

    private async Task<(string currentUserId, bool isAdmin, bool isManager, List<string> teamUserIds)> GetUserScopeAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return (string.Empty, false, false, new List<string>());

        var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
        var isManager = await _userManager.IsInRoleAsync(user, "Manager");

        var teamUserIds = new List<string> { user.Id };
        if (isManager)
        {
            var reports = await _context.Users
                .Where(u => u.ManagerId == user.Id)
                .Select(u => u.Id)
                .ToListAsync();
            teamUserIds.AddRange(reports);
        }

        return (user.Id, isAdmin, isManager, teamUserIds);
    }

    private IQueryable<Activity> ApplyScope(IQueryable<Activity> query, string currentUserId, bool isAdmin, bool isManager, List<string> teamUserIds)
    {
        query = query.Where(a => !a.IsDeleted);

        if (isAdmin)
        {
            return query;
        }

        if (isManager)
        {
            return query.Where(a => a.AssignedTo != null && teamUserIds.Contains(a.AssignedTo));
        }

        return query.Where(a => a.AssignedTo == currentUserId);
    }

    // GET: Activities
    public async Task<IActionResult> Index(
        string? activityTypeFilter,
        string? statusFilter,
        DateTime? dateFilter,
        string? assignedToFilter,
        string? searchTerm)
    {
        var scope = await GetUserScopeAsync();
        var query = ApplyScope(_context.Activities.AsNoTracking(), scope.currentUserId, scope.isAdmin, scope.isManager, scope.teamUserIds);

        if (!string.IsNullOrWhiteSpace(activityTypeFilter))
        {
            query = query.Where(a => a.ActivityType == activityTypeFilter);
        }

        if (!string.IsNullOrWhiteSpace(statusFilter))
        {
            query = query.Where(a => a.Status == statusFilter);
        }

        if (dateFilter.HasValue)
        {
            query = query.Where(a => a.ActivityDate.Date == dateFilter.Value.Date);
        }

        if (!string.IsNullOrWhiteSpace(assignedToFilter))
        {
            query = query.Where(a => a.AssignedTo == assignedToFilter);
        }

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            query = query.Where(a => EF.Functions.Like(a.Subject, $"%{searchTerm}%") ||
                                     (a.Description != null && EF.Functions.Like(a.Description, $"%{searchTerm}%")));
        }

        var items = await query
            .Include(a => a.Customer)
            .Include(a => a.Lead)
            .Include(a => a.Opportunity)
            .OrderByDescending(a => a.ActivityDate)
            .ToListAsync();

        var userIds = items.Where(a => a.AssignedTo != null).Select(a => a.AssignedTo!).Distinct().ToList();
        var userDict = await _context.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.UserName ?? u.Email);

        var listItems = items.Select(a => new ActivityListItemViewModel
        {
            ActivityId = a.ActivityId,
            ActivityType = a.ActivityType,
            Subject = a.Subject,
            ActivityDate = a.ActivityDate,
            Status = a.Status,
            CustomerId = a.CustomerId,
            CustomerName = a.Customer?.CustomerName,
            LeadId = a.LeadId,
            LeadName = a.Lead?.LeadName,
            OpportunityId = a.OpportunityId,
            OpportunityName = a.Opportunity?.OpportunityName,
            AssignedTo = a.AssignedTo,
            AssignedToName = a.AssignedTo != null && userDict.ContainsKey(a.AssignedTo) ? userDict[a.AssignedTo] : "Unassigned"
        }).ToList();

        var scopedUsers = await GetAssignableUsersQuery(scope).ToListAsync();

        var vm = new ActivityListViewModel
        {
            Activities = listItems,
            ActivityTypeFilter = activityTypeFilter,
            StatusFilter = statusFilter,
            DateFilter = dateFilter,
            AssignedToFilter = assignedToFilter,
            SearchTerm = searchTerm,
            TypeList = new SelectList(new[] { "Call", "Meeting", "Email", "Task" }, activityTypeFilter),
            StatusList = new SelectList(new[] { "Completed", "Pending", "InProgress", "Cancelled" }, statusFilter),
            UserList = new SelectList(scopedUsers, "Id", "DisplayName", assignedToFilter)
        };

        return View(vm);
    }

    // GET: Activities/Details/5
    public async Task<IActionResult> Details(int? id)
    {
        if (id == null) return NotFound();

        var scope = await GetUserScopeAsync();
        var activity = await ApplyScope(_context.Activities, scope.currentUserId, scope.isAdmin, scope.isManager, scope.teamUserIds)
            .Include(a => a.Customer)
            .Include(a => a.Lead)
            .Include(a => a.Opportunity)
            .FirstOrDefaultAsync(a => a.ActivityId == id);

        if (activity == null) return NotFound();

        string? assignedName = null;
        if (!string.IsNullOrEmpty(activity.AssignedTo))
        {
            var u = await _context.Users.FindAsync(activity.AssignedTo);
            assignedName = u?.FullName ?? u?.UserName;
        }

        var vm = new ActivityDetailsViewModel
        {
            Activity = activity,
            CustomerName = activity.Customer?.CustomerName,
            LeadName = activity.Lead?.LeadName,
            OpportunityName = activity.Opportunity?.OpportunityName,
            AssignedToName = assignedName
        };

        return View(vm);
    }

    // GET: Activities/Create
    public async Task<IActionResult> Create(int? customerId, int? leadId, int? opportunityId)
    {
        var scope = await GetUserScopeAsync();
        var vm = new ActivityCreateEditViewModel
        {
            CustomerId = customerId,
            LeadId = leadId,
            OpportunityId = opportunityId,
            AssignedTo = scope.currentUserId,
            ActivityDate = DateTime.Today
        };

        await PopulateDropDownsAsync(vm, scope);
        return View(vm);
    }

    // POST: Activities/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ActivityCreateEditViewModel vm)
    {
        var scope = await GetUserScopeAsync();

        // Scoped link validation
        if (vm.CustomerId.HasValue)
        {
            var cOk = await GetScopedCustomersQuery(scope).AnyAsync(c => c.CustomerId == vm.CustomerId.Value);
            if (!cOk) ModelState.AddModelError(nameof(vm.CustomerId), "Selected customer is not accessible.");
        }
        if (vm.LeadId.HasValue)
        {
            var lOk = await GetScopedLeadsQuery(scope).AnyAsync(l => l.LeadId == vm.LeadId.Value);
            if (!lOk) ModelState.AddModelError(nameof(vm.LeadId), "Selected lead is not accessible.");
        }
        if (vm.OpportunityId.HasValue)
        {
            var oOk = await GetScopedOpportunitiesQuery(scope).AnyAsync(o => o.OpportunityId == vm.OpportunityId.Value);
            if (!oOk) ModelState.AddModelError(nameof(vm.OpportunityId), "Selected opportunity is not accessible.");
        }

        if (ModelState.IsValid)
        {
            var activity = new Activity
            {
                ActivityType = vm.ActivityType,
                Subject = vm.Subject,
                Description = vm.Description,
                ActivityDate = vm.ActivityDate,
                CustomerId = vm.CustomerId,
                LeadId = vm.LeadId,
                OpportunityId = vm.OpportunityId,
                Status = vm.Status,
                AssignedTo = string.IsNullOrWhiteSpace(vm.AssignedTo) ? scope.currentUserId : vm.AssignedTo,
                IsDeleted = false
            };

            _context.Activities.Add(activity);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                "ActivityCreated",
                "Activity",
                activity.ActivityId.ToString(),
                $"Activity {activity.ActivityId} ({activity.ActivityType}: {activity.Subject}) logged for {activity.ActivityDate:yyyy-MM-dd}");

            TempData["SuccessMessage"] = "Activity logged successfully.";
            return RedirectToAction(nameof(Index));
        }

        await PopulateDropDownsAsync(vm, scope);
        return View(vm);
    }

    // GET: Activities/Edit/5
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();

        var scope = await GetUserScopeAsync();
        var activity = await ApplyScope(_context.Activities, scope.currentUserId, scope.isAdmin, scope.isManager, scope.teamUserIds)
            .FirstOrDefaultAsync(a => a.ActivityId == id);

        if (activity == null) return NotFound();

        var vm = new ActivityCreateEditViewModel
        {
            ActivityId = activity.ActivityId,
            ActivityType = activity.ActivityType,
            Subject = activity.Subject,
            Description = activity.Description,
            ActivityDate = activity.ActivityDate,
            CustomerId = activity.CustomerId,
            LeadId = activity.LeadId,
            OpportunityId = activity.OpportunityId,
            Status = activity.Status,
            AssignedTo = activity.AssignedTo
        };

        await PopulateDropDownsAsync(vm, scope);
        return View(vm);
    }

    // POST: Activities/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ActivityCreateEditViewModel vm)
    {
        if (id != vm.ActivityId) return NotFound();

        var scope = await GetUserScopeAsync();
        var activity = await ApplyScope(_context.Activities, scope.currentUserId, scope.isAdmin, scope.isManager, scope.teamUserIds)
            .FirstOrDefaultAsync(a => a.ActivityId == id);

        if (activity == null) return NotFound();

        if (ModelState.IsValid)
        {
            activity.ActivityType = vm.ActivityType;
            activity.Subject = vm.Subject;
            activity.Description = vm.Description;
            activity.ActivityDate = vm.ActivityDate;
            activity.CustomerId = vm.CustomerId;
            activity.LeadId = vm.LeadId;
            activity.OpportunityId = vm.OpportunityId;
            activity.Status = vm.Status;
            activity.AssignedTo = vm.AssignedTo;

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                "ActivityUpdated",
                "Activity",
                activity.ActivityId.ToString(),
                $"Activity {activity.ActivityId} updated: {activity.Subject}");

            TempData["SuccessMessage"] = "Activity updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        await PopulateDropDownsAsync(vm, scope);
        return View(vm);
    }

    // GET: Activities/Delete/5
    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();

        var scope = await GetUserScopeAsync();
        var activity = await ApplyScope(_context.Activities, scope.currentUserId, scope.isAdmin, scope.isManager, scope.teamUserIds)
            .Include(a => a.Customer)
            .Include(a => a.Lead)
            .Include(a => a.Opportunity)
            .FirstOrDefaultAsync(a => a.ActivityId == id);

        if (activity == null) return NotFound();

        return View(activity);
    }

    // POST: Activities/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var scope = await GetUserScopeAsync();
        var activity = await ApplyScope(_context.Activities, scope.currentUserId, scope.isAdmin, scope.isManager, scope.teamUserIds)
            .FirstOrDefaultAsync(a => a.ActivityId == id);

        if (activity == null) return NotFound();

        activity.IsDeleted = true;
        await _context.SaveChangesAsync();

        await _auditService.LogAsync(
            "ActivityDeleted",
            "Activity",
            activity.ActivityId.ToString(),
            $"Activity {id} soft deleted");

        TempData["SuccessMessage"] = "Activity deleted.";
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateDropDownsAsync(ActivityCreateEditViewModel vm, (string currentUserId, bool isAdmin, bool isManager, List<string> teamUserIds) scope)
    {
        var customers = await GetScopedCustomersQuery(scope).OrderBy(c => c.CustomerName).ToListAsync();
        var leads = await GetScopedLeadsQuery(scope).OrderBy(l => l.LeadName).ToListAsync();
        var opportunities = await GetScopedOpportunitiesQuery(scope).OrderBy(o => o.OpportunityName).ToListAsync();
        var users = await GetAssignableUsersQuery(scope).ToListAsync();

        vm.CustomerList = new SelectList(customers, "CustomerId", "CustomerName", vm.CustomerId);
        vm.LeadList = new SelectList(leads, "LeadId", "LeadName", vm.LeadId);
        vm.OpportunityList = new SelectList(opportunities, "OpportunityId", "OpportunityName", vm.OpportunityId);
        vm.UserList = new SelectList(users, "Id", "DisplayName", vm.AssignedTo);
    }

    private IQueryable<Customer> GetScopedCustomersQuery((string currentUserId, bool isAdmin, bool isManager, List<string> teamUserIds) scope)
    {
        var q = _context.Customers.Where(c => !c.IsDeleted && c.Status == "Active");
        if (scope.isAdmin) return q;
        if (scope.isManager) return q.Where(c => c.AssignedTo != null && scope.teamUserIds.Contains(c.AssignedTo));
        return q.Where(c => c.AssignedTo == scope.currentUserId);
    }

    private IQueryable<Lead> GetScopedLeadsQuery((string currentUserId, bool isAdmin, bool isManager, List<string> teamUserIds) scope)
    {
        var q = _context.Leads.Where(l => !l.IsDeleted);
        if (scope.isAdmin) return q;
        if (scope.isManager) return q.Where(l => l.AssignedTo != null && scope.teamUserIds.Contains(l.AssignedTo));
        return q.Where(l => l.AssignedTo == scope.currentUserId);
    }

    private IQueryable<Opportunity> GetScopedOpportunitiesQuery((string currentUserId, bool isAdmin, bool isManager, List<string> teamUserIds) scope)
    {
        var q = _context.Opportunities.Where(o => !o.IsDeleted);
        if (scope.isAdmin) return q;
        if (scope.isManager) return q.Where(o => o.AssignedTo != null && scope.teamUserIds.Contains(o.AssignedTo));
        return q.Where(o => o.AssignedTo == scope.currentUserId);
    }

    private IQueryable<UserOption> GetAssignableUsersQuery((string currentUserId, bool isAdmin, bool isManager, List<string> teamUserIds) scope)
    {
        if (scope.isAdmin)
        {
            return _context.Users.Select(u => new UserOption { Id = u.Id, DisplayName = (u.FullName ?? u.UserName ?? u.Email) + " (" + u.UserName + ")" });
        }
        if (scope.isManager)
        {
            return _context.Users.Where(u => scope.teamUserIds.Contains(u.Id))
                .Select(u => new UserOption { Id = u.Id, DisplayName = (u.FullName ?? u.UserName ?? u.Email) + " (" + u.UserName + ")" });
        }
        return _context.Users.Where(u => u.Id == scope.currentUserId)
            .Select(u => new UserOption { Id = u.Id, DisplayName = (u.FullName ?? u.UserName ?? u.Email ?? "User") });
    }

    private class UserOption
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }
}
