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

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly IAuditService _auditService;

    public AdminController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IAuditService auditService)
    {
        _context = context;
        _userManager = userManager;
        _roleManager = roleManager;
        _auditService = auditService;
    }

    private string GetIpAddress() =>
        HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";

    // GET: /Admin/Users
    public async Task<IActionResult> Users(string? search, string? role)
    {
        var query = _context.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(u => (u.Email != null && u.Email.Contains(s)) || u.Name.Contains(s) || (u.UserName != null && u.UserName.Contains(s)));
        }

        var users = await query.OrderByDescending(u => u.CreatedDate).ToListAsync();
        var userItems = new List<AdminUserItemViewModel>();

        var allManagers = await _context.Users
            .ToDictionaryAsync(u => u.Id, u => u.Name);

        foreach (var u in users)
        {
            var userRoles = await _userManager.GetRolesAsync(u);
            var primaryRole = userRoles.FirstOrDefault() ?? "SalesExecutive";

            if (!string.IsNullOrWhiteSpace(role) && !string.Equals(primaryRole, role, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var isLocked = u.LockoutEnd.HasValue && u.LockoutEnd.Value > DateTimeOffset.UtcNow;

            userItems.Add(new AdminUserItemViewModel
            {
                Id = u.Id,
                Email = u.Email ?? string.Empty,
                Name = u.Name,
                Role = primaryRole,
                IsActive = u.IsActive,
                IsLockedOut = isLocked,
                LockoutEnd = u.LockoutEnd,
                AccessFailedCount = u.AccessFailedCount,
                CreatedDate = u.CreatedDate,
                ManagerName = (!string.IsNullOrEmpty(u.ManagerId) && allManagers.ContainsKey(u.ManagerId)) ? allManagers[u.ManagerId] : null
            });
        }

        var vm = new AdminUserListViewModel
        {
            Users = userItems,
            SearchTerm = search,
            RoleFilter = role
        };

        return View(vm);
    }

    // GET: /Admin/UserDetails/5
    public async Task<IActionResult> UserDetails(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        var roles = await _userManager.GetRolesAsync(user);
        var primaryRole = roles.FirstOrDefault() ?? "SalesExecutive";

        string? managerName = null;
        if (!string.IsNullOrEmpty(user.ManagerId))
        {
            var m = await _userManager.FindByIdAsync(user.ManagerId);
            managerName = m?.Name;
        }

        var vm = new AdminUserItemViewModel
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            Name = user.Name,
            Role = primaryRole,
            IsActive = user.IsActive,
            IsLockedOut = user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow,
            LockoutEnd = user.LockoutEnd,
            AccessFailedCount = user.AccessFailedCount,
            CreatedDate = user.CreatedDate,
            ManagerName = managerName
        };

        return View(vm);
    }

    // GET: /Admin/UserEdit/5
    public async Task<IActionResult> UserEdit(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        var roles = await _userManager.GetRolesAsync(user);
        var currentRole = roles.FirstOrDefault() ?? "SalesExecutive";

        var managers = await _context.Users
            .Where(u => u.Id != id)
            .Select(u => new { u.Id, DisplayName = $"{u.Name} ({u.Email})" })
            .ToListAsync();

        var vm = new AdminUserEditViewModel
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            Name = user.Name,
            Role = currentRole,
            ManagerId = user.ManagerId,
            IsActive = user.IsActive,
            AvailableRoles = new SelectList(new[] { "Admin", "Manager", "SalesExecutive" }, currentRole),
            AvailableManagers = new SelectList(managers, "Id", "DisplayName", user.ManagerId)
        };

        return View(vm);
    }

    // POST: /Admin/UserEdit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UserEdit(string id, AdminUserEditViewModel vm)
    {
        if (id != vm.Id) return NotFound();

        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        if (ModelState.IsValid)
        {
            var adminUser = await _userManager.GetUserAsync(User);
            var oldRole = (await _userManager.GetRolesAsync(user)).FirstOrDefault() ?? "None";
            var oldStatus = user.IsActive ? "Active" : "Inactive";

            user.Name = vm.Name.Trim();
            user.ManagerId = string.IsNullOrWhiteSpace(vm.ManagerId) ? null : vm.ManagerId;
            user.IsActive = vm.IsActive;

            await _userManager.UpdateAsync(user);

            // Update role if changed
            if (oldRole != vm.Role)
            {
                var existingRoles = await _userManager.GetRolesAsync(user);
                await _userManager.RemoveFromRolesAsync(user, existingRoles);
                await _userManager.AddToRoleAsync(user, vm.Role);

                await _auditService.LogAsync(
                    action: "Role Change",
                    entityName: "User",
                    recordId: user.Id,
                    oldValue: $"Role: {oldRole}",
                    newValue: $"Role: {vm.Role} assigned by {adminUser?.Email}",
                    userId: adminUser?.Id,
                    ipAddress: GetIpAddress());
            }

            var newStatus = user.IsActive ? "Active" : "Inactive";
            if (oldStatus != newStatus)
            {
                await _auditService.LogAsync(
                    action: "Update",
                    entityName: "User",
                    recordId: user.Id,
                    oldValue: $"Status: {oldStatus}",
                    newValue: $"Status: {newStatus}",
                    userId: adminUser?.Id,
                    ipAddress: GetIpAddress());
            }

            TempData["SuccessMessage"] = $"User '{user.Email}' updated successfully.";
            return RedirectToAction(nameof(Users));
        }

        var managers = await _context.Users
            .Where(u => u.Id != id)
            .Select(u => new { u.Id, DisplayName = $"{u.Name} ({u.Email})" })
            .ToListAsync();

        vm.AvailableRoles = new SelectList(new[] { "Admin", "Manager", "SalesExecutive" }, vm.Role);
        vm.AvailableManagers = new SelectList(managers, "Id", "DisplayName", vm.ManagerId);
        return View(vm);
    }

    // POST: /Admin/ToggleActive/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        var adminUser = await _userManager.GetUserAsync(User);
        if (adminUser?.Id == user.Id)
        {
            TempData["ErrorMessage"] = "You cannot deactivate your own administrative account.";
            return RedirectToAction(nameof(Users));
        }

        user.IsActive = !user.IsActive;
        await _userManager.UpdateAsync(user);

        await _auditService.LogAsync(
            action: "Update",
            entityName: "User",
            recordId: user.Id,
            oldValue: $"IsActive: {!user.IsActive}",
            newValue: $"IsActive: {user.IsActive}",
            userId: adminUser?.Id,
            ipAddress: GetIpAddress());

        TempData["SuccessMessage"] = $"User '{user.Email}' is now {(user.IsActive ? "Active" : "Inactive")}.";
        return RedirectToAction(nameof(Users));
    }

    // POST: /Admin/ResetPassword/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(string id, string newPassword)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        var adminUser = await _userManager.GetUserAsync(User);

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
        {
            TempData["ErrorMessage"] = "Password must be at least 8 characters long and satisfy password policy.";
            return RedirectToAction(nameof(UserDetails), new { id });
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, newPassword);

        if (result.Succeeded)
        {
            await _auditService.LogAsync(
                action: "Security",
                entityName: "User",
                recordId: user.Id,
                oldValue: "Password reset",
                newValue: $"Password reset executed by Administrator {adminUser?.Email}",
                userId: adminUser?.Id,
                ipAddress: GetIpAddress());

            TempData["SuccessMessage"] = $"Password for '{user.Email}' was successfully reset.";
        }
        else
        {
            TempData["ErrorMessage"] = "Failed to reset password: " + string.Join(", ", result.Errors.Select(e => e.Description));
        }

        return RedirectToAction(nameof(UserDetails), new { id });
    }

    // POST: /Admin/Unlock/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unlock(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        var adminUser = await _userManager.GetUserAsync(User);

        await _userManager.SetLockoutEndDateAsync(user, null);
        await _userManager.ResetAccessFailedCountAsync(user);

        await _auditService.LogAsync(
            action: "Security",
            entityName: "User",
            recordId: user.Id,
            oldValue: "Account locked",
            newValue: $"Account unlocked by Administrator {adminUser?.Email}",
            userId: adminUser?.Id,
            ipAddress: GetIpAddress());

        TempData["SuccessMessage"] = $"Account '{user.Email}' has been unlocked.";
        return RedirectToAction(nameof(UserDetails), new { id });
    }

    // GET: /Admin/Roles
    public IActionResult Roles()
    {
        return View();
    }

    // GET: /Admin/AuditLogs
    public async Task<IActionResult> AuditLogs(string? userFilter, string? actionFilter, string? moduleFilter, DateTime? fromDate, DateTime? toDate)
    {
        var query = _context.AuditLogs.AsQueryable();

        if (!string.IsNullOrWhiteSpace(userFilter))
        {
            query = query.Where(a => a.UserId != null && a.UserId.Contains(userFilter));
        }

        if (!string.IsNullOrWhiteSpace(actionFilter))
        {
            query = query.Where(a => a.Action == actionFilter);
        }

        if (!string.IsNullOrWhiteSpace(moduleFilter))
        {
            query = query.Where(a => a.EntityName == moduleFilter);
        }

        if (fromDate.HasValue)
        {
            query = query.Where(a => a.CreatedDate >= fromDate.Value.Date);
        }

        if (toDate.HasValue)
        {
            query = query.Where(a => a.CreatedDate <= toDate.Value.Date.AddDays(1).AddTicks(-1));
        }

        var logs = await query
            .OrderByDescending(a => a.CreatedDate)
            .Take(200)
            .ToListAsync();

        var actions = await _context.AuditLogs.Select(a => a.Action).Distinct().ToListAsync();
        var modules = await _context.AuditLogs.Select(a => a.EntityName).Distinct().ToListAsync();

        var vm = new AdminAuditLogsViewModel
        {
            Logs = logs,
            UserFilter = userFilter,
            ActionFilter = actionFilter,
            ModuleFilter = moduleFilter,
            FromDate = fromDate,
            ToDate = toDate,
            AvailableActions = new SelectList(actions, actionFilter),
            AvailableModules = new SelectList(modules, moduleFilter)
        };

        return View(vm);
    }
}
