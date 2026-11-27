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
[Route("api/followups")]
[Authorize]
public class FollowUpsApiController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditService _auditService;

    public FollowUpsApiController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IAuditService auditService)
    {
        _context = context;
        _userManager = userManager;
        _auditService = auditService;
    }

    // GET: /api/followups
    [HttpGet]
    public async Task<IActionResult> GetFollowUps()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
        var isManager = await _userManager.IsInRoleAsync(user, "Manager");

        var query = _context.FollowUps.Where(f => !f.IsDeleted).AsQueryable();

        if (!isAdmin)
        {
            if (isManager)
            {
                var reports = await _context.Users.Where(u => u.ManagerId == user.Id).Select(u => u.Id).ToListAsync();
                reports.Add(user.Id);
                query = query.Where(f => f.AssignedTo != null && reports.Contains(f.AssignedTo));
            }
            else
            {
                query = query.Where(f => f.AssignedTo == user.Id);
            }
        }

        var list = await query
            .OrderBy(f => f.FollowUpDate)
            .Select(f => new FollowUpDto
            {
                FollowUpId = f.FollowUpId,
                CustomerId = f.CustomerId,
                LeadId = f.LeadId,
                OpportunityId = f.OpportunityId,
                FollowUpDate = f.FollowUpDate,
                FollowUpType = f.FollowUpType,
                Remarks = f.Remarks,
                Status = f.Status,
                AssignedTo = f.AssignedTo
            })
            .ToListAsync();

        return Ok(list);
    }

    // POST: /api/followups
    [HttpPost]
    public async Task<IActionResult> CreateFollowUp([FromBody] CreateFollowUpDto dto)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        if (dto.FollowUpDate.Date < DateTime.Today)
        {
            return BadRequest(new { message = "Follow-up date cannot be in the past for new follow-ups." });
        }

        var followUp = new FollowUp
        {
            CustomerId = dto.CustomerId,
            LeadId = dto.LeadId,
            OpportunityId = dto.OpportunityId,
            FollowUpDate = dto.FollowUpDate,
            FollowUpType = dto.FollowUpType,
            Remarks = dto.Remarks,
            Status = dto.Status,
            AssignedTo = string.IsNullOrWhiteSpace(dto.AssignedTo) ? user.Id : dto.AssignedTo,
            IsDeleted = false
        };

        _context.FollowUps.Add(followUp);
        await _context.SaveChangesAsync();

        await _auditService.LogAsync(
            action: "Create",
            entityName: "FollowUp",
            recordId: followUp.FollowUpId.ToString(),
            oldValue: null,
            newValue: $"FollowUp {followUp.FollowUpId} scheduled via REST API for {followUp.FollowUpDate:yyyy-MM-dd}",
            userId: user.Id,
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1");

        var responseDto = new FollowUpDto
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

        return CreatedAtAction(nameof(GetFollowUps), new { id = followUp.FollowUpId }, responseDto);
    }
}
