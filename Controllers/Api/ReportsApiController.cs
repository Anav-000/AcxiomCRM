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
[Route("api/reports")]
[Authorize]
public class ReportsApiController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IScopedAccessService _scopedAccess;

    public ReportsApiController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IScopedAccessService scopedAccess)
    {
        _context = context;
        _userManager = userManager;
        _scopedAccess = scopedAccess;
    }

    // GET: /api/reports/pipeline
    [HttpGet("pipeline")]
    public async Task<IActionResult> GetPipelineReport()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var query = _context.Opportunities.Where(o => !o.IsDeleted).AsQueryable();
        query = await _scopedAccess.ApplyScopeAsync(query, user, o => o.AssignedTo);

        var opps = await query.ToListAsync();

        var openOpps = opps.Where(o => o.Status == "Open").ToList();
        var wonOpps = opps.Where(o => o.Status == "Won").ToList();

        var stageGroups = opps.GroupBy(o => o.Stage).ToDictionary(g => g.Key, g => g.Sum(o => o.Amount));
        var countGroups = opps.GroupBy(o => o.Stage).ToDictionary(g => g.Key, g => g.Count());
        var ownerGroups = openOpps.GroupBy(o => o.AssignedTo ?? "Unassigned").ToDictionary(g => g.Key, g => g.Sum(o => o.Amount));

        var report = new PipelineReportDto
        {
            TotalOpenPipelineAmount = openOpps.Sum(o => o.Amount),
            TotalWeightedPipelineAmount = openOpps.Sum(o => o.WeightedValue),
            TotalWonAmount = wonOpps.Sum(o => o.Amount),
            AmountByStage = stageGroups,
            CountByStage = countGroups,
            AmountByOwner = ownerGroups
        };

        return Ok(report);
    }
}
