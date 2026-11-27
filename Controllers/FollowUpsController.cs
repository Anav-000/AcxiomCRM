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
public class FollowUpsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditService _auditService;

    public FollowUpsController(
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

    private IQueryable<FollowUp> ApplyScope(IQueryable<FollowUp> query, string currentUserId, bool isAdmin, bool isManager, List<string> teamUserIds)
    {
        query = query.Where(f => !f.IsDeleted);

        if (isAdmin)
        {
            return query;
        }

        if (isManager)
        {
            return query.Where(f => f.AssignedTo != null && teamUserIds.Contains(f.AssignedTo));
        }

        return query.Where(f => f.AssignedTo == currentUserId);
    }

    // GET: FollowUps
    public async Task<IActionResult> Index(
        string? statusFilter,
        string? followUpTypeFilter,
        DateTime? dateFilter,
        string? ownerFilter,
        int? customerIdFilter,
        string? leadSearch,
        bool overdueOnly = false)
    {
        var scope = await GetUserScopeAsync();
        var query = ApplyScope(_context.FollowUps.AsNoTracking(), scope.currentUserId, scope.isAdmin, scope.isManager, scope.teamUserIds);

        // Compute summary metrics before filters
        var totalPlanned = await query.CountAsync(f => f.Status == "Planned");
        var totalOverdue = await query.CountAsync(f => f.Status == "Planned" && f.FollowUpDate.Date < DateTime.Today);
        var totalCompleted = await query.CountAsync(f => f.Status == "Completed");

        if (overdueOnly)
        {
            query = query.Where(f => f.Status == "Planned" && f.FollowUpDate.Date < DateTime.Today);
        }
        else if (!string.IsNullOrWhiteSpace(statusFilter))
        {
            query = query.Where(f => f.Status == statusFilter);
        }

        if (!string.IsNullOrWhiteSpace(followUpTypeFilter))
        {
            query = query.Where(f => f.FollowUpType == followUpTypeFilter);
        }

        if (dateFilter.HasValue)
        {
            query = query.Where(f => f.FollowUpDate.Date == dateFilter.Value.Date);
        }

        if (!string.IsNullOrWhiteSpace(ownerFilter))
        {
            query = query.Where(f => f.AssignedTo == ownerFilter);
        }

        if (customerIdFilter.HasValue)
        {
            query = query.Where(f => f.CustomerId == customerIdFilter.Value);
        }

        if (!string.IsNullOrWhiteSpace(leadSearch))
        {
            query = query.Where(f => f.Lead != null && EF.Functions.Like(f.Lead.LeadName, $"%{leadSearch}%"));
        }

        var items = await query
            .Include(f => f.Customer)
            .Include(f => f.Lead)
            .Include(f => f.Opportunity)
            .OrderBy(f => f.FollowUpDate)
            .ToListAsync();

        var userIds = items.Where(f => f.AssignedTo != null).Select(f => f.AssignedTo!).Distinct().ToList();
        var userDict = await _context.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.UserName ?? u.Email);

        var listItems = items.Select(f => new FollowUpListItemViewModel
        {
            FollowUpId = f.FollowUpId,
            CustomerId = f.CustomerId,
            CustomerName = f.Customer?.CustomerName,
            LeadId = f.LeadId,
            LeadName = f.Lead?.LeadName,
            OpportunityId = f.OpportunityId,
            OpportunityName = f.Opportunity?.OpportunityName,
            FollowUpDate = f.FollowUpDate,
            FollowUpType = f.FollowUpType,
            Remarks = f.Remarks,
            Status = f.Status,
            AssignedTo = f.AssignedTo,
            AssignedToName = f.AssignedTo != null && userDict.ContainsKey(f.AssignedTo) ? userDict[f.AssignedTo] : "Unassigned"
        }).ToList();

        // Dropdowns for filtering
        var scopedUsers = await GetAssignableUsersQuery(scope).ToListAsync();
        var scopedCustomers = await GetScopedCustomersQuery(scope).ToListAsync();

        var vm = new FollowUpListViewModel
        {
            FollowUps = listItems,
            StatusFilter = statusFilter,
            FollowUpTypeFilter = followUpTypeFilter,
            DateFilter = dateFilter,
            OwnerFilter = ownerFilter,
            CustomerIdFilter = customerIdFilter,
            LeadSearch = leadSearch,
            OverdueOnly = overdueOnly,
            TotalPlannedCount = totalPlanned,
            TotalOverdueCount = totalOverdue,
            TotalCompletedCount = totalCompleted,
            StatusList = new SelectList(new[] { "Planned", "Completed", "Cancelled" }, statusFilter),
            TypeList = new SelectList(new[] { "Call", "Meeting", "Email", "Task" }, followUpTypeFilter),
            UserList = new SelectList(scopedUsers, "Id", "DisplayName", ownerFilter),
            CustomerList = new SelectList(scopedCustomers, "CustomerId", "CustomerName", customerIdFilter)
        };

        return View(vm);
    }

    // GET: FollowUps/Details/5
    public async Task<IActionResult> Details(int? id)
    {
        if (id == null) return NotFound();

        var scope = await GetUserScopeAsync();
        var followUp = await ApplyScope(_context.FollowUps, scope.currentUserId, scope.isAdmin, scope.isManager, scope.teamUserIds)
            .Include(f => f.Customer)
            .Include(f => f.Lead)
            .Include(f => f.Opportunity)
            .FirstOrDefaultAsync(f => f.FollowUpId == id);

        if (followUp == null) return NotFound();

        string? assignedName = null;
        if (!string.IsNullOrEmpty(followUp.AssignedTo))
        {
            var u = await _context.Users.FindAsync(followUp.AssignedTo);
            assignedName = u?.FullName ?? u?.UserName;
        }

        var vm = new FollowUpDetailsViewModel
        {
            FollowUp = followUp,
            CustomerName = followUp.Customer?.CustomerName,
            LeadName = followUp.Lead?.LeadName,
            OpportunityName = followUp.Opportunity?.OpportunityName,
            AssignedToName = assignedName
        };

        return View(vm);
    }

    // GET: FollowUps/Create
    public async Task<IActionResult> Create(int? customerId, int? leadId, int? opportunityId)
    {
        var scope = await GetUserScopeAsync();
        var vm = new FollowUpCreateEditViewModel
        {
            CustomerId = customerId,
            LeadId = leadId,
            OpportunityId = opportunityId,
            AssignedTo = scope.currentUserId,
            FollowUpDate = DateTime.Today.AddDays(1)
        };

        await PopulateDropDownsAsync(vm, scope);
        return View(vm);
    }

    // POST: FollowUps/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(FollowUpCreateEditViewModel vm)
    {
        var scope = await GetUserScopeAsync();

        if (vm.FollowUpDate.Date < DateTime.Today)
        {
            ModelState.AddModelError(nameof(vm.FollowUpDate), "Follow-up date cannot be in the past for new follow-ups.");
        }

        // Validate scoped links
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
            var followUp = new FollowUp
            {
                CustomerId = vm.CustomerId,
                LeadId = vm.LeadId,
                OpportunityId = vm.OpportunityId,
                FollowUpDate = vm.FollowUpDate,
                FollowUpType = vm.FollowUpType,
                Remarks = vm.Remarks,
                Status = vm.Status,
                AssignedTo = string.IsNullOrWhiteSpace(vm.AssignedTo) ? scope.currentUserId : vm.AssignedTo,
                IsDeleted = false
            };

            _context.FollowUps.Add(followUp);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                "FollowUpCreated",
                "FollowUp",
                followUp.FollowUpId.ToString(),
                $"FollowUp {followUp.FollowUpId} ({followUp.FollowUpType}) scheduled for {followUp.FollowUpDate:yyyy-MM-dd} assigned to {followUp.AssignedTo}");

            TempData["SuccessMessage"] = "Follow-up scheduled successfully.";
            return RedirectToAction(nameof(Index));
        }

        await PopulateDropDownsAsync(vm, scope);
        return View(vm);
    }

    // GET: FollowUps/Edit/5
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();

        var scope = await GetUserScopeAsync();
        var followUp = await ApplyScope(_context.FollowUps, scope.currentUserId, scope.isAdmin, scope.isManager, scope.teamUserIds)
            .FirstOrDefaultAsync(f => f.FollowUpId == id);

        if (followUp == null) return NotFound();

        var vm = new FollowUpCreateEditViewModel
        {
            FollowUpId = followUp.FollowUpId,
            CustomerId = followUp.CustomerId,
            LeadId = followUp.LeadId,
            OpportunityId = followUp.OpportunityId,
            FollowUpDate = followUp.FollowUpDate,
            FollowUpType = followUp.FollowUpType,
            Remarks = followUp.Remarks,
            Status = followUp.Status,
            AssignedTo = followUp.AssignedTo
        };

        await PopulateDropDownsAsync(vm, scope);
        return View(vm);
    }

    // POST: FollowUps/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, FollowUpCreateEditViewModel vm)
    {
        if (id != vm.FollowUpId) return NotFound();

        var scope = await GetUserScopeAsync();
        var followUp = await ApplyScope(_context.FollowUps, scope.currentUserId, scope.isAdmin, scope.isManager, scope.teamUserIds)
            .FirstOrDefaultAsync(f => f.FollowUpId == id);

        if (followUp == null) return NotFound();

        if (vm.Status == "Planned" && vm.FollowUpDate.Date < DateTime.Today)
        {
            ModelState.AddModelError(nameof(vm.FollowUpDate), "Follow-up date cannot be in the past for planned follow-ups.");
        }

        if (ModelState.IsValid)
        {
            var oldDate = followUp.FollowUpDate;
            var oldStatus = followUp.Status;

            followUp.CustomerId = vm.CustomerId;
            followUp.LeadId = vm.LeadId;
            followUp.OpportunityId = vm.OpportunityId;
            followUp.FollowUpDate = vm.FollowUpDate;
            followUp.FollowUpType = vm.FollowUpType;
            followUp.Remarks = vm.Remarks;
            followUp.Status = vm.Status;
            followUp.AssignedTo = vm.AssignedTo;

            await _context.SaveChangesAsync();

            if (oldDate.Date != vm.FollowUpDate.Date)
            {
                await _auditService.LogAsync(
                    "FollowUpRescheduled",
                    "FollowUp",
                    followUp.FollowUpId.ToString(),
                    $"FollowUp {id} rescheduled from {oldDate:yyyy-MM-dd} to {vm.FollowUpDate:yyyy-MM-dd}");
            }

            if (oldStatus != vm.Status && vm.Status == "Completed")
            {
                await _auditService.LogAsync(
                    "FollowUpCompleted",
                    "FollowUp",
                    followUp.FollowUpId.ToString(),
                    $"FollowUp {id} marked Completed");
            }
            else
            {
                await _auditService.LogAsync(
                    "FollowUpUpdated",
                    "FollowUp",
                    followUp.FollowUpId.ToString(),
                    $"FollowUp {id} updated. Status: {followUp.Status}");
            }

            TempData["SuccessMessage"] = "Follow-up updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        await PopulateDropDownsAsync(vm, scope);
        return View(vm);
    }

    // POST: FollowUps/Complete/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(int id)
    {
        var scope = await GetUserScopeAsync();
        var followUp = await ApplyScope(_context.FollowUps, scope.currentUserId, scope.isAdmin, scope.isManager, scope.teamUserIds)
            .FirstOrDefaultAsync(f => f.FollowUpId == id);

        if (followUp == null) return NotFound();

        followUp.Status = "Completed";
        await _context.SaveChangesAsync();

        await _auditService.LogAsync(
            "FollowUpCompleted",
            "FollowUp",
            followUp.FollowUpId.ToString(),
            $"FollowUp {id} marked Completed (original date was {followUp.FollowUpDate:yyyy-MM-dd})");

        TempData["SuccessMessage"] = "Follow-up marked as Completed.";
        return RedirectToAction(nameof(Index));
    }

    // POST: FollowUps/Cancel/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var scope = await GetUserScopeAsync();
        var followUp = await ApplyScope(_context.FollowUps, scope.currentUserId, scope.isAdmin, scope.isManager, scope.teamUserIds)
            .FirstOrDefaultAsync(f => f.FollowUpId == id);

        if (followUp == null) return NotFound();

        followUp.Status = "Cancelled";
        await _context.SaveChangesAsync();

        await _auditService.LogAsync(
            "FollowUpCancelled",
            "FollowUp",
            followUp.FollowUpId.ToString(),
            $"FollowUp {id} marked Cancelled");

        TempData["SuccessMessage"] = "Follow-up cancelled.";
        return RedirectToAction(nameof(Index));
    }

    // GET: FollowUps/Delete/5
    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();

        var scope = await GetUserScopeAsync();
        var followUp = await ApplyScope(_context.FollowUps, scope.currentUserId, scope.isAdmin, scope.isManager, scope.teamUserIds)
            .Include(f => f.Customer)
            .Include(f => f.Lead)
            .Include(f => f.Opportunity)
            .FirstOrDefaultAsync(f => f.FollowUpId == id);

        if (followUp == null) return NotFound();

        return View(followUp);
    }

    // POST: FollowUps/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var scope = await GetUserScopeAsync();
        var followUp = await ApplyScope(_context.FollowUps, scope.currentUserId, scope.isAdmin, scope.isManager, scope.teamUserIds)
            .FirstOrDefaultAsync(f => f.FollowUpId == id);

        if (followUp == null) return NotFound();

        followUp.IsDeleted = true;
        await _context.SaveChangesAsync();

        await _auditService.LogAsync(
            "FollowUpDeleted",
            "FollowUp",
            followUp.FollowUpId.ToString(),
            $"FollowUp {id} soft deleted");

        TempData["SuccessMessage"] = "Follow-up deleted.";
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateDropDownsAsync(FollowUpCreateEditViewModel vm, (string currentUserId, bool isAdmin, bool isManager, List<string> teamUserIds) scope)
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
