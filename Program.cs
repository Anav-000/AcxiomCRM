using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Port binding fallback for containerized environments (Render, Cloud Run, etc.)
var portEnv = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(portEnv) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{portEnv}");
}

// 1. Configure EF Core with SQLite
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Data Source=AcxiomCRM.db";

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));

// 2. Configure Data Protection to persist inside workspace / container
var keysFolder = Path.Combine(builder.Environment.ContentRootPath, ".keys");
if (!Directory.Exists(keysFolder))
{
    Directory.CreateDirectory(keysFolder);
}

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysFolder))
    .SetApplicationName("AcxiomCRM");

// Configure Forwarded Headers for reverse proxy (Render, AWS, etc.)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// 3. Configure ASP.NET Core Identity with mandatory password policy & lockout
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    // Password policy
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequireUppercase = true;
    options.Password.RequiredLength = 8;
    options.Password.RequiredUniqueChars = 1;

    // Lockout policy
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;

    // User policy
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// 4. Configure Cookie Settings with reverse proxy compatibility
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.SlidingExpiration = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

// 5. Register Services & Anti-forgery filter
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IScopedAccessService, ScopedAccessService>();

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});

var app = builder.Build();

// Enable Forwarded Headers first before any redirect or auth middleware
app.UseForwardedHeaders();

// 6. Initialize Database, migrations & seed roles/accounts
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    try
    {
        await DbInitializer.InitializeAsync(services, app.Configuration, app.Environment.IsDevelopment());
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while migrating and seeding the database.");
    }
}

// 7. Handle CLI verification flag
if (args.Contains("--verify-phase1"))
{
    var passed = await Phase1Verifier.RunAsync(app.Services);
    Environment.Exit(passed ? 0 : 1);
    return;
}

if (args.Contains("--verify-phase2"))
{
    var passed = await Phase2Verifier.RunAsync(app.Services);
    Environment.Exit(passed ? 0 : 1);
    return;
}

if (args.Contains("--verify-phase3"))
{
    var passed = await Phase3Verifier.RunAsync(app.Services);
    Environment.Exit(passed ? 0 : 1);
    return;
}

if (args.Contains("--verify-phase4"))
{
    var passed = await Phase4Verifier.RunAsync(app.Services);
    Environment.Exit(passed ? 0 : 1);
    return;
}

// 8. Configure HTTP pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
