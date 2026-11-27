using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AcxiomCRM.Data;
using AcxiomCRM.DTOs;
using AcxiomCRM.Models;
using AcxiomCRM.Services;

namespace AcxiomCRM.Controllers.Api;

[ApiController]
[Route("api/opportunities")]
[Authorize]
public class OpportunitiesApiController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IScopedAccessService _scopedAccess;
    private readonly IAuditService _auditService;

    public OpportunitiesApiController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IScopedAccessService scopedAccess,
        IAuditService auditService)
    {
        _context = context;
        _userManager = userManager;
        _scopedAccess = scopedAccess;
        _auditService = auditService;
    }

    private string GetIpAddress() =>
        HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";

    public static string DeriveStatus(string stage) => stage switch
    {
        "Won" => "Won",
        "Lost" => "Lost",
        _ => "Open"
    };

    // GET: /api/opportunities
    [HttpGet]
    public async Task<IActionResult> GetOpportunities([FromQuery] string? search)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var query = _context.Opportunities
            .Include(o => o.Customer)
            .Where(o => !o.IsDeleted)
            .AsQueryable();

        query = await _scopedAccess.ApplyScopeAsync(query, user, o => o.AssignedTo);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(o => o.OpportunityName.Contains(s) || (o.Customer != null && o.Customer.CustomerName.Contains(s)));
        }

        var list = await query
            .OrderByDescending(o => o.CreatedDate)
            .Select(o => new OpportunityDto
            {
                OpportunityId = o.OpportunityId,
                OpportunityName = o.OpportunityName,
                CustomerId = o.CustomerId,
                CustomerName = o.Customer != null ? o.Customer.CustomerName : null,
                LeadId = o.LeadId,
                Amount = o.Amount,
                Stage = o.Stage,
                Probability = o.Probability,
                WeightedValue = o.WeightedValue,
                ExpectedCloseDate = o.ExpectedCloseDate,
                Status = o.Status,
                AssignedTo = o.AssignedTo,
                CreatedDate = o.CreatedDate
            })
            .ToListAsync();

        return Ok(list);
    }

    // POST: /api/opportunities
    [HttpPost]
    public async Task<IActionResult> CreateOpportunity([FromBody] CreateOpportunityDto dto)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        if (dto.Amount <= 0)
        {
            return BadRequest(new { message = "Opportunity Amount must be greater than 0." });
        }

        if (dto.Probability < 0 || dto.Probability > 100)
        {
            return BadRequest(new { message = "Probability must be between 0 and 100." });
        }

        var derivedStatus = DeriveStatus(dto.Stage);
        if (derivedStatus == "Open" && dto.ExpectedCloseDate.Date < DateTime.Today)
        {
            return BadRequest(new { message = "Expected Close Date cannot be in the past for active opportunities." });
        }

        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.CustomerId == dto.CustomerId && !c.IsDeleted);
        if (customer == null || !await _scopedAccess.CanAccessRecordAsync(user, customer.AssignedTo))
        {
            return BadRequest(new { message = "Selected customer is invalid or unauthorized." });
        }

        var canReassign = await _scopedAccess.CanReassignRecordAsync(user);
        var assignedTo = (canReassign && !string.IsNullOrWhiteSpace(dto.AssignedTo))
            ? dto.AssignedTo
            : user.Email;

        var opp = new Opportunity
        {
            OpportunityName = dto.OpportunityName.Trim(),
            CustomerId = dto.CustomerId,
            LeadId = dto.LeadId,
            Amount = dto.Amount,
            Stage = dto.Stage,
            Probability = dto.Probability,
            ExpectedCloseDate = dto.ExpectedCloseDate,
            Status = derivedStatus,
            AssignedTo = assignedTo,
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
            newValue: $"Opportunity '{opp.OpportunityName}' created via REST API. Amount: {opp.Amount:C}",
            userId: user.Id,
            ipAddress: GetIpAddress());

        var responseDto = new OpportunityDto
        {
            OpportunityId = opp.OpportunityId,
            OpportunityName = opp.OpportunityName,
            CustomerId = opp.CustomerId,
            CustomerName = customer.CustomerName,
            LeadId = opp.LeadId,
            Amount = opp.Amount,
            Stage = opp.Stage,
            Probability = opp.Probability,
            WeightedValue = opp.WeightedValue,
            ExpectedCloseDate = opp.ExpectedCloseDate,
            Status = opp.Status,
            AssignedTo = opp.AssignedTo,
            CreatedDate = opp.CreatedDate
        };

        return CreatedAtAction(nameof(GetOpportunities), new { id = opp.OpportunityId }, responseDto);
    }
}
