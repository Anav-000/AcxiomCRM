using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using AcxiomCRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AcxiomCRM.Controllers;

public class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IAuditService _auditService;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IAuditService _auditService,
        ILogger<AccountController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        this._auditService = _auditService;
        _logger = logger;
    }

    private string GetIpAddress() =>
        HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";

    // GET: /Account/Register
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Dashboard");
        }
        return View();
    }

    // POST: /Account/Register
    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var existingUser = await _userManager.FindByEmailAsync(model.Email);
        if (existingUser != null)
        {
            ModelState.AddModelError(string.Empty, "An account with this email address already exists.");
            return View(model);
        }

        var user = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            Name = model.Name,
            IsActive = true,
            CreatedDate = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, model.Password);
        if (result.Succeeded)
        {
            // Public registration assigns SalesExecutive role
            await _userManager.AddToRoleAsync(user, DbInitializer.RoleSalesExecutive);

            var ip = GetIpAddress();
            await _auditService.LogAsync(
                action: "Register",
                entityName: "Authentication",
                recordId: user.Id,
                oldValue: null,
                newValue: $"Registered with role {DbInitializer.RoleSalesExecutive}",
                userId: user.Id,
                ipAddress: ip);

            await _signInManager.SignInAsync(user, isPersistent: false);

            await _auditService.LogAsync(
                action: "Login",
                entityName: "Authentication",
                recordId: user.Id,
                oldValue: null,
                newValue: "Successful registration & login",
                userId: user.Id,
                ipAddress: ip);

            return RedirectToAction("Index", "Dashboard");
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }

        return View(model);
    }

    // GET: /Account/Login
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Dashboard");
        }

        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    // POST: /Account/Login
    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _userManager.FindByEmailAsync(model.Email);
        var ip = GetIpAddress();

        // Check if user is active
        if (user != null && !user.IsActive)
        {
            await _auditService.LogAsync(
                action: "FailedLogin",
                entityName: "Authentication",
                recordId: user.Id,
                oldValue: null,
                newValue: "Account inactive",
                userId: user.Id,
                ipAddress: ip);

            ModelState.AddModelError(string.Empty, "This account is inactive. Please contact your administrator.");
            return View(model);
        }

        var result = await _signInManager.PasswordSignInAsync(
            userName: model.Email,
            password: model.Password,
            isPersistent: model.RememberMe,
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
            await _auditService.LogAsync(
                action: "Login",
                entityName: "Authentication",
                recordId: user?.Id,
                oldValue: null,
                newValue: "Successful login",
                userId: user?.Id,
                ipAddress: ip);

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction("Index", "Dashboard");
        }

        if (result.IsLockedOut)
        {
            await _auditService.LogAsync(
                action: "Lockout",
                entityName: "Authentication",
                recordId: user?.Id,
                oldValue: null,
                newValue: $"Account locked out for {model.Email}",
                userId: user?.Id,
                ipAddress: ip);

            ModelState.AddModelError(string.Empty, "This account has been locked out due to multiple failed login attempts. Please try again later.");
            return View(model);
        }

        // Invalid credentials or failed attempt
        await _auditService.LogAsync(
            action: "FailedLogin",
            entityName: "Authentication",
            recordId: user?.Id,
            oldValue: null,
            newValue: $"Failed login attempt for {model.Email}",
            userId: user?.Id,
            ipAddress: ip);

        ModelState.AddModelError(string.Empty, "Invalid login credentials. Please check your email and password.");
        return View(model);
    }

    // POST & GET: /Account/Logout
    [AcceptVerbs("GET", "POST")]
    [Authorize]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Logout()
    {
        var user = await _userManager.GetUserAsync(User);
        var ip = GetIpAddress();

        await _signInManager.SignOutAsync();

        await _auditService.LogAsync(
            action: "Logout",
            entityName: "Authentication",
            recordId: user?.Id,
            oldValue: null,
            newValue: "User logged out successfully",
            userId: user?.Id,
            ipAddress: ip);

        return RedirectToAction("Login", "Account");
    }

    // GET: /Account/AccessDenied
    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied()
    {
        return View();
    }
}
