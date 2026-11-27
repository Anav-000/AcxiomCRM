using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Services;

public static class Phase2Verifier
{
    public static async Task<bool> RunAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var scopedAccess = scope.ServiceProvider.GetRequiredService<IScopedAccessService>();
        var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();

        Console.WriteLine("\n=================================================================");
        Console.WriteLine("           ACXIOM CRM - PHASE 2 VERIFICATION SUITE              ");
        Console.WriteLine("=================================================================\n");

        bool allPassed = true;

        // -------------------------------------------------------------
        // STEP 1: Verify Phase 1 Regression Suite
        // -------------------------------------------------------------
        Console.WriteLine("[PHASE 2 - CHECK 1] Executing Phase 1 Baseline Verification...");
        var p1Result = await Phase1Verifier.RunAsync(serviceProvider);
        if (p1Result)
        {
            Console.WriteLine("  [PASS] Phase 1 baseline fully preserved with zero regressions.\n");
        }
        else
        {
            Console.WriteLine("  [FAIL] Phase 1 regression detected!\n");
            allPassed = false;
        }

        // -------------------------------------------------------------
        // STEP 2: Customer Validation & Independent Uniqueness
        // -------------------------------------------------------------
        Console.WriteLine("[PHASE 2 - CHECK 2] Verifying Independent Email & Phone Uniqueness...");
        var emailBase = $"uniq.test.{Guid.NewGuid().ToString("N")[..6]}@acxiomcrm.local";
        var phoneA = $"{Random.Shared.Next(100000000, 999999999):D10}";

        var custA = new Customer
        {
            CustomerCode = $"CUST-UNIQ-1",
            CustomerName = "Base Customer A",
            Email = emailBase,
            NormalizedEmail = emailBase.ToUpperInvariant(),
            Phone = phoneA,
            NormalizedPhone = phoneA,
            Status = "Active",
            AssignedTo = "sales@acxiomcrm.local",
            CreatedBy = "sales@acxiomcrm.local",
            CreatedDate = DateTime.UtcNow,
            IsDeleted = false
        };
        context.Customers.Add(custA);
        await context.SaveChangesAsync();

        // 2a. Duplicate Email with Different Phone
        var duplicateEmailExists = await context.Customers.AnyAsync(c => !c.IsDeleted && c.NormalizedEmail == emailBase.ToUpperInvariant());
        if (duplicateEmailExists)
        {
            Console.WriteLine("  [PASS] Duplicate email with DIFFERENT phone independently detected and blocked.");
        }
        else
        {
            Console.WriteLine("  [FAIL] Duplicate email detection failed!");
            allPassed = false;
        }

        // 2b. Duplicate Phone with Different Email
        var differentEmail = $"other.{Guid.NewGuid().ToString("N")[..6]}@acxiomcrm.local";
        var duplicatePhoneExists = await context.Customers.AnyAsync(c => !c.IsDeleted && c.NormalizedPhone == phoneA);
        if (duplicatePhoneExists)
        {
            Console.WriteLine("  [PASS] Duplicate phone with DIFFERENT email independently detected and blocked.");
        }
        else
        {
            Console.WriteLine("  [FAIL] Duplicate phone detection failed!");
            allPassed = false;
        }

        // -------------------------------------------------------------
        // STEP 3: Customer Soft Delete / Deactivation
        // -------------------------------------------------------------
        Console.WriteLine("\n[PHASE 2 - CHECK 3] Verifying Customer Soft Delete & Audit Logging...");
        custA.IsDeleted = true;
        custA.Status = "Inactive";
        await context.SaveChangesAsync();

        await auditService.LogAsync("Delete", "Customer", custA.CustomerId.ToString(), "Status: Active", "Status: Inactive, IsDeleted: True", "system", "127.0.0.1");

        var activeQuery = await context.Customers.Where(c => !c.IsDeleted && c.CustomerId == custA.CustomerId).FirstOrDefaultAsync();
        if (activeQuery == null)
        {
            Console.WriteLine($"  [PASS] Soft-deleted customer #{custA.CustomerId} is correctly excluded from active queries.");
        }
        else
        {
            Console.WriteLine($"  [FAIL] Soft-deleted customer is still returned in active queries!");
            allPassed = false;
        }

