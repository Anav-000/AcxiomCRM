using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using AcxiomCRM.DTOs;
using AcxiomCRM.Models;
using AcxiomCRM.Services;

namespace AcxiomCRM.Controllers.Api;

[ApiController]
[Route("api/auth")]
public class AuthApiController : ControllerBase
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAntiforgery _antiforgery;
    private readonly IAuditService _auditService;

    public AuthApiController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IAntiforgery antiforgery,
        IAuditService auditService)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _antiforgery = antiforgery;
        _auditService = auditService;
    }

    [HttpGet("csrf")]
    [AllowAnonymous]
    public IActionResult GetCsrfToken()
    {
        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
        Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false,
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps
        });
        return Ok(new
        {
            token = tokens.RequestToken,
            headerName = "X-XSRF-TOKEN"
        });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto model)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user == null || !user.IsActive)
        {
            await _auditService.LogAsync("FailedLogin", "Account", model.Email, "User not found or inactive");
            return Unauthorized(new { message = "Invalid email or password." });
        }

        var result = await _signInManager.PasswordSignInAsync(user, model.Password, isPersistent: true, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            var roles = await _userManager.GetRolesAsync(user);
            var primaryRole = roles.FirstOrDefault() ?? "SalesExecutive";

            await _auditService.LogAsync("Login", "Account", user.Id, $"Successful login by {user.Email} ({primaryRole})");

            var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
            Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
            {
                HttpOnly = false,
                SameSite = SameSiteMode.Lax,
                Secure = Request.IsHttps
            });

            return Ok(new LoginResponseDto
            {
                Email = user.Email ?? string.Empty,
                Name = user.Name,
                Role = primaryRole,
                Message = "Authentication successful"
            });
        }

        if (result.IsLockedOut)
        {
            await _auditService.LogAsync("Lockout", "Account", user.Id, $"Account locked out due to failed attempts: {user.Email}");
            return StatusCode(423, new { message = "Account locked due to multiple failed login attempts. Contact an administrator." });
        }

        await _auditService.LogAsync("FailedLogin", "Account", user.Id, $"Failed login attempt for {user.Email}");
        return Unauthorized(new { message = "Invalid email or password." });
    }

    [HttpPost("logout")]
    [Authorize]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Logout()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user != null)
        {
            await _auditService.LogAsync("Logout", "Account", user.Id, $"User {user.Email} logged out");
        }
        await _signInManager.SignOutAsync();
        return Ok(new { message = "Logout successful" });
    }
}
