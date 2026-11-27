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
[Route("api/customers")]
[Authorize]
public class CustomersApiController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IScopedAccessService _scopedAccess;
    private readonly IAuditService _auditService;

    public CustomersApiController(
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

    // GET: /api/customers
    [HttpGet]
    public async Task<IActionResult> GetCustomers([FromQuery] string? search)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var query = _context.Customers.Where(c => !c.IsDeleted).AsQueryable();
        query = await _scopedAccess.ApplyScopeAsync(query, user, c => c.AssignedTo);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(c => c.CustomerName.Contains(s) || c.Email.Contains(s) || c.Phone.Contains(s) || (c.CompanyName != null && c.CompanyName.Contains(s)));
        }

        var list = await query
            .OrderByDescending(c => c.CreatedDate)
            .Select(c => new CustomerDto
            {
                CustomerId = c.CustomerId,
                CustomerCode = c.CustomerCode,
                CustomerName = c.CustomerName,
                Email = c.Email,
                Phone = c.Phone,
                CompanyName = c.CompanyName,
                Address = c.Address,
                City = c.City,
                State = c.State,
                Status = c.Status,
                AssignedTo = c.AssignedTo,
                CreatedBy = c.CreatedBy,
                CreatedDate = c.CreatedDate
            })
            .ToListAsync();

        return Ok(list);
    }

    // GET: /api/customers/5
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetCustomer(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var customer = await _context.Customers
            .FirstOrDefaultAsync(c => c.CustomerId == id && !c.IsDeleted);

        if (customer == null) return NotFound(new { message = $"Customer with ID {id} not found." });

        if (!await _scopedAccess.CanAccessRecordAsync(user, customer.AssignedTo))
        {
            return Forbid();
        }

        var dto = new CustomerDto
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
            CreatedBy = customer.CreatedBy,
            CreatedDate = customer.CreatedDate
        };

        return Ok(dto);
    }

    // POST: /api/customers
    [HttpPost]
    public async Task<IActionResult> CreateCustomer([FromBody] CreateCustomerDto dto)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var normEmail = dto.Email.Trim().ToUpperInvariant();
        var normPhone = new string(dto.Phone.Where(char.IsDigit).ToArray());

        // Validate uniqueness independently (409 Conflict)
        if (await _context.Customers.AnyAsync(c => !c.IsDeleted && c.NormalizedEmail == normEmail))
        {
            return Conflict(new { message = "A customer with this email address already exists." });
        }

        if (await _context.Customers.AnyAsync(c => !c.IsDeleted && c.NormalizedPhone == normPhone))
        {
            return Conflict(new { message = "A customer with this phone number already exists." });
        }

        var canReassign = await _scopedAccess.CanReassignRecordAsync(user);
        var assignedTo = (canReassign && !string.IsNullOrWhiteSpace(dto.AssignedTo))
            ? dto.AssignedTo
            : user.Email;

        var customerCode = $"CUST-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..4].ToUpper()}";

        var customer = new Customer
        {
            CustomerCode = customerCode,
            CustomerName = dto.CustomerName.Trim(),
            Email = dto.Email.Trim(),
            NormalizedEmail = normEmail,
            Phone = dto.Phone.Trim(),
            NormalizedPhone = normPhone,
            CompanyName = dto.CompanyName?.Trim(),
            Address = dto.Address?.Trim(),
            City = dto.City?.Trim(),
            State = dto.State?.Trim(),
            Status = "Active",
            AssignedTo = assignedTo,
            CreatedBy = user.Email ?? user.UserName ?? user.Id,
            CreatedDate = DateTime.UtcNow,
            IsDeleted = false
        };

        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        await _auditService.LogAsync(
            action: "Create",
            entityName: "Customer",
            recordId: customer.CustomerId.ToString(),
            oldValue: null,
            newValue: $"Customer '{customer.CustomerName}' ({customer.CustomerCode}) created via REST API",
            userId: user.Id,
            ipAddress: GetIpAddress());

        var responseDto = new CustomerDto
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
            CreatedBy = customer.CreatedBy,
            CreatedDate = customer.CreatedDate
        };

        return CreatedAtAction(nameof(GetCustomer), new { id = customer.CustomerId }, responseDto);
    }

    // PUT: /api/customers/5
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateCustomer(int id, [FromBody] UpdateCustomerDto dto)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.CustomerId == id && !c.IsDeleted);
        if (customer == null) return NotFound(new { message = $"Customer with ID {id} not found." });

        if (!await _scopedAccess.CanAccessRecordAsync(user, customer.AssignedTo))
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var normEmail = dto.Email.Trim().ToUpperInvariant();
        var normPhone = new string(dto.Phone.Where(char.IsDigit).ToArray());

        // Check duplicate email against other records
        if (await _context.Customers.AnyAsync(c => c.CustomerId != id && !c.IsDeleted && c.NormalizedEmail == normEmail))
        {
            return Conflict(new { message = "A customer with this email address already exists." });
        }

        // Check duplicate phone against other records
        if (await _context.Customers.AnyAsync(c => c.CustomerId != id && !c.IsDeleted && c.NormalizedPhone == normPhone))
        {
            return Conflict(new { message = "A customer with this phone number already exists." });
        }

        var oldInfo = $"Name: {customer.CustomerName}, Email: {customer.Email}, Phone: {customer.Phone}, Status: {customer.Status}";

        customer.CustomerName = dto.CustomerName.Trim();
        customer.Email = dto.Email.Trim();
        customer.NormalizedEmail = normEmail;
        customer.Phone = dto.Phone.Trim();
        customer.NormalizedPhone = normPhone;
        customer.CompanyName = dto.CompanyName?.Trim();
        customer.Address = dto.Address?.Trim();
        customer.City = dto.City?.Trim();
        customer.State = dto.State?.Trim();
        customer.Status = dto.Status;

        if (await _scopedAccess.CanReassignRecordAsync(user) && !string.IsNullOrWhiteSpace(dto.AssignedTo))
        {
            customer.AssignedTo = dto.AssignedTo;
        }

        await _context.SaveChangesAsync();

        await _auditService.LogAsync(
            action: "Update",
            entityName: "Customer",
            recordId: customer.CustomerId.ToString(),
            oldValue: oldInfo,
            newValue: $"Updated via REST API. Name: {customer.CustomerName}, Status: {customer.Status}",
            userId: user.Id,
            ipAddress: GetIpAddress());

        var responseDto = new CustomerDto
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
            CreatedBy = customer.CreatedBy,
            CreatedDate = customer.CreatedDate
        };

        return Ok(responseDto);
    }

    // DELETE: /api/customers/5
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteCustomer(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.CustomerId == id && !c.IsDeleted);
        if (customer == null) return NotFound(new { message = $"Customer with ID {id} not found." });

        if (!await _scopedAccess.CanAccessRecordAsync(user, customer.AssignedTo))
        {
            return Forbid();
        }

        customer.IsDeleted = true;
        customer.Status = "Inactive";
        await _context.SaveChangesAsync();

        await _auditService.LogAsync(
            action: "Delete",
            entityName: "Customer",
            recordId: customer.CustomerId.ToString(),
            oldValue: $"CustomerName: {customer.CustomerName}",
            newValue: "Soft deleted via REST API",
            userId: user.Id,
            ipAddress: GetIpAddress());

        return Ok(new { message = $"Customer {id} successfully deleted/deactivated." });
    }
}
