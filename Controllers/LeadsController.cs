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
public class LeadsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IScopedAccessService _scopedAccess;
    private readonly IAuditService _auditService;
    private readonly ILogger<LeadsController> _logger;

    private static readonly string[] ValidStatuses =
    [
        "New", "Contacted", "Qualified", "Unqualified", "Converted", "Lost"
    ];

    private static readonly Dictionary<string, HashSet<string>> AllowedTransitions = new()
    {
        { "New", new() { "New", "Contacted", "Unqualified", "Lost" } },
        { "Contacted", new() { "Contacted", "Qualified", "Unqualified", "Lost" } },
        { "Qualified", new() { "Qualified", "Converted", "Unqualified", "Lost" } },
        { "Unqualified", new() { "Unqualified", "Contacted", "Lost" } },
        { "Lost", new() { "Lost", "Contacted" } },
        { "Converted", new() { "Converted" } } // Terminal
    };

    public LeadsController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IScopedAccessService scopedAccess,
        IAuditService auditService,
        ILogger<LeadsController> logger)
    {
        _context = context;
        _userManager = userManager;
        _scopedAccess = scopedAccess;
        _auditService = auditService;
        _logger = logger;
    }

    private string GetIpAddress() =>
        HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";

    // GET: /Leads
    public async Task<IActionResult> Index(string? name, string? company, string? status, string? assignedUser)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var query = _context.Leads
            .Include(l => l.ConvertedCustomer)
            .Where(l => !l.IsDeleted)
            .AsQueryable();

        query = await _scopedAccess.ApplyScopeAsync(query, user, l => l.AssignedTo);

        if (!string.IsNullOrWhiteSpace(name))
        {
            var n = name.Trim();
            query = query.Where(l => l.LeadName.Contains(n) || l.Email.Contains(n) || l.Phone.Contains(n));
        }

        if (!string.IsNullOrWhiteSpace(company))
        {
            var c = company.Trim();
            query = query.Where(l => l.CompanyName != null && l.CompanyName.Contains(c));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(l => l.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(assignedUser))
        {
            query = query.Where(l => l.AssignedTo == assignedUser);
        }

        var leads = await query.OrderByDescending(l => l.CreatedDate).ToListAsync();
        var assignableUsers = await _scopedAccess.GetAssignableUsersAsync(user);

        var model = new LeadListViewModel
        {
            Leads = leads,
            NameFilter = name,
            CompanyFilter = company,
            StatusFilter = status,
            AssignedUserFilter = assignedUser,
            AvailableStatuses = ValidStatuses.Select(s => new SelectListItem(s, s, s == status)).ToList(),
            AvailableUsers = assignableUsers.Select(u => new SelectListItem($"{u.Name} ({u.Email})", u.Email, u.Email == assignedUser)).ToList()
        };

        return View(model);
    }

    // GET: /Leads/Details/5
    public async Task<IActionResult> Details(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var lead = await _context.Leads
            .Include(l => l.ConvertedCustomer)
            .Include(l => l.Opportunities)
            .Include(l => l.FollowUps)
            .Include(l => l.Activities)
            .FirstOrDefaultAsync(l => l.LeadId == id && !l.IsDeleted);

        if (lead == null)
        {
            return NotFound();
        }

        if (!await _scopedAccess.CanAccessRecordAsync(user, lead.AssignedTo))
        {
            return Forbid();
        }

        var model = new LeadDetailsViewModel
        {
            Lead = lead,
            CanEdit = lead.Status != "Converted",
            CanDelete = true,
            CanConvert = lead.Status == "Qualified" && lead.ConvertedCustomerId == null
        };

        return View(model);
    }

    // GET: /Leads/Create
    public async Task<IActionResult> Create()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var canReassign = await _scopedAccess.CanReassignRecordAsync(user);
        var assignableUsers = await _scopedAccess.GetAssignableUsersAsync(user);

        var model = new LeadCreateEditViewModel
        {
            Status = "New",
            ExpectedValue = 1000.00m,
            AssignedTo = user.Email,
            CanReassign = canReassign,
            AssignableUsers = assignableUsers.Select(u => new SelectListItem($"{u.Name} ({u.Email})", u.Email)).ToList(),
            AvailableStatuses = new List<SelectListItem> { new("New", "New") }
        };

        return View(model);
    }

    // POST: /Leads/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(LeadCreateEditViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var canReassign = await _scopedAccess.CanReassignRecordAsync(user);
        if (!canReassign || string.IsNullOrWhiteSpace(model.AssignedTo))
        {
            model.AssignedTo = user.Email;
        }

        // New leads must start as New
        model.Status = "New";

        if (model.ExpectedValue <= 0)
        {
            ModelState.AddModelError("ExpectedValue", "Expected Value must be greater than 0.");
        }

        if (!ModelState.IsValid)
        {
            model.CanReassign = canReassign;
            var users = await _scopedAccess.GetAssignableUsersAsync(user);
            model.AssignableUsers = users.Select(u => new SelectListItem($"{u.Name} ({u.Email})", u.Email, u.Email == model.AssignedTo)).ToList();
            model.AvailableStatuses = new List<SelectListItem> { new("New", "New") };
            return View(model);
        }

        var leadCode = $"LEAD-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..4].ToUpper()}";

        var lead = new Lead
        {
            LeadCode = leadCode,
            LeadName = model.LeadName.Trim(),
            Email = model.Email.Trim(),
            Phone = model.Phone.Trim(),
            CompanyName = model.CompanyName?.Trim(),
            Source = model.Source?.Trim(),
            Status = "New",
            ExpectedValue = model.ExpectedValue,
            AssignedTo = model.AssignedTo,
            CreatedDate = DateTime.UtcNow,
            IsDeleted = false
        };

        _context.Leads.Add(lead);
        await _context.SaveChangesAsync();

        await _auditService.LogAsync(
            action: "Create",
            entityName: "Lead",
            recordId: lead.LeadId.ToString(),
            oldValue: null,
            newValue: $"Lead '{lead.LeadName}' ({lead.LeadCode}) created. ExpectedValue: {lead.ExpectedValue:C}, AssignedTo: {lead.AssignedTo}",
            userId: user.Id,
            ipAddress: GetIpAddress());

        TempData["SuccessMessage"] = $"Lead '{lead.LeadName}' created successfully.";
        return RedirectToAction(nameof(Index));
    }

    // GET: /Leads/Edit/5
    public async Task<IActionResult> Edit(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var lead = await _context.Leads.FirstOrDefaultAsync(l => l.LeadId == id && !l.IsDeleted);
        if (lead == null)
        {
            return NotFound();
        }

        if (!await _scopedAccess.CanAccessRecordAsync(user, lead.AssignedTo))
        {
            return Forbid();
        }

        if (lead.Status == "Converted")
        {
            TempData["ErrorMessage"] = "Converted leads cannot be modified.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var canReassign = await _scopedAccess.CanReassignRecordAsync(user);
        var assignableUsers = await _scopedAccess.GetAssignableUsersAsync(user);

        // Calculate allowed transition options (exclude Converted from Edit form since only Convert action can convert a lead)
        var allowedNext = (AllowedTransitions.ContainsKey(lead.Status)
            ? AllowedTransitions[lead.Status]
            : new HashSet<string> { lead.Status })
            .Where(s => s != "Converted" || lead.Status == "Converted")
            .ToList();

        var model = new LeadCreateEditViewModel
        {
            LeadId = lead.LeadId,
            LeadCode = lead.LeadCode,
            LeadName = lead.LeadName,
            Email = lead.Email,
            Phone = lead.Phone,
            CompanyName = lead.CompanyName,
            Source = lead.Source,
            Status = lead.Status,
            ExpectedValue = lead.ExpectedValue,
            AssignedTo = lead.AssignedTo,
            CanReassign = canReassign,
            AssignableUsers = assignableUsers.Select(u => new SelectListItem($"{u.Name} ({u.Email})", u.Email, u.Email == lead.AssignedTo)).ToList(),
            AvailableStatuses = allowedNext.Select(s => new SelectListItem(s, s, s == lead.Status)).ToList()
        };

        return View(model);
    }

    // POST: /Leads/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, LeadCreateEditViewModel model)
    {
        if (id != model.LeadId)
        {
            return NotFound();
        }

        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var lead = await _context.Leads.FirstOrDefaultAsync(l => l.LeadId == id && !l.IsDeleted);
        if (lead == null)
        {
            return NotFound();
        }

        if (!await _scopedAccess.CanAccessRecordAsync(user, lead.AssignedTo))
        {
            return Forbid();
        }

        if (lead.Status == "Converted")
        {
            ModelState.AddModelError(string.Empty, "Converted leads cannot be modified.");
        }

        var canReassign = await _scopedAccess.CanReassignRecordAsync(user);
        if (!canReassign)
        {
            model.AssignedTo = lead.AssignedTo;
        }

        // Ensure only the Convert action can move a lead to Converted
        if (model.Status == "Converted" && lead.Status != "Converted")
        {
            ModelState.AddModelError("Status", "Leads can only be moved to Converted via the Convert Lead action.");
        }

        // Validate Status Transition
        if (AllowedTransitions.TryGetValue(lead.Status, out var allowedNext))
        {
            if (!allowedNext.Contains(model.Status))
            {
                ModelState.AddModelError("Status", $"Invalid status transition from '{lead.Status}' to '{model.Status}'. Allowed: {string.Join(", ", allowedNext)}");
            }
        }

        if (model.ExpectedValue <= 0)
        {
            ModelState.AddModelError("ExpectedValue", "Expected Value must be greater than 0.");
        }

        if (!ModelState.IsValid)
        {
            model.CanReassign = canReassign;
            var users = await _scopedAccess.GetAssignableUsersAsync(user);
            model.AssignableUsers = users.Select(u => new SelectListItem($"{u.Name} ({u.Email})", u.Email, u.Email == model.AssignedTo)).ToList();
            var nextStatuses = AllowedTransitions.ContainsKey(lead.Status) ? AllowedTransitions[lead.Status] : new HashSet<string> { lead.Status };
            model.AvailableStatuses = nextStatuses.Select(s => new SelectListItem(s, s, s == model.Status)).ToList();
            return View(model);
        }

        var oldSummary = $"Name: {lead.LeadName}, Status: {lead.Status}, Value: {lead.ExpectedValue}, AssignedTo: {lead.AssignedTo}";

        lead.LeadName = model.LeadName.Trim();
        lead.Email = model.Email.Trim();
        lead.Phone = model.Phone.Trim();
        lead.CompanyName = model.CompanyName?.Trim();
        lead.Source = model.Source?.Trim();
        lead.Status = model.Status;
        lead.ExpectedValue = model.ExpectedValue;
        lead.AssignedTo = model.AssignedTo;

        await _context.SaveChangesAsync();

        var newSummary = $"Name: {lead.LeadName}, Status: {lead.Status}, Value: {lead.ExpectedValue}, AssignedTo: {lead.AssignedTo}";

        await _auditService.LogAsync(
            action: "Update",
            entityName: "Lead",
            recordId: lead.LeadId.ToString(),
            oldValue: oldSummary,
            newValue: newSummary,
            userId: user.Id,
            ipAddress: GetIpAddress());

        TempData["SuccessMessage"] = $"Lead '{lead.LeadName}' updated successfully.";
        return RedirectToAction(nameof(Index));
    }

    // GET: /Leads/Convert/5
    public async Task<IActionResult> Convert(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var lead = await _context.Leads.FirstOrDefaultAsync(l => l.LeadId == id && !l.IsDeleted);
        if (lead == null)
        {
            return NotFound();
        }

        if (!await _scopedAccess.CanAccessRecordAsync(user, lead.AssignedTo))
        {
            return Forbid();
        }

        if (lead.Status == "Converted" || lead.ConvertedCustomerId != null)
        {
            TempData["ErrorMessage"] = "This lead has already been converted.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (lead.Status != "Qualified")
        {
            TempData["ErrorMessage"] = "Only leads with status 'Qualified' can be converted.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var model = new LeadConvertViewModel
        {
            LeadId = lead.LeadId,
            LeadCode = lead.LeadCode,
            CustomerName = lead.LeadName,
            Email = lead.Email,
            Phone = lead.Phone,
            CompanyName = lead.CompanyName,
            AssignedTo = lead.AssignedTo,
            CreateOpportunity = true,
            OpportunityName = !string.IsNullOrWhiteSpace(lead.CompanyName)
                ? $"{lead.CompanyName} Opportunity"
                : $"{lead.LeadName} Opportunity",
            OpportunityAmount = lead.ExpectedValue > 0 ? lead.ExpectedValue : 5000m,
            OpportunityProbability = 50,
            ExpectedCloseDate = DateTime.Today.AddDays(30)
        };

        return View(model);
    }

    // POST: /Leads/Convert/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Convert(int id, LeadConvertViewModel model)
    {
        if (id != model.LeadId)
        {
            return NotFound();
        }

        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var lead = await _context.Leads.FirstOrDefaultAsync(l => l.LeadId == id && !l.IsDeleted);
        if (lead == null)
        {
            return NotFound();
        }

        if (!await _scopedAccess.CanAccessRecordAsync(user, lead.AssignedTo))
        {
            return Forbid();
        }

        // Prevent duplicate / repeated conversion
        if (lead.Status == "Converted" || lead.ConvertedCustomerId != null)
        {
            ModelState.AddModelError(string.Empty, "This lead has already been converted.");
        }

        if (lead.Status != "Qualified")
        {
            ModelState.AddModelError(string.Empty, "Only leads with status 'Qualified' can be converted.");
        }

        var normEmail = model.Email.Trim().ToUpperInvariant();
        var normPhone = new string(model.Phone.Where(char.IsDigit).ToArray());

        // Validate customer uniqueness independently
        if (await _context.Customers.AnyAsync(c => !c.IsDeleted && c.NormalizedEmail == normEmail))
        {
            ModelState.AddModelError("Email", "A customer with this email address already exists.");
        }

        if (await _context.Customers.AnyAsync(c => !c.IsDeleted && c.NormalizedPhone == normPhone))
        {
            ModelState.AddModelError("Phone", "A customer with this phone number already exists.");
        }

        // Validate Opportunity if requested
        if (model.CreateOpportunity)
        {
            if (string.IsNullOrWhiteSpace(model.OpportunityName))
            {
                ModelState.AddModelError("OpportunityName", "Opportunity Name is required when creating an opportunity.");
            }
            if (!model.OpportunityAmount.HasValue || model.OpportunityAmount.Value <= 0)
            {
                ModelState.AddModelError("OpportunityAmount", "Opportunity Amount must be greater than 0.");
            }
            if (!model.OpportunityProbability.HasValue || model.OpportunityProbability.Value < 0 || model.OpportunityProbability.Value > 100)
            {
                ModelState.AddModelError("OpportunityProbability", "Probability must be between 0 and 100.");
            }
            if (!model.ExpectedCloseDate.HasValue || model.ExpectedCloseDate.Value.Date < DateTime.Today)
            {
                ModelState.AddModelError("ExpectedCloseDate", "Expected Close Date cannot be in the past for active opportunities.");
            }
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        // Execute conversion in single transaction
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
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
                Status = "Active",
                AssignedTo = lead.AssignedTo,
                CreatedBy = user.Email ?? user.UserName ?? user.Id,
                CreatedDate = DateTime.UtcNow,
                IsDeleted = false
            };

            _context.Customers.Add(customer);
            await _context.SaveChangesAsync();

            Opportunity? opp = null;
            if (model.CreateOpportunity && model.OpportunityAmount.HasValue && model.ExpectedCloseDate.HasValue)
            {
                var prob = model.OpportunityProbability ?? 50;
                var amount = model.OpportunityAmount.Value;
                opp = new Opportunity
                {
                    OpportunityName = model.OpportunityName!.Trim(),
                    CustomerId = customer.CustomerId,
                    LeadId = lead.LeadId,
                    Amount = amount,
                    Stage = "Qualification",
                    Probability = prob,
                    ExpectedCloseDate = model.ExpectedCloseDate.Value,
                    Status = "Open",
                    AssignedTo = lead.AssignedTo,
                    CreatedDate = DateTime.UtcNow,
                    IsDeleted = false
                };

                _context.Opportunities.Add(opp);
                await _context.SaveChangesAsync();
            }

            // Update Lead to Converted
            lead.Status = "Converted";
            lead.ConvertedCustomerId = customer.CustomerId;
            await _context.SaveChangesAsync();

            var ip = GetIpAddress();

            // Commit audit logs
            await _auditService.LogAsync(
                action: "Convert",
                entityName: "Lead",
                recordId: lead.LeadId.ToString(),
                oldValue: "Status: Qualified",
                newValue: $"Status: Converted, CustomerId: {customer.CustomerId}" + (opp != null ? $", OpportunityId: {opp.OpportunityId}" : ""),
                userId: user.Id,
                ipAddress: ip);

            await _auditService.LogAsync(
                action: "Create",
                entityName: "Customer",
                recordId: customer.CustomerId.ToString(),
                oldValue: null,
                newValue: $"Created via conversion from Lead #{lead.LeadId} ({lead.LeadCode})",
                userId: user.Id,
                ipAddress: ip);

            if (opp != null)
            {
                await _auditService.LogAsync(
                    action: "Create",
                    entityName: "Opportunity",
                    recordId: opp.OpportunityId.ToString(),
                    oldValue: null,
                    newValue: $"Created via conversion from Lead #{lead.LeadId}. Amount: {opp.Amount:C}",
                    userId: user.Id,
                    ipAddress: ip);
            }

            await transaction.CommitAsync();

            TempData["SuccessMessage"] = $"Lead '{lead.LeadName}' successfully converted to Customer '{customer.CustomerName}'" + (opp != null ? " and Opportunity created." : ".");
            return RedirectToAction("Details", "Customers", new { id = customer.CustomerId });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Failed to convert lead #{LeadId}", lead.LeadId);
            ModelState.AddModelError(string.Empty, "An error occurred while converting the lead: " + ex.Message);
            return View(model);
        }
    }

    // GET: /Leads/Delete/5
    public async Task<IActionResult> Delete(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var lead = await _context.Leads.FirstOrDefaultAsync(l => l.LeadId == id && !l.IsDeleted);
        if (lead == null)
        {
            return NotFound();
        }

        if (!await _scopedAccess.CanAccessRecordAsync(user, lead.AssignedTo))
        {
            return Forbid();
        }

        return View(lead);
    }

    // POST: /Leads/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var lead = await _context.Leads.FirstOrDefaultAsync(l => l.LeadId == id && !l.IsDeleted);
        if (lead == null)
        {
            return NotFound();
        }

        if (!await _scopedAccess.CanAccessRecordAsync(user, lead.AssignedTo))
        {
            return Forbid();
        }

        var oldStatus = $"Status: {lead.Status}, IsDeleted: {lead.IsDeleted}";

        lead.IsDeleted = true;
        lead.Status = "Lost";
        await _context.SaveChangesAsync();

        await _auditService.LogAsync(
            action: "Delete",
            entityName: "Lead",
            recordId: lead.LeadId.ToString(),
            oldValue: oldStatus,
            newValue: "Status: Lost, IsDeleted: True",
            userId: user.Id,
            ipAddress: GetIpAddress());

        TempData["SuccessMessage"] = $"Lead '{lead.LeadName}' deactivated/deleted successfully.";
        return RedirectToAction(nameof(Index));
    }
}
