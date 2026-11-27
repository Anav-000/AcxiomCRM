using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using AcxiomCRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Controllers;

[Authorize]
public class CustomersController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IScopedAccessService _scopedAccess;
    private readonly IAuditService _auditService;
    private readonly ILogger<CustomersController> _logger;

    public CustomersController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IScopedAccessService scopedAccess,
        IAuditService auditService,
        ILogger<CustomersController> logger)
    {
        _context = context;
        _userManager = userManager;
        _scopedAccess = scopedAccess;
        _auditService = auditService;
        _logger = logger;
    }

    private string GetIpAddress() =>
        HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";

    // GET: /Customers
    public async Task<IActionResult> Index(string? search, string? status)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var query = _context.Customers
            .Where(c => !c.IsDeleted)
            .AsQueryable();

        query = await _scopedAccess.ApplyScopeAsync(query, user, c => c.AssignedTo);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(c =>
                c.CustomerName.Contains(s) ||
                c.Email.Contains(s) ||
                c.Phone.Contains(s) ||
                (c.CompanyName != null && c.CompanyName.Contains(s)));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(c => c.Status == status);
        }

        var customers = await query
            .OrderByDescending(c => c.CreatedDate)
            .ToListAsync();

        var model = new CustomerListViewModel
        {
            Customers = customers,
            SearchTerm = search,
            StatusFilter = status
        };

        return View(model);
    }

    // GET: /Customers/Details/5
    public async Task<IActionResult> Details(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var customer = await _context.Customers
            .Include(c => c.Opportunities)
            .Include(c => c.FollowUps)
            .Include(c => c.Activities)
            .FirstOrDefaultAsync(c => c.CustomerId == id && !c.IsDeleted);

        if (customer == null)
        {
            return NotFound();
        }

        if (!await _scopedAccess.CanAccessRecordAsync(user, customer.AssignedTo))
        {
            return Forbid();
        }

        var model = new CustomerDetailsViewModel
        {
            Customer = customer,
            CanEdit = true,
            CanDelete = true
        };

        return View(model);
    }

    // GET: /Customers/Create
    public async Task<IActionResult> Create()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var canReassign = await _scopedAccess.CanReassignRecordAsync(user);
        var assignableUsers = await _scopedAccess.GetAssignableUsersAsync(user);

        var model = new CustomerCreateEditViewModel
        {
            AssignedTo = user.Email,
            CanReassign = canReassign,
            AssignableUsers = assignableUsers.Select(u => new SelectListItem
            {
                Value = u.Email,
                Text = $"{u.Name} ({u.Email})"
            }).ToList()
        };

        return View(model);
    }

    // POST: /Customers/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CustomerCreateEditViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var canReassign = await _scopedAccess.CanReassignRecordAsync(user);
        if (!canReassign || string.IsNullOrWhiteSpace(model.AssignedTo))
        {
            model.AssignedTo = user.Email;
        }

        var normEmail = model.Email.Trim().ToUpperInvariant();
        var normPhone = new string(model.Phone.Where(char.IsDigit).ToArray());

        // Independent Email Uniqueness Check
        if (await _context.Customers.AnyAsync(c => !c.IsDeleted && c.NormalizedEmail == normEmail))
        {
            ModelState.AddModelError("Email", "Email address is already in use by another customer.");
        }

        // Independent Phone Uniqueness Check
        if (await _context.Customers.AnyAsync(c => !c.IsDeleted && c.NormalizedPhone == normPhone))
        {
            ModelState.AddModelError("Phone", "Phone number is already in use by another customer.");
        }

        if (!ModelState.IsValid)
        {
            model.CanReassign = canReassign;
            var users = await _scopedAccess.GetAssignableUsersAsync(user);
            model.AssignableUsers = users.Select(u => new SelectListItem
            {
                Value = u.Email,
                Text = $"{u.Name} ({u.Email})",
                Selected = u.Email == model.AssignedTo
            }).ToList();

            return View(model);
        }

        // Generate unique CustomerCode
        var customerCode = $"CUST-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..4].ToUpper()}";

        var customer = new Customer
        {
            CustomerCode = customerCode,
            CustomerName = model.CustomerName.Trim(),
            Email = model.Email.Trim(),
            NormalizedEmail = normEmail,
            Phone = model.Phone.Trim(),
            NormalizedPhone = normPhone,
            CompanyName = model.CompanyName?.Trim(),
            Address = model.Address?.Trim(),
            City = model.City?.Trim(),
            State = model.State?.Trim(),
            Status = model.Status,
            AssignedTo = model.AssignedTo,
            CreatedBy = user.Email ?? user.UserName ?? user.Id,
            CreatedDate = DateTime.UtcNow,
            IsDeleted = false
        };

        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        // Audit Log
        await _auditService.LogAsync(
            action: "Create",
            entityName: "Customer",
            recordId: customer.CustomerId.ToString(),
            oldValue: null,
            newValue: $"Customer '{customer.CustomerName}' ({customer.CustomerCode}) created. Email: {customer.Email}, Phone: {customer.Phone}, AssignedTo: {customer.AssignedTo}",
            userId: user.Id,
            ipAddress: GetIpAddress());

        TempData["SuccessMessage"] = $"Customer '{customer.CustomerName}' created successfully.";
        return RedirectToAction(nameof(Index));
    }

    // GET: /Customers/Edit/5
    public async Task<IActionResult> Edit(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.CustomerId == id && !c.IsDeleted);
        if (customer == null)
        {
            return NotFound();
        }

        if (!await _scopedAccess.CanAccessRecordAsync(user, customer.AssignedTo))
        {
            return Forbid();
        }

        var canReassign = await _scopedAccess.CanReassignRecordAsync(user);
        var assignableUsers = await _scopedAccess.GetAssignableUsersAsync(user);

        var model = new CustomerCreateEditViewModel
        {
            CustomerId = customer.CustomerId,
            CustomerCode = customer.CustomerCode,
            CustomerName = customer.CustomerName,
            Email = customer.Email,
            Phone = customer.Phone,
            CompanyName = customer.CompanyName,
            Address = customer.Address,
            City = customer.City,
            State = customer.State,
            Status = customer.Status,
            AssignedTo = customer.AssignedTo,
            CanReassign = canReassign,
            AssignableUsers = assignableUsers.Select(u => new SelectListItem
            {
                Value = u.Email,
                Text = $"{u.Name} ({u.Email})",
                Selected = u.Email == customer.AssignedTo
            }).ToList()
        };

        return View(model);
    }

    // POST: /Customers/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, CustomerCreateEditViewModel model)
    {
        if (id != model.CustomerId)
        {
            return NotFound();
        }

        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.CustomerId == id && !c.IsDeleted);
        if (customer == null)
        {
            return NotFound();
        }

        if (!await _scopedAccess.CanAccessRecordAsync(user, customer.AssignedTo))
        {
            return Forbid();
        }

        var canReassign = await _scopedAccess.CanReassignRecordAsync(user);
        if (!canReassign)
        {
            // SalesExecutive cannot reassign
            model.AssignedTo = customer.AssignedTo;
        }

        var normEmail = model.Email.Trim().ToUpperInvariant();
        var normPhone = new string(model.Phone.Where(char.IsDigit).ToArray());

        // Independent Email Uniqueness Check
        if (await _context.Customers.AnyAsync(c => c.CustomerId != id && !c.IsDeleted && c.NormalizedEmail == normEmail))
        {
            ModelState.AddModelError("Email", "Email address is already in use by another customer.");
        }

        // Independent Phone Uniqueness Check
        if (await _context.Customers.AnyAsync(c => c.CustomerId != id && !c.IsDeleted && c.NormalizedPhone == normPhone))
        {
            ModelState.AddModelError("Phone", "Phone number is already in use by another customer.");
        }

        if (!ModelState.IsValid)
        {
            model.CanReassign = canReassign;
            var users = await _scopedAccess.GetAssignableUsersAsync(user);
            model.AssignableUsers = users.Select(u => new SelectListItem
            {
                Value = u.Email,
                Text = $"{u.Name} ({u.Email})",
                Selected = u.Email == model.AssignedTo
            }).ToList();

            return View(model);
        }

        var oldSummary = $"Name: {customer.CustomerName}, Email: {customer.Email}, Phone: {customer.Phone}, Status: {customer.Status}, AssignedTo: {customer.AssignedTo}";

        customer.CustomerName = model.CustomerName.Trim();
        customer.Email = model.Email.Trim();
        customer.NormalizedEmail = normEmail;
        customer.Phone = model.Phone.Trim();
        customer.NormalizedPhone = normPhone;
        customer.CompanyName = model.CompanyName?.Trim();
        customer.Address = model.Address?.Trim();
        customer.City = model.City?.Trim();
        customer.State = model.State?.Trim();
        customer.Status = model.Status;
        customer.AssignedTo = model.AssignedTo;

        await _context.SaveChangesAsync();

        var newSummary = $"Name: {customer.CustomerName}, Email: {customer.Email}, Phone: {customer.Phone}, Status: {customer.Status}, AssignedTo: {customer.AssignedTo}";

        await _auditService.LogAsync(
            action: "Update",
            entityName: "Customer",
            recordId: customer.CustomerId.ToString(),
            oldValue: oldSummary,
            newValue: newSummary,
            userId: user.Id,
            ipAddress: GetIpAddress());

        TempData["SuccessMessage"] = $"Customer '{customer.CustomerName}' updated successfully.";
        return RedirectToAction(nameof(Index));
    }

    // GET: /Customers/Delete/5
    public async Task<IActionResult> Delete(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.CustomerId == id && !c.IsDeleted);
        if (customer == null)
        {
            return NotFound();
        }

        if (!await _scopedAccess.CanAccessRecordAsync(user, customer.AssignedTo))
        {
            return Forbid();
        }

        return View(customer);
    }

    // POST: /Customers/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.CustomerId == id && !c.IsDeleted);
        if (customer == null)
        {
            return NotFound();
        }

        if (!await _scopedAccess.CanAccessRecordAsync(user, customer.AssignedTo))
        {
            return Forbid();
        }

        var oldStatus = $"Status: {customer.Status}, IsDeleted: {customer.IsDeleted}";

        // Soft delete / deactivation
        customer.IsDeleted = true;
        customer.Status = "Inactive";
        await _context.SaveChangesAsync();

        await _auditService.LogAsync(
            action: "Delete",
            entityName: "Customer",
            recordId: customer.CustomerId.ToString(),
            oldValue: oldStatus,
            newValue: "Status: Inactive, IsDeleted: True",
            userId: user.Id,
            ipAddress: GetIpAddress());

        TempData["SuccessMessage"] = $"Customer '{customer.CustomerName}' deactivated successfully.";
        return RedirectToAction(nameof(Index));
    }
}