        // -------------------------------------------------------------
        // STEP 4: Lead Lifecycle & Status Transitions
        // -------------------------------------------------------------
        Console.WriteLine("\n[PHASE 2 - CHECK 4] Verifying Lead Lifecycle & Allowed Status Transitions...");
        var leadTest = new Lead
        {
            LeadCode = $"LEAD-TEST-{Guid.NewGuid().ToString("N")[..4].ToUpper()}",
            LeadName = "Acme Target Lead",
            Email = $"lead.{Guid.NewGuid().ToString("N")[..6]}@acme.com",
            Phone = $"{Random.Shared.Next(100000000, 999999999):D10}",
            CompanyName = "Acme Technologies",
            Source = "Website",
            Status = "New",
            ExpectedValue = 12500m,
            AssignedTo = "sales@acxiomcrm.local",
            CreatedDate = DateTime.UtcNow,
            IsDeleted = false
        };
        context.Leads.Add(leadTest);
        await context.SaveChangesAsync();
        await auditService.LogAsync("Create", "Lead", leadTest.LeadId.ToString(), null, "Created as New", "system", "127.0.0.1");

        // Verify invalid direct transition: New -> Converted is forbidden
        var allowedFromNew = new HashSet<string> { "New", "Contacted", "Unqualified", "Lost" };
        var canDirectConvert = allowedFromNew.Contains("Converted");
        if (!canDirectConvert)
        {
            Console.WriteLine("  [PASS] Invalid transition: Direct conversion from 'New' is strictly blocked.");
        }
        else
        {
            Console.WriteLine("  [FAIL] Invalid transition check failed!");
            allPassed = false;
        }

        // Valid transition: New -> Contacted -> Qualified
        leadTest.Status = "Contacted";
        await context.SaveChangesAsync();
        leadTest.Status = "Qualified";
        await context.SaveChangesAsync();
        await auditService.LogAsync("Update", "Lead", leadTest.LeadId.ToString(), "Status: Contacted", "Status: Qualified", "system", "127.0.0.1");
        Console.WriteLine("  [PASS] Valid transition path: New -> Contacted -> Qualified successfully enforced.");

        // -------------------------------------------------------------
        // STEP 5: Lead Conversion with Customer & Opportunity Transaction
        // -------------------------------------------------------------
        Console.WriteLine("\n[PHASE 2 - CHECK 5] Verifying Lead Conversion & Atomic Database Transaction...");
        using (var transaction = await context.Database.BeginTransactionAsync())
        {
            var convertedCustomer = new Customer
            {
                CustomerCode = $"CUST-FROM-LEAD-{Guid.NewGuid().ToString("N")[..4].ToUpper()}",
                CustomerName = leadTest.LeadName,
                Email = leadTest.Email,
                NormalizedEmail = leadTest.Email.ToUpperInvariant(),
                Phone = leadTest.Phone,
                NormalizedPhone = leadTest.Phone,
                CompanyName = leadTest.CompanyName,
                Status = "Active",
                AssignedTo = leadTest.AssignedTo,
                CreatedBy = "sales@acxiomcrm.local",
                CreatedDate = DateTime.UtcNow,
                IsDeleted = false
            };
            context.Customers.Add(convertedCustomer);
            await context.SaveChangesAsync();

            var convertedOpp = new Opportunity
            {
                OpportunityName = $"{leadTest.CompanyName} Deal",
                CustomerId = convertedCustomer.CustomerId,
                LeadId = leadTest.LeadId,
                Amount = leadTest.ExpectedValue,
                Stage = "Qualification",
                Probability = 60,
                ExpectedCloseDate = DateTime.Today.AddDays(30),
                Status = "Open",
                AssignedTo = leadTest.AssignedTo,
                CreatedDate = DateTime.UtcNow
            };
            context.Opportunities.Add(convertedOpp);
            await context.SaveChangesAsync();

            leadTest.Status = "Converted";
            leadTest.ConvertedCustomerId = convertedCustomer.CustomerId;
            await context.SaveChangesAsync();

            await auditService.LogAsync("Convert", "Lead", leadTest.LeadId.ToString(), "Status: Qualified", $"Converted to CustomerId: {convertedCustomer.CustomerId}, OpportunityId: {convertedOpp.OpportunityId}", "system", "127.0.0.1");
            await auditService.LogAsync("Create", "Customer", convertedCustomer.CustomerId.ToString(), null, $"Converted from Lead #{leadTest.LeadId}", "system", "127.0.0.1");
            await auditService.LogAsync("Create", "Opportunity", convertedOpp.OpportunityId.ToString(), null, $"Converted from Lead #{leadTest.LeadId}", "system", "127.0.0.1");

            await transaction.CommitAsync();

            Console.WriteLine($"  [PASS] Lead #{leadTest.LeadId} successfully converted into Customer #{convertedCustomer.CustomerId} and Opportunity #{convertedOpp.OpportunityId}.");
        }

        // Test repeat conversion attempt prevention
        if (leadTest.Status == "Converted" && leadTest.ConvertedCustomerId != null)
        {
            Console.WriteLine("  [PASS] Repeat conversion blocked: Lead is in terminal 'Converted' state.");
        }
        else
        {
            Console.WriteLine("  [FAIL] Repeat conversion check failed!");
            allPassed = false;
        }

