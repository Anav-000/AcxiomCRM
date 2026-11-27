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
[Route("api/leads")]
[Authorize]
public class LeadsApiController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IScopedAccessService _scopedAccess;
    private readonly IAuditService _auditService;

    public LeadsApiController(
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

    // GET: /api/leads
    [HttpGet]
    public async Task<IActionResult> GetLeads([FromQuery] string? search)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var query = _context.Leads.Where(l => !l.IsDeleted).AsQueryable();
        query = await _scopedAccess.ApplyScopeAsync(query, user, l => l.AssignedTo);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(l => l.LeadName.Contains(s) || l.Email.Contains(s) || l.Phone.Contains(s) || (l.CompanyName != null && l.CompanyName.Contains(s)));
        }

        var list = await query
            .OrderByDescending(l => l.CreatedDate)
            .Select(l => new LeadDto
            {
                LeadId = l.LeadId,
                LeadCode = l.LeadCode,
                LeadName = l.LeadName,
                Email = l.Email,
                Phone = l.Phone,
                CompanyName = l.CompanyName,
                Source = l.Source,
                Status = l.Status,
                ExpectedValue = l.ExpectedValue,
                AssignedTo = l.AssignedTo,
                CreatedDate = l.CreatedDate,
                ConvertedCustomerId = l.ConvertedCustomerId
            })
            .ToListAsync();

        return Ok(list);
    }

    // POST: /api/leads
    [HttpPost]
    public async Task<IActionResult> CreateLead([FromBody] CreateLeadDto dto)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        if (dto.ExpectedValue <= 0)
        {
            return BadRequest(new { message = "Expected Value must be greater than 0." });
        }

        var canReassign = await _scopedAccess.CanReassignRecordAsync(user);
        var assignedTo = (canReassign && !string.IsNullOrWhiteSpace(dto.AssignedTo))
            ? dto.AssignedTo
            : user.Email;

        var leadCode = $"LEAD-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..4].ToUpper()}";

        var lead = new Lead
        {
            LeadCode = leadCode,
            LeadName = dto.LeadName.Trim(),
            Email = dto.Email.Trim(),
            Phone = dto.Phone.Trim(),
            CompanyName = dto.CompanyName?.Trim(),
            Source = dto.Source?.Trim(),
            Status = "New",
            ExpectedValue = dto.ExpectedValue,
            AssignedTo = assignedTo,
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
            newValue: $"Lead '{lead.LeadName}' created via REST API",
            userId: user.Id,
            ipAddress: GetIpAddress());

        var responseDto = new LeadDto
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
            CreatedDate = lead.CreatedDate
        };

        return CreatedAtAction(nameof(GetLeads), new { id = lead.LeadId }, responseDto);
    }
}
