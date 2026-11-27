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
public class OpportunitiesController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IScopedAccessService _scopedAccess;
    private readonly IAuditService _auditService;
    private readonly ILogger<OpportunitiesController> _logger;

    public static readonly string[] ValidStages =
    [
        "Qualification", "Proposal", "Negotiation", "Won", "Lost"
    ];

    public OpportunitiesController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IScopedAccessService scopedAccess,
        IAuditService auditService,
        ILogger<OpportunitiesController> logger)
    {
        _context = context;
        _userManager = userManager;
        _scopedAccess = scopedAccess;
        _auditService = auditService;
        _logger = logger;
    }

    private string GetIpAddress() =>
        HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";

    public static string DeriveStatusFromStage(string stage) => stage switch
    {
        "Won" => "Won",
        "Lost" => "Lost",
        _ => "Open"
    };

    // GET: /Opportunities
    public async Task<IActionResult> Index(string? search, string? stage, string? status)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var query = _context.Opportunities
            .Include(o => o.Customer)
            .Include(o => o.Lead)
            .Where(o => !o.IsDeleted)
            .AsQueryable();

        query = await _scopedAccess.ApplyScopeAsync(query, user, o => o.AssignedTo);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(o => o.OpportunityName.Contains(s) || (o.Customer != null && o.Customer.CustomerName.Contains(s)));
        }

        if (!string.IsNullOrWhiteSpace(stage))
        {
            query = query.Where(o => o.Stage == stage);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(o => o.Status == status);
        }

        var opportunities = await query
            .OrderByDescending(o => o.CreatedDate)
            .ToListAsync();

        var openOpps = opportunities.Where(o => o.Status == "Open").ToList();
        var wonOpps = opportunities.Where(o => o.Status == "Won").ToList();

        var model = new OpportunityListViewModel
        {
            Opportunities = opportunities,
            SearchTerm = search,
            StageFilter = stage,
            StatusFilter = status,
            TotalOpenAmount = openOpps.Sum(o => o.Amount),
            TotalWeightedAmount = openOpps.Sum(o => o.WeightedValue),
            TotalWonAmount = wonOpps.Sum(o => o.Amount),
            AvailableStages = ValidStages.Select(st => new SelectListItem(st, st, st == stage)).ToList(),
            AvailableStatuses = new List<SelectListItem>
            {
                new("Open", "Open", status == "Open"),
                new("Won", "Won", status == "Won"),
                new("Lost", "Lost", status == "Lost")
            }
        };

        return View(model);
    }

    // GET: /Opportunities/Details/5
    public async Task<IActionResult> Details(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var opp = await _context.Opportunities
            .Include(o => o.Customer)
            .Include(o => o.Lead)
            .FirstOrDefaultAsync(o => o.OpportunityId == id && !o.IsDeleted);

        if (opp == null) return NotFound();

        if (!await _scopedAccess.CanAccessRecordAsync(user, opp.AssignedTo))
        {
            return Forbid();
        }

        var model = new OpportunityDetailsViewModel
        {
            Opportunity = opp,
            CanEdit = true,
            CanDelete = true
        };

        return View(model);
    }

    // GET: /Opportunities/Create
    public async Task<IActionResult> Create(int? customerId = null, int? leadId = null)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var canReassign = await _scopedAccess.CanReassignRecordAsync(user);
        var customers = await GetScopedCustomersAsync(user);
        var leads = await GetScopedLeadsAsync(user);
        var assignableUsers = await _scopedAccess.GetAssignableUsersAsync(user);

        var model = new OpportunityCreateEditViewModel
        {
            CustomerId = customerId ?? (customers.FirstOrDefault()?.CustomerId ?? 0),
            LeadId = leadId,
            Amount = 10000m,
            Stage = "Qualification",
            Probability = 50,
            ExpectedCloseDate = DateTime.Today.AddDays(30),
            AssignedTo = user.Email,
            CanReassign = canReassign,
            AvailableCustomers = customers.Select(c => new SelectListItem(c.CustomerName, c.CustomerId.ToString(), c.CustomerId == customerId)).ToList(),
            AvailableLeads = leads.Select(l => new SelectListItem(l.LeadName, l.LeadId.ToString(), l.LeadId == leadId)).ToList(),
            AssignableUsers = assignableUsers.Select(u => new SelectListItem($"{u.Name} ({u.Email})", u.Email, u.Email == user.Email)).ToList(),
            AvailableStages = ValidStages.Select(st => new SelectListItem(st, st, st == "Qualification")).ToList()
        };

        return View(model);
    }

    // POST: /Opportunities/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(OpportunityCreateEditViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var canReassign = await _scopedAccess.CanReassignRecordAsync(user);
        if (!canReassign || string.IsNullOrWhiteSpace(model.AssignedTo))
        {
            model.AssignedTo = user.Email;
        }

        // Business Validation
        if (model.Amount <= 0)
        {
            ModelState.AddModelError("Amount", "Opportunity Amount must be greater than 0.");
        }

        if (model.Probability < 0 || model.Probability > 100)
        {
            ModelState.AddModelError("Probability", "Probability must be between 0 and 100.");
        }

        var derivedStatus = DeriveStatusFromStage(model.Stage);
        if (derivedStatus == "Open" && model.ExpectedCloseDate.Date < DateTime.Today)
        {
            ModelState.AddModelError("ExpectedCloseDate", "Expected Close Date cannot be in the past for active opportunities.");
        }

        // Verify authorized Customer selection
        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.CustomerId == model.CustomerId && !c.IsDeleted);
        if (customer == null || !await _scopedAccess.CanAccessRecordAsync(user, customer.AssignedTo))
        {
            ModelState.AddModelError("CustomerId", "Invalid or unauthorized customer selected.");
        }

        if (!ModelState.IsValid)
        {
            await PopulateDropDownsAsync(model, user, canReassign);
            return View(model);
        }

        var opp = new Opportunity
        {
            OpportunityName = model.OpportunityName.Trim(),
            CustomerId = model.CustomerId,
            LeadId = model.LeadId,
            Amount = model.Amount,
            Stage = model.Stage,
            Probability = model.Probability,
            ExpectedCloseDate = model.ExpectedCloseDate,
            Status = derivedStatus,
            AssignedTo = model.AssignedTo,
            CreatedDate = DateTime.UtcNow,
            IsDeleted = false
        };

        _context.Opportunities.Add(opp);
        await _context.SaveChangesAsync();

        await _auditService.LogAsync(
            action: "Create",
            entityName: "Opportunity",
            recordId: opp.OpportunityId.ToString(),
            oldValue: null,
            newValue: $"Opportunity '{opp.OpportunityName}' created. Amount: {opp.Amount:C}, Stage: {opp.Stage}, Status: {opp.Status}, AssignedTo: {opp.AssignedTo}",
            userId: user.Id,
            ipAddress: GetIpAddress());

        TempData["SuccessMessage"] = $"Opportunity '{opp.OpportunityName}' created successfully.";
        return RedirectToAction(nameof(Index));
    }

    // GET: /Opportunities/Edit/5
    public async Task<IActionResult> Edit(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var opp = await _context.Opportunities.FirstOrDefaultAsync(o => o.OpportunityId == id && !o.IsDeleted);
        if (opp == null) return NotFound();

        if (!await _scopedAccess.CanAccessRecordAsync(user, opp.AssignedTo))
        {
            return Forbid();
        }

        var canReassign = await _scopedAccess.CanReassignRecordAsync(user);
        var model = new OpportunityCreateEditViewModel
        {
            OpportunityId = opp.OpportunityId,
            OpportunityName = opp.OpportunityName,
            CustomerId = opp.CustomerId,
            LeadId = opp.LeadId,
            Amount = opp.Amount,
            Stage = opp.Stage,
            Probability = opp.Probability,
            ExpectedCloseDate = opp.ExpectedCloseDate,
            AssignedTo = opp.AssignedTo,
            CanReassign = canReassign
        };

        await PopulateDropDownsAsync(model, user, canReassign);
        return View(model);
    }

    // POST: /Opportunities/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, OpportunityCreateEditViewModel model)
    {
        if (id != model.OpportunityId) return NotFound();

        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var opp = await _context.Opportunities.FirstOrDefaultAsync(o => o.OpportunityId == id && !o.IsDeleted);
        if (opp == null) return NotFound();

        if (!await _scopedAccess.CanAccessRecordAsync(user, opp.AssignedTo))
        {
            return Forbid();
        }

        var canReassign = await _scopedAccess.CanReassignRecordAsync(user);
        if (!canReassign)
        {
            model.AssignedTo = opp.AssignedTo;
        }

        // Business Validation
        if (model.Amount <= 0)
        {
            ModelState.AddModelError("Amount", "Opportunity Amount must be greater than 0.");
        }

        if (model.Probability < 0 || model.Probability > 100)
        {
            ModelState.AddModelError("Probability", "Probability must be between 0 and 100.");
        }

        var derivedStatus = DeriveStatusFromStage(model.Stage);
        if (derivedStatus == "Open" && model.ExpectedCloseDate.Date < DateTime.Today)
        {
            ModelState.AddModelError("ExpectedCloseDate", "Expected Close Date cannot be in the past for active opportunities.");
        }

        if (!ModelState.IsValid)
        {
            await PopulateDropDownsAsync(model, user, canReassign);
            return View(model);
        }

        var oldStage = opp.Stage;
        var oldSummary = $"Stage: {opp.Stage}, Status: {opp.Status}, Amount: {opp.Amount}, AssignedTo: {opp.AssignedTo}";

        opp.OpportunityName = model.OpportunityName.Trim();
        opp.CustomerId = model.CustomerId;
        opp.LeadId = model.LeadId;
        opp.Amount = model.Amount;
        opp.Stage = model.Stage;
        opp.Probability = model.Probability;
        opp.ExpectedCloseDate = model.ExpectedCloseDate;
        opp.Status = derivedStatus;
        opp.AssignedTo = model.AssignedTo;

        await _context.SaveChangesAsync();

        var newSummary = $"Stage: {opp.Stage}, Status: {opp.Status}, Amount: {opp.Amount}, AssignedTo: {opp.AssignedTo}";

        // Audit outcome change if Stage moved to Won/Lost or changed
        var action = (oldStage != opp.Stage && (opp.Stage == "Won" || opp.Stage == "Lost"))
            ? "OutcomeChange"
            : "Update";

        await _auditService.LogAsync(
            action: action,
            entityName: "Opportunity",
            recordId: opp.OpportunityId.ToString(),
            oldValue: oldSummary,
            newValue: newSummary,
            userId: user.Id,
            ipAddress: GetIpAddress());

        TempData["SuccessMessage"] = $"Opportunity '{opp.OpportunityName}' updated successfully.";
        return RedirectToAction(nameof(Index));
    }

    // GET: /Opportunities/Delete/5
    public async Task<IActionResult> Delete(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var opp = await _context.Opportunities
            .Include(o => o.Customer)
            .FirstOrDefaultAsync(o => o.OpportunityId == id && !o.IsDeleted);

        if (opp == null) return NotFound();

        if (!await _scopedAccess.CanAccessRecordAsync(user, opp.AssignedTo))
        {
            return Forbid();
        }

        return View(opp);
    }

    // POST: /Opportunities/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var opp = await _context.Opportunities.FirstOrDefaultAsync(o => o.OpportunityId == id && !o.IsDeleted);
        if (opp == null) return NotFound();

        if (!await _scopedAccess.CanAccessRecordAsync(user, opp.AssignedTo))
        {
            return Forbid();
        }

        opp.IsDeleted = true;
        opp.Status = "Lost";
        await _context.SaveChangesAsync();

        await _auditService.LogAsync(
            action: "Delete",
            entityName: "Opportunity",
            recordId: opp.OpportunityId.ToString(),
            oldValue: "Active",
            newValue: "Status: Lost, IsDeleted: True",
            userId: user.Id,
            ipAddress: GetIpAddress());

        TempData["SuccessMessage"] = $"Opportunity '{opp.OpportunityName}' deleted/deactivated.";
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateDropDownsAsync(OpportunityCreateEditViewModel model, ApplicationUser user, bool canReassign)
    {
        var customers = await GetScopedCustomersAsync(user);
        var leads = await GetScopedLeadsAsync(user);
        var assignableUsers = await _scopedAccess.GetAssignableUsersAsync(user);

        model.CanReassign = canReassign;
        model.AvailableCustomers = customers.Select(c => new SelectListItem(c.CustomerName, c.CustomerId.ToString(), c.CustomerId == model.CustomerId)).ToList();
        model.AvailableLeads = leads.Select(l => new SelectListItem(l.LeadName, l.LeadId.ToString(), l.LeadId == model.LeadId)).ToList();
        model.AssignableUsers = assignableUsers.Select(u => new SelectListItem($"{u.Name} ({u.Email})", u.Email, u.Email == model.AssignedTo)).ToList();
        model.AvailableStages = ValidStages.Select(st => new SelectListItem(st, st, st == model.Stage)).ToList();
    }

    private async Task<List<Customer>> GetScopedCustomersAsync(ApplicationUser user)
    {
        var query = _context.Customers.Where(c => !c.IsDeleted).AsQueryable();
        query = await _scopedAccess.ApplyScopeAsync(query, user, c => c.AssignedTo);
        return await query.OrderBy(c => c.CustomerName).ToListAsync();
    }

    private async Task<List<Lead>> GetScopedLeadsAsync(ApplicationUser user)
    {
        var query = _context.Leads.Where(l => !l.IsDeleted).AsQueryable();
        query = await _scopedAccess.ApplyScopeAsync(query, user, l => l.AssignedTo);
        return await query.OrderBy(l => l.LeadName).ToListAsync();
    }
}
