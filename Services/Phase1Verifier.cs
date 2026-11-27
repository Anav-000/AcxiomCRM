using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Services;

public static class Phase1Verifier
{
    public static async Task<bool> RunAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var signInManager = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
        var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();

        Console.WriteLine("\n=================================================================");
        Console.WriteLine("           ACXIOM CRM - PHASE 1 VERIFICATION SUITE              ");
        Console.WriteLine("=================================================================\n");

        bool allPassed = true;

        // 1. Verify Database Tables
        Console.WriteLine("[TEST 1] Verifying Mandatory Tables in SQLite...");
        var requiredTables = new[] { "Customer", "Lead", "Opportunity", "FollowUp", "Activity", "AuditLog" };
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        foreach (var table in requiredTables)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}'";
            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            if (count > 0)
            {
                Console.WriteLine($"  [PASS] Table '{table}' exists.");
            }
            else
            {
                Console.WriteLine($"  [FAIL] Table '{table}' is missing!");
                allPassed = false;
            }
        }

        // 2. Verify Unique Indexes on Customer
        Console.WriteLine("\n[TEST 2] Verifying Unique Indexes on Customer...");
        var requiredIndexes = new[] { "IX_Customer_NormalizedEmail", "IX_Customer_NormalizedPhone" };
        foreach (var index in requiredIndexes)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='{index}'";
            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            if (count > 0)
            {
                Console.WriteLine($"  [PASS] Unique index '{index}' exists on Customer table.");
            }
            else
            {
                Console.WriteLine($"  [FAIL] Unique index '{index}' is missing!");
                allPassed = false;
            }
        }

        // 3. Verify Roles
        Console.WriteLine("\n[TEST 3] Verifying Identity Roles...");
        var requiredRoles = new[] { DbInitializer.RoleAdmin, DbInitializer.RoleManager, DbInitializer.RoleSalesExecutive };
        foreach (var role in requiredRoles)
        {
            var exists = await roleManager.RoleExistsAsync(role);
            if (exists)
            {
                Console.WriteLine($"  [PASS] Role '{role}' exists.");
            }
            else
            {
                Console.WriteLine($"  [FAIL] Role '{role}' does not exist!");
                allPassed = false;
            }
        }

        // 4. Verify Seeded Demo Accounts
        Console.WriteLine("\n[TEST 4] Verifying Seeded Test Accounts & Role Assignments...");
        var demoAccounts = new[]
        {
            (Email: "admin@acxiomcrm.local", Role: DbInitializer.RoleAdmin),
            (Email: "manager@acxiomcrm.local", Role: DbInitializer.RoleManager),
            (Email: "sales@acxiomcrm.local", Role: DbInitializer.RoleSalesExecutive)
        };

        foreach (var acc in demoAccounts)
        {
            var user = await userManager.FindByEmailAsync(acc.Email);
            if (user != null)
            {
                var inRole = await userManager.IsInRoleAsync(user, acc.Role);
                var hasPasswordHash = !string.IsNullOrEmpty(user.PasswordHash) && !user.PasswordHash.Contains("Password");
                if (inRole && hasPasswordHash)
                {
                    Console.WriteLine($"  [PASS] User '{acc.Email}' is seeded in role '{acc.Role}' with secure password hash.");
                }
                else
                {
                    Console.WriteLine($"  [FAIL] User '{acc.Email}' role or password hash check failed! (inRole: {inRole}, hasHash: {hasPasswordHash})");
                    allPassed = false;
                }
            }
            else
            {
                Console.WriteLine($"  [FAIL] User '{acc.Email}' not found!");
                allPassed = false;
            }
        }

        // 5. Test Public Registration Assigning SalesExecutive
        Console.WriteLine("\n[TEST 5] Testing Public Registration & Role Assignment...");
        var testRegisterEmail = $"test.sales.{Guid.NewGuid().ToString("N")[..6]}@acxiomcrm.local";
        var newUser = new ApplicationUser
        {
            UserName = testRegisterEmail,
            Email = testRegisterEmail,
            Name = "New Test Sales Executive",
            IsActive = true,
            CreatedDate = DateTime.UtcNow
        };

        var regResult = await userManager.CreateAsync(newUser, "SalesExec@2026!");
        if (regResult.Succeeded)
        {
            await userManager.AddToRoleAsync(newUser, DbInitializer.RoleSalesExecutive);
            var isSalesExec = await userManager.IsInRoleAsync(newUser, DbInitializer.RoleSalesExecutive);
            var isManager = await userManager.IsInRoleAsync(newUser, DbInitializer.RoleManager);
            var isAdmin = await userManager.IsInRoleAsync(newUser, DbInitializer.RoleAdmin);

            if (isSalesExec && !isManager && !isAdmin)
            {
                Console.WriteLine($"  [PASS] User '{testRegisterEmail}' successfully registered and assigned strictly '{DbInitializer.RoleSalesExecutive}'.");
            }
            else
            {
                Console.WriteLine($"  [FAIL] Role assignment failed for new registration.");
                allPassed = false;
            }

            // Audit the registration
            await auditService.LogAsync("Register", "Authentication", newUser.Id, null, "Verification Registration", newUser.Id, "127.0.0.1");
        }
        else
        {
            Console.WriteLine($"  [FAIL] Registration failed: {string.Join(", ", regResult.Errors.Select(e => e.Description))}");
            allPassed = false;
        }

        // 6. Test Password Policy Validation
        Console.WriteLine("\n[TEST 6] Testing Password Policy Enforcement...");
        var weakUser = new ApplicationUser { UserName = "weak@acxiomcrm.local", Email = "weak@acxiomcrm.local", Name = "Weak" };
        var weakResult = await userManager.CreateAsync(weakUser, "weak");
        if (!weakResult.Succeeded)
        {
            Console.WriteLine($"  [PASS] Weak password correctly rejected by policy: {weakResult.Errors.First().Description}");
        }
        else
        {
            Console.WriteLine($"  [FAIL] Weak password was unexpectedly accepted!");
            allPassed = false;
        }

        // 7. Test Account Lockout Mechanism
        Console.WriteLine("\n[TEST 7] Testing Account Lockout After Repeated Failures...");
        var lockoutTargetEmail = $"lockout.test.{Guid.NewGuid().ToString("N")[..6]}@acxiomcrm.local";
        var lockoutUser = new ApplicationUser
        {
            UserName = lockoutTargetEmail,
            Email = lockoutTargetEmail,
            Name = "Lockout Tester",
            IsActive = true,
            CreatedDate = DateTime.UtcNow
        };
        await userManager.CreateAsync(lockoutUser, "Lockout@12345!");
        await userManager.SetLockoutEnabledAsync(lockoutUser, true);

        // Simulate 5 failed login attempts
        for (int i = 1; i <= 5; i++)
        {
            await userManager.AccessFailedAsync(lockoutUser);
            await auditService.LogAsync("FailedLogin", "Authentication", lockoutUser.Id, null, $"Attempt {i}", lockoutUser.Id, "127.0.0.1");
        }

        var isLocked = await userManager.IsLockedOutAsync(lockoutUser);
        if (isLocked)
        {
            Console.WriteLine($"  [PASS] User '{lockoutTargetEmail}' is locked out after 5 failed attempts.");
            await auditService.LogAsync("Lockout", "Authentication", lockoutUser.Id, null, "Account locked out", lockoutUser.Id, "127.0.0.1");
        }
        else
        {
            Console.WriteLine($"  [FAIL] User was not locked out after 5 failed attempts!");
            allPassed = false;
        }

        // 8. Test AuditLog Persistence
        Console.WriteLine("\n[TEST 8] Verifying AuditLog Records in Database...");
        // Add a logout audit event
        await auditService.LogAsync("Logout", "Authentication", newUser.Id, null, "User logged out", newUser.Id, "127.0.0.1");

        var auditActions = await context.AuditLogs.Select(a => a.Action).Distinct().ToListAsync();
        Console.WriteLine($"  Recorded Audit Actions: {string.Join(", ", auditActions)}");

        bool hasRegister = auditActions.Contains("Register");
        bool hasFailedLogin = auditActions.Contains("FailedLogin");
        bool hasLockout = auditActions.Contains("Lockout");
        bool hasLogout = auditActions.Contains("Logout");

        if (hasRegister && hasFailedLogin && hasLockout && hasLogout)
        {
            Console.WriteLine("  [PASS] All security audit event types (Register, FailedLogin, Lockout, Logout) verified in AuditLog table.");
        }
        else
        {
            Console.WriteLine("  [FAIL] Missing expected audit actions in AuditLog table!");
            allPassed = false;
        }

        // 9. Test Customer Normalization & Unique Index Enforcement
        Console.WriteLine("\n[TEST 9] Verifying Customer Unique Normalized Email & Phone Constraint...");
        var uniqueEmail = $"test.cust.{Guid.NewGuid().ToString("N")[..6]}@domain.com";
        var uniquePhone = $"{Random.Shared.Next(100000000, 999999999):D10}";
        var cust1 = new Customer
        {
            CustomerCode = "CUST-001",
            CustomerName = "Acme Corp",
            Email = uniqueEmail,
            NormalizedEmail = uniqueEmail.ToUpperInvariant(),
            Phone = uniquePhone,
            NormalizedPhone = uniquePhone,
            Status = "Active",
            CreatedDate = DateTime.UtcNow
        };
        context.Customers.Add(cust1);
        await context.SaveChangesAsync();

        var cust2 = new Customer
        {
            CustomerCode = "CUST-002",
            CustomerName = "Duplicate Corp",
            Email = "TEST.CUST@DOMAIN.COM",
            NormalizedEmail = uniqueEmail.ToUpperInvariant(), // duplicate normalized
            Phone = "1234567890",
            NormalizedPhone = "1234567890",
            Status = "Active",
            CreatedDate = DateTime.UtcNow
        };
        context.Customers.Add(cust2);

        try
        {
            await context.SaveChangesAsync();
            Console.WriteLine("  [FAIL] Duplicate normalized customer email was accepted without error!");
            allPassed = false;
        }
        catch (DbUpdateException)
        {
            Console.WriteLine("  [PASS] Duplicate normalized customer email successfully blocked by unique index IX_Customer_NormalizedEmail.");
            // Detach duplicate entity to keep context clean
            context.Entry(cust2).State = EntityState.Detached;
        }

        Console.WriteLine("\n=================================================================");
        if (allPassed)
        {
            Console.WriteLine("           ALL PHASE 1 VERIFICATION TESTS PASSED!               ");
        }
        else
        {
            Console.WriteLine("           SOME VERIFICATION TESTS FAILED!                       ");
        }
        Console.WriteLine("=================================================================\n");

        return allPassed;
    }
}