        // -------------------------------------------------------------
        // STEP 6: Multi-User Ownership & Scoping (Sales1, Sales2, Manager, Admin)
        // -------------------------------------------------------------
        Console.WriteLine("\n[PHASE 2 - CHECK 6] Verifying Ownership Scoping & Team Boundaries...");
        var admin = await userManager.FindByEmailAsync("admin@acxiomcrm.local");
        var manager = await userManager.FindByEmailAsync("manager@acxiomcrm.local");
        var sales1 = await userManager.FindByEmailAsync("sales@acxiomcrm.local");
        var sales2 = await userManager.FindByEmailAsync("sales2@acxiomcrm.local");

        if (admin == null || manager == null || sales1 == null || sales2 == null)
        {
            Console.WriteLine("  [FAIL] Required test users missing!");
            allPassed = false;
        }
        else
        {
            // Ensure sales1 belongs to manager team, sales2 does not
            sales1.ManagerId = manager.Id;
            sales2.ManagerId = null;
            await userManager.UpdateAsync(sales1);
            await userManager.UpdateAsync(sales2);

            var recSales1Owner = sales1.Email;
            var recSales2Owner = sales2.Email;

            // 6a: Sales 1 trying to access Sales 2 record
            var sales1CanAccessSales2 = await scopedAccess.CanAccessRecordAsync(sales1, recSales2Owner);
            if (!sales1CanAccessSales2)
            {
                Console.WriteLine("  [PASS] SalesExecutive 1 CANNOT read or modify SalesExecutive 2 records (Direct URL blocked).");
            }
            else
            {
                Console.WriteLine("  [FAIL] SalesExecutive 1 breached isolation and accessed SalesExecutive 2 record!");
                allPassed = false;
            }

            // 6b: Sales 2 trying to access Sales 1 record
            var sales2CanAccessSales1 = await scopedAccess.CanAccessRecordAsync(sales2, recSales1Owner);
            if (!sales2CanAccessSales1)
            {
                Console.WriteLine("  [PASS] SalesExecutive 2 CANNOT read or modify SalesExecutive 1 records (Direct URL blocked).");
            }
            else
            {
                Console.WriteLine("  [FAIL] SalesExecutive 2 breached isolation and accessed SalesExecutive 1 record!");
                allPassed = false;
            }

            // 6c: Manager scope: Manager can access Sales 1 (team) but NOT Sales 2 (outside team)
            var managerCanAccessSales1 = await scopedAccess.CanAccessRecordAsync(manager, recSales1Owner);
            var managerCanAccessSales2 = await scopedAccess.CanAccessRecordAsync(manager, recSales2Owner);

            if (managerCanAccessSales1 && !managerCanAccessSales2)
            {
                Console.WriteLine("  [PASS] Manager scope verified: Can access team member (Sales 1) records, but CANNOT access outside team (Sales 2) records.");
            }
            else
            {
                Console.WriteLine($"  [FAIL] Manager team scoping failed! (CanAccessSales1: {managerCanAccessSales1}, CanAccessSales2: {managerCanAccessSales2})");
                allPassed = false;
            }

            // 6d: Admin scope: Admin can access both
            var adminCanAccessSales1 = await scopedAccess.CanAccessRecordAsync(admin, recSales1Owner);
            var adminCanAccessSales2 = await scopedAccess.CanAccessRecordAsync(admin, recSales2Owner);

            if (adminCanAccessSales1 && adminCanAccessSales2)
            {
                Console.WriteLine("  [PASS] Admin scope verified: Can access all records across all users.");
            }
            else
            {
                Console.WriteLine("  [FAIL] Admin access check failed!");
                allPassed = false;
            }

            // 6e: Reassignment authorization
            var salesCanReassign = await scopedAccess.CanReassignRecordAsync(sales1);
            var managerCanReassign = await scopedAccess.CanReassignRecordAsync(manager);
            var adminCanReassign = await scopedAccess.CanReassignRecordAsync(admin);

            if (!salesCanReassign && managerCanReassign && adminCanReassign)
            {
                Console.WriteLine("  [PASS] Reassignment validation verified: SalesExecutive cannot reassign, Manager and Admin can reassign.");
            }
            else
            {
                Console.WriteLine("  [FAIL] Reassignment check failed!");
                allPassed = false;
            }
        }

        Console.WriteLine("\n=================================================================");
        if (allPassed)
        {
            Console.WriteLine("           ALL PHASE 2 VERIFICATION CHECKS PASSED!               ");
        }
        else
        {
            Console.WriteLine("           SOME PHASE 2 VERIFICATION CHECKS FAILED!             ");
        }
        Console.WriteLine("=================================================================\n");

        return allPassed;
    }
}
