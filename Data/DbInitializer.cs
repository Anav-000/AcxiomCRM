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

        // Check if demo accounts should be seeded
        var envAdminEmail = Environment.GetEnvironmentVariable("ADMIN_EMAIL") ?? Environment.GetEnvironmentVariable("SEED_ADMIN_EMAIL");
        var envAdminPass = Environment.GetEnvironmentVariable("ADMIN_PASSWORD") ?? Environment.GetEnvironmentVariable("SEED_ADMIN_PASSWORD");
        var seedDemoEnv = string.Equals(Environment.GetEnvironmentVariable("SEED_DEMO_DATA"), "true", StringComparison.OrdinalIgnoreCase);

        var shouldSeedUsers = isDevelopment || seedDemoEnv || !string.IsNullOrEmpty(envAdminEmail) || !string.IsNullOrEmpty(envAdminPass);

        if (!shouldSeedUsers)
        {
            logger.LogInformation("Non-development environment and no bootstrap credentials provided: skipping demo accounts seeding.");
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

        // 3. Seed Synthetic Demo Data for Hosted Instance (if database has no customer records)
        if (seedDemoEnv || !await context.Customers.AnyAsync())
        {
            await SeedSyntheticDataAsync(context, salesUser?.Id, sales2User?.Id, adminUser?.Id, logger);
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

    private static async Task SeedSyntheticDataAsync(
        ApplicationDbContext context,
        string? sales1Id,
        string? sales2Id,
        string? adminId,
        ILogger logger)
    {
        if (await context.Customers.AnyAsync())
        {
            return;
        }

        logger.LogInformation("Seeding synthetic demo data for hosted CRM instance...");

        var s1 = sales1Id ?? adminId;
        var s2 = sales2Id ?? s1;
        var creator = adminId ?? s1;

        // 1. Synthetic Customers
        var c1 = new Customer
        {
            CustomerCode = "CUST-DEMO-001",
            CustomerName = "Acme Global Technologies",
            Email = "contact@acmeglobal.demo",
            NormalizedEmail = "CONTACT@ACMEGLOBAL.DEMO",
            Phone = "+1-555-0101",
            NormalizedPhone = "15550101",
            CompanyName = "Acme Global Technologies",
            Address = "100 Innovation Parkway, Suite 400",
            City = "San Jose",
            State = "CA",
            Status = "Active",
            CreatedDate = DateTime.UtcNow.AddDays(-30),
            CreatedBy = creator,
            AssignedTo = s1
        };

        var c2 = new Customer
        {
            CustomerCode = "CUST-DEMO-002",
            CustomerName = "Apex Retail Enterprises",
            Email = "procure@apexretail.demo",
            NormalizedEmail = "PROCURE@APEXRETAIL.DEMO",
            Phone = "+1-555-0102",
            NormalizedPhone = "15550102",
            CompanyName = "Apex Retail Enterprises",
            Address = "500 Commerce Boulevard",
            City = "Dallas",
            State = "TX",
            Status = "Active",
            CreatedDate = DateTime.UtcNow.AddDays(-25),
            CreatedBy = creator,
            AssignedTo = s1
        };

        var c3 = new Customer
        {
            CustomerCode = "CUST-DEMO-003",
            CustomerName = "Starlight Logistics Ltd",
            Email = "ops@starlightlogistics.demo",
            NormalizedEmail = "OPS@STARLIGHTLOGISTICS.DEMO",
            Phone = "+1-555-0103",
            NormalizedPhone = "15550103",
            CompanyName = "Starlight Logistics Ltd",
            Address = "75 Harbor Road, Terminal 2",
            City = "Seattle",
            State = "WA",
            Status = "Active",
            CreatedDate = DateTime.UtcNow.AddDays(-20),
            CreatedBy = creator,
            AssignedTo = s2
        };

        var c4 = new Customer
        {
            CustomerCode = "CUST-DEMO-004",
            CustomerName = "Horizon Healthcare Inc",
            Email = "admin@horizonhealth.demo",
            NormalizedEmail = "ADMIN@HORIZONHEALTH.DEMO",
            Phone = "+1-555-0104",
            NormalizedPhone = "15550104",
            CompanyName = "Horizon Healthcare Inc",
            Address = "12 Medical Center Drive",
            City = "Chicago",
            State = "IL",
            Status = "Active",
            CreatedDate = DateTime.UtcNow.AddDays(-15),
            CreatedBy = creator,
            AssignedTo = s2
        };

        context.Customers.AddRange(c1, c2, c3, c4);
        await context.SaveChangesAsync();

        // 2. Synthetic Leads
        var l1 = new Lead
        {
            LeadCode = "LEAD-DEMO-001",
            LeadName = "Nexus Robotics",
            Email = "inquiry@nexusrobotics.demo",
            Phone = "+1-555-0201",
            CompanyName = "Nexus Robotics Corp",
            Source = "Website",
            Status = "New",
            ExpectedValue = 35000m,
            CreatedDate = DateTime.UtcNow.AddDays(-8),
            AssignedTo = s1
        };

        var l2 = new Lead
        {
            LeadCode = "LEAD-DEMO-002",
            LeadName = "Omni Cloud Systems",
            Email = "info@omnicloud.demo",
            Phone = "+1-555-0202",
            CompanyName = "Omni Cloud Systems",
            Source = "Referral",
            Status = "Contacted",
            ExpectedValue = 52000m,
            CreatedDate = DateTime.UtcNow.AddDays(-12),
            AssignedTo = s1
        };

        var l3 = new Lead
        {
            LeadCode = "LEAD-DEMO-003",
            LeadName = "Pioneer Energy Corp",
            Email = "bd@pioneerenergy.demo",
            Phone = "+1-555-0203",
            CompanyName = "Pioneer Energy Corp",
            Source = "Campaign",
            Status = "Qualified",
            ExpectedValue = 85000m,
            CreatedDate = DateTime.UtcNow.AddDays(-18),
            AssignedTo = s2
        };

        var l4 = new Lead
        {
            LeadCode = "LEAD-DEMO-004",
            LeadName = "Quantum Software",
            Email = "sales@quantumsoft.demo",
            Phone = "+1-555-0204",
            CompanyName = "Quantum Software",
            Source = "Cold Call",
            Status = "Lost",
            ExpectedValue = 18000m,
            CreatedDate = DateTime.UtcNow.AddDays(-22),
            AssignedTo = s2
        };

        context.Leads.AddRange(l1, l2, l3, l4);
        await context.SaveChangesAsync();

        // 3. Synthetic Opportunities
        var o1 = new Opportunity
        {
            OpportunityName = "Enterprise Cloud Migration",
            CustomerId = c1.CustomerId,
            Amount = 60000m,
            Stage = "Negotiation",
            Probability = 75,
            Status = "Open",
            ExpectedCloseDate = DateTime.UtcNow.AddDays(30),
            CreatedDate = DateTime.UtcNow.AddDays(-14),
            AssignedTo = s1
        };

        var o2 = new Opportunity
        {
            OpportunityName = "Retail POS Modernization",
            CustomerId = c2.CustomerId,
            Amount = 40000m,
            Stage = "Proposal",
            Probability = 50,
            Status = "Open",
            ExpectedCloseDate = DateTime.UtcNow.AddDays(45),
            CreatedDate = DateTime.UtcNow.AddDays(-10),
            AssignedTo = s1
        };

        var o3 = new Opportunity
        {
            OpportunityName = "Supply Chain Analytics Platform",
            CustomerId = c3.CustomerId,
            Amount = 80000m,
            Stage = "Won",
            Probability = 100,
            Status = "Won",
            ExpectedCloseDate = DateTime.UtcNow.AddDays(-5),
            CreatedDate = DateTime.UtcNow.AddDays(-28),
            AssignedTo = s2
        };

        var o4 = new Opportunity
        {
            OpportunityName = "Telehealth Workflow Automation",
            CustomerId = c4.CustomerId,
            Amount = 35000m,
            Stage = "Qualification",
            Probability = 25,
            Status = "Open",
            ExpectedCloseDate = DateTime.UtcNow.AddDays(60),
            CreatedDate = DateTime.UtcNow.AddDays(-7),
            AssignedTo = s2
        };

        context.Opportunities.AddRange(o1, o2, o3, o4);
        await context.SaveChangesAsync();

        // 4. Synthetic Follow-Ups
        var f1 = new FollowUp
        {
            CustomerId = c1.CustomerId,
            OpportunityId = o1.OpportunityId,
            FollowUpDate = DateTime.UtcNow.AddDays(2),
            FollowUpType = "Meeting",
            Remarks = "Review final SLA terms and pricing tiers",
            Status = "Planned",
            AssignedTo = s1
        };

        var f2 = new FollowUp
        {
            LeadId = l1.LeadId,
            FollowUpDate = DateTime.UtcNow.AddDays(3),
            FollowUpType = "Call",
            Remarks = "Discovery call on robot teleoperation infrastructure",
            Status = "Planned",
            AssignedTo = s1
        };

        var f3 = new FollowUp
        {
            CustomerId = c3.CustomerId,
            OpportunityId = o3.OpportunityId,
            FollowUpDate = DateTime.UtcNow.AddDays(-2),
            FollowUpType = "Call",
            Remarks = "Contract signature confirmation completed",
            Status = "Completed",
            AssignedTo = s2
        };

        var f4 = new FollowUp
        {
            CustomerId = c4.CustomerId,
            OpportunityId = o4.OpportunityId,
            FollowUpDate = DateTime.UtcNow.AddDays(5),
            FollowUpType = "Email",
            Remarks = "Send compliance documentation and HIPAA questionnaire",
            Status = "Planned",
            AssignedTo = s2
        };

        context.FollowUps.AddRange(f1, f2, f3, f4);

        // 5. Synthetic Activities
        var a1 = new Activity
        {
            ActivityType = "Meeting",
            Subject = "Architecture Alignment Review",
            Description = "Reviewed high-level architecture with Acme CTO and security lead",
            ActivityDate = DateTime.UtcNow.AddDays(-4),
            CustomerId = c1.CustomerId,
            OpportunityId = o1.OpportunityId,
            Status = "Completed",
            AssignedTo = s1
        };

        var a2 = new Activity
        {
            ActivityType = "Call",
            Subject = "Inbound Inquiry Assessment",
            Description = "Initial qualification call with Omni Cloud technical team",
            ActivityDate = DateTime.UtcNow.AddDays(-2),
            LeadId = l2.LeadId,
            Status = "Completed",
            AssignedTo = s1
        };

        var a3 = new Activity
        {
            ActivityType = "Email",
            Subject = "Onboarding Welcome & Access Credentials",
            Description = "Sent system credentials and welcome packet to Starlight Logistics",
            ActivityDate = DateTime.UtcNow.AddDays(-1),
            CustomerId = c3.CustomerId,
            OpportunityId = o3.OpportunityId,
            Status = "Completed",
            AssignedTo = s2
        };

        var a4 = new Activity
        {
            ActivityType = "Task",
            Subject = "Prepare Security Assessment Pack",
            Description = "Compile SOC 2 and ISO compliance documents for Horizon Healthcare",
            ActivityDate = DateTime.UtcNow.AddDays(4),
            CustomerId = c4.CustomerId,
            OpportunityId = o4.OpportunityId,
            Status = "Pending",
            AssignedTo = s2
        };

        context.Activities.AddRange(a1, a2, a3, a4);

        // 6. Synthetic Audit Log
        var audit = new AuditLog
        {
            Action = "SystemInitialize",
            EntityName = "Database",
            RecordId = "SYS-001",
            NewValue = "System initialized with fresh database and synthetic demo CRM dataset",
            CreatedDate = DateTime.UtcNow.AddDays(-10),
            UserId = creator
        };
        context.AuditLogs.Add(audit);

        await context.SaveChangesAsync();
        logger.LogInformation("Synthetic demo CRM dataset seeded successfully.");
    }
}
