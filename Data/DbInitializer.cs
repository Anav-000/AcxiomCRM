using AcxiomCRM.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Data;

public static class DbInitializer
{
    public const string RoleAdmin = "Admin";
    public const string RoleManager = "Manager";
    public const string RoleSalesExecutive = "SalesExecutive";

    public static async Task InitializeAsync(IServiceProvider serviceProvider, IConfiguration configuration, bool isDevelopment = true)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<ApplicationDbContext>>();

        // Apply migrations
        await context.Database.MigrateAsync();

        // 1. Seed Roles (Always required for system integrity)
        string[] roles = [RoleAdmin, RoleManager, RoleSalesExecutive];
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
                logger.LogInformation("Role created: {Role}", role);
            }
        }

        // Demo account seeding is Development-only (or when explicit environment variables are set)
        var envAdminEmail = Environment.GetEnvironmentVariable("ADMIN_EMAIL") ?? Environment.GetEnvironmentVariable("SEED_ADMIN_EMAIL");
        var envAdminPass = Environment.GetEnvironmentVariable("ADMIN_PASSWORD") ?? Environment.GetEnvironmentVariable("SEED_ADMIN_PASSWORD");

        if (!isDevelopment && string.IsNullOrEmpty(envAdminEmail) && string.IsNullOrEmpty(envAdminPass))
        {
            logger.LogInformation("Non-development environment: skipping demo accounts seeding.");
            return;
        }

        // 2. Read test accounts from Environment Variables or Development Defaults
        var adminEmail = envAdminEmail ?? "admin@acxiomcrm.local";
        var adminPassword = envAdminPass ?? "Admin@Password123!";

        var managerEmail = Environment.GetEnvironmentVariable("MANAGER_EMAIL")
                           ?? Environment.GetEnvironmentVariable("SEED_MANAGER_EMAIL")
                           ?? "manager@acxiomcrm.local";
        var managerPassword = Environment.GetEnvironmentVariable("MANAGER_PASSWORD")
                              ?? Environment.GetEnvironmentVariable("SEED_MANAGER_PASSWORD")
                              ?? "Manager@Password123!";

        var salesEmail = Environment.GetEnvironmentVariable("SALES_EMAIL")
                         ?? Environment.GetEnvironmentVariable("SEED_SALES_EMAIL")
                         ?? "sales@acxiomcrm.local";
        var salesPassword = Environment.GetEnvironmentVariable("SALES_PASSWORD")
                            ?? Environment.GetEnvironmentVariable("SEED_SALES_PASSWORD")
                            ?? "Sales@Password123!";

        var sales2Email = Environment.GetEnvironmentVariable("SALES2_EMAIL")
                          ?? Environment.GetEnvironmentVariable("SEED_SALES2_EMAIL")
                          ?? "sales2@acxiomcrm.local";
        var sales2Password = Environment.GetEnvironmentVariable("SALES2_PASSWORD")
                             ?? Environment.GetEnvironmentVariable("SEED_SALES2_PASSWORD")
                             ?? "Sales2@Password123!";

        var adminUser = await SeedUserAsync(userManager, logger, adminEmail, adminPassword, "System Administrator", RoleAdmin);
        var managerUser = await SeedUserAsync(userManager, logger, managerEmail, managerPassword, "Regional Manager", RoleManager);
        var salesUser = await SeedUserAsync(userManager, logger, salesEmail, salesPassword, "Primary Sales Specialist", RoleSalesExecutive);
        var sales2User = await SeedUserAsync(userManager, logger, sales2Email, sales2Password, "Secondary Sales Specialist", RoleSalesExecutive);

        // Assign sales1 to manager's team
        if (salesUser != null && managerUser != null && salesUser.ManagerId != managerUser.Id)
        {
            salesUser.ManagerId = managerUser.Id;
            await userManager.UpdateAsync(salesUser);
        }
    }

    private static async Task<ApplicationUser?> SeedUserAsync(
        UserManager<ApplicationUser> userManager,
        ILogger logger,
        string email,
        string password,
        string name,
        string role)
    {
        var existing = await userManager.FindByEmailAsync(email);
        if (existing == null)
        {
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                Name = name,
                IsActive = true,
                CreatedDate = DateTime.UtcNow
            };

            var createResult = await userManager.CreateAsync(user, password);
            if (createResult.Succeeded)
            {
                await userManager.AddToRoleAsync(user, role);
                logger.LogInformation("Seeded user {Email} with role {Role}", email, role);
                return user;
            }
            else
            {
                logger.LogWarning("Failed to seed user {Email}: {Errors}", email, string.Join(", ", createResult.Errors.Select(e => e.Description)));
                return null;
            }
        }
        else
        {
            if (!await userManager.IsInRoleAsync(existing, role))
            {
                await userManager.AddToRoleAsync(existing, role);
                logger.LogInformation("Assigned role {Role} to existing user {Email}", role, email);
            }
            return existing;
        }
    }
}
