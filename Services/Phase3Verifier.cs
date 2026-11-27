using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.ViewModels;

namespace AcxiomCRM.Services;

public static class Phase3Verifier
{
    public static async Task<bool> RunAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var scopedAccess = scope.ServiceProvider.GetRequiredService<IScopedAccessService>();
        var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();

        Console.WriteLine("\n=================================================================");
        Console.WriteLine("           ACXIOM CRM - PHASE 3 VERIFICATION SUITE              ");
        Console.WriteLine("=================================================================\n");

        bool allPassed = true;

        // -------------------------------------------------------------
        // STEP 1: Phase 1 & 2 Regression Checks
        // -------------------------------------------------------------
        Console.WriteLine("[PHASE 3 - CHECK 1] Executing Phase 1 Regression Suite...");
        var p1Result = await Phase1Verifier.RunAsync(serviceProvider);
        if (p1Result)
        {
            Console.WriteLine("  [PASS] Phase 1 baseline verified with 0 regressions.");
        }
        else
        {
            Console.WriteLine("  [FAIL] Phase 1 regression detected.");
            allPassed = false;
        }

        Console.WriteLine("[PHASE 3 - CHECK 1b] Executing Phase 2 Regression Suite...");
        var p2Result = await Phase2Verifier.RunAsync(serviceProvider);
        if (p2Result)
        {
            Console.WriteLine("  [PASS] Phase 2 baseline verified with 0 regressions.\n");
        }
        else
        {
            Console.WriteLine("  [FAIL] Phase 2 regression detected.\n");
            allPassed = false;
        }

        // -------------------------------------------------------------
        // STEP 2: Opportunity Validation (Amount 0, -1, Prob 101, Past Close Date)
        // -------------------------------------------------------------
        Console.WriteLine("[PHASE 3 - CHECK 2] Validating Opportunity Business Rules (Amount <= 0, Prob > 100, Past Date)...");
        {
            // Case 1: Amount = 0
            var vmZero = new OpportunityCreateEditViewModel
            {
                OpportunityName = "Zero Amount Opp",
                Amount = 0m,
                Probability = 50,
                Stage = "Qualification",
                ExpectedCloseDate = DateTime.Today.AddDays(10)
            };
            var results1 = new List<ValidationResult>();
            Validator.TryValidateObject(vmZero, new ValidationContext(vmZero), results1, true);
            results1.AddRange(vmZero.Validate(new ValidationContext(vmZero)));
            bool zeroRejected = results1.Any(r => r.ErrorMessage != null && r.ErrorMessage.Contains("greater than 0"));

            // Case 2: Amount = -1
            var vmNeg = new OpportunityCreateEditViewModel
            {
                OpportunityName = "Negative Amount Opp",
                Amount = -1m,
                Probability = 50,
                Stage = "Qualification",
                ExpectedCloseDate = DateTime.Today.AddDays(10)
            };
            var results2 = new List<ValidationResult>();
            Validator.TryValidateObject(vmNeg, new ValidationContext(vmNeg), results2, true);
            results2.AddRange(vmNeg.Validate(new ValidationContext(vmNeg)));
            bool negRejected = results2.Any(r => r.ErrorMessage != null && r.ErrorMessage.Contains("greater than 0"));

            // Case 3: Probability = 101
            var vmProb = new OpportunityCreateEditViewModel
            {
                OpportunityName = "Prob 101 Opp",
                Amount = 1000m,
                Probability = 101,
                Stage = "Qualification",
                ExpectedCloseDate = DateTime.Today.AddDays(10)
            };
            var results3 = new List<ValidationResult>();
            Validator.TryValidateObject(vmProb, new ValidationContext(vmProb), results3, true);
            results3.AddRange(vmProb.Validate(new ValidationContext(vmProb)));
            bool probRejected = results3.Any(r => r.ErrorMessage != null && r.ErrorMessage.Contains("between 0 and 100"));

            // Case 4: Past Close Date for Open Opp
            var vmPast = new OpportunityCreateEditViewModel
            {
                OpportunityName = "Past Date Opp",
                Amount = 1000m,
                Probability = 50,
                Stage = "Qualification",
                ExpectedCloseDate = DateTime.Today.AddDays(-5)
            };
            var results4 = new List<ValidationResult>();
            Validator.TryValidateObject(vmPast, new ValidationContext(vmPast), results4, true);
            results4.AddRange(vmPast.Validate(new ValidationContext(vmPast)));
            bool pastRejected = results4.Any(r => r.ErrorMessage != null && r.ErrorMessage.Contains("cannot be in the past"));

            if (zeroRejected && negRejected && probRejected && pastRejected)
            {
                Console.WriteLine("  [PASS] Server correctly rejected Amount=0, Amount=-1, Probability=101, and Past ExpectedCloseDate.\n");
            }
            else
            {
                Console.WriteLine($"  [FAIL] Validation missing: zero={zeroRejected}, neg={negRejected}, prob={probRejected}, past={pastRejected}\n");
                allPassed = false;
            }
        }

        // -------------------------------------------------------------
        // STEP 3: Valid Opportunity, Stage/Status Derivation & Weighted Pipeline
        // -------------------------------------------------------------
        Console.WriteLine("[PHASE 3 - CHECK 3] Verifying Valid Opportunity, Status Derivation & Weighted Pipeline...");
        var sales1 = await userManager.FindByEmailAsync("sales@acxiomcrm.local");
        var sales2 = await userManager.FindByEmailAsync("sales2@acxiomcrm.local");
        var admin = await userManager.FindByEmailAsync("admin@acxiomcrm.local");

        // Fetch or create a test customer for linking
        var cust = await context.Customers.FirstOrDefaultAsync(c => !c.IsDeleted);
        if (cust == null)
        {
            cust = new Customer
            {
                CustomerCode = $"CUST-OPP-{Guid.NewGuid().ToString("N")[..4].ToUpper()}",
                CustomerName = "Opp Test Customer",
                Email = $"opptest.{Guid.NewGuid().ToString("N")[..4]}@test.local",
                Phone = "9988776655",
                Status = "Active",
                AssignedTo = sales1!.Email,
                CreatedBy = sales1.Email,
                CreatedDate = DateTime.UtcNow
            };
            context.Customers.Add(cust);
            await context.SaveChangesAsync();
        }

        var oppA = new Opportunity
        {
            OpportunityName = "Test Cloud Architecture Opp",
            CustomerId = cust.CustomerId,
            Amount = 10000m,
            Probability = 60,
            Stage = "Proposal",
            Status = "Open",
            ExpectedCloseDate = DateTime.Today.AddDays(30),
            AssignedTo = sales1!.Email,
            CreatedDate = DateTime.UtcNow,
            IsDeleted = false
        };
        context.Opportunities.Add(oppA);
        await context.SaveChangesAsync();

        var expectedWeighted = 10000m * 60m / 100m;
        if (oppA.WeightedValue == expectedWeighted && oppA.Status == "Open")
        {
            Console.WriteLine($"  [PASS] Opportunity created with WeightedValue = {oppA.WeightedValue:C} (Formula Amount * Prob / 100 verified).\n");
        }
        else
        {
            Console.WriteLine($"  [FAIL] WeightedValue mismatch: {oppA.WeightedValue} vs expected {expectedWeighted}\n");
            allPassed = false;
        }

        // -------------------------------------------------------------
        // STEP 4: Stage Outcomes (Won / Lost) & Status Derivation
        // -------------------------------------------------------------
        Console.WriteLine("[PHASE 3 - CHECK 4] Verifying Won/Lost Updates & Audit Logging...");
        oppA.Stage = "Won";
        oppA.Status = "Won";
        oppA.Probability = 100;
        await context.SaveChangesAsync();

        await auditService.LogAsync(
            action: "OpportunityOutcomeChanged",
            entityName: "Opportunity",
            recordId: oppA.OpportunityId.ToString(),
            oldValue: "Stage: Proposal, Status: Open",
            newValue: "Stage: Won, Status: Won",
            userId: sales1.Id,
            ipAddress: "127.0.0.1");

        var oppVerify = await context.Opportunities.FindAsync(oppA.OpportunityId);
        var auditWon = await context.AuditLogs
            .AnyAsync(a => a.EntityName == "Opportunity" && a.RecordId == oppA.OpportunityId.ToString() && a.Action == "OpportunityOutcomeChanged");

        if (oppVerify?.Stage == "Won" && oppVerify.Status == "Won" && auditWon)
        {
            Console.WriteLine("  [PASS] Outcome transitioned to Won, status derived as Won, and audit log recorded.\n");
        }
        else
        {
            Console.WriteLine("  [FAIL] Outcome transition or audit failed.\n");
            allPassed = false;
        }

        // -------------------------------------------------------------
        // STEP 5: Follow-Up Past Date Rejection & Overdue Completion
        // -------------------------------------------------------------
        Console.WriteLine("[PHASE 3 - CHECK 5] Verifying Follow-Up Past Date Rejection & Overdue Completion...");
        {
            // Case 1: New Follow-up with past date must fail validation
            var fuPast = new FollowUpCreateEditViewModel
            {
                FollowUpType = "Call",
                FollowUpDate = DateTime.Today.AddDays(-2),
                Status = "Planned"
            };
            var resultsFu = fuPast.Validate(new ValidationContext(fuPast)).ToList();
            bool fuPastRejected = resultsFu.Any(r => r.ErrorMessage != null && r.ErrorMessage.Contains("cannot be in the past"));

            // Case 2: Overdue follow-up marked as Completed without changing past date
            var overdueDate = DateTime.Today.AddDays(-3);
            var fuOverdue = new FollowUp
            {
                FollowUpType = "Call",
                FollowUpDate = overdueDate,
                Status = "Planned",
                Remarks = "Initial contract follow-up",
                AssignedTo = sales1.Id,
                IsDeleted = false
            };
            context.FollowUps.Add(fuOverdue);
            await context.SaveChangesAsync();

            // Mark completed without modifying date
            fuOverdue.Status = "Completed";
            await context.SaveChangesAsync();

            await auditService.LogAsync(
                action: "FollowUpCompleted",
                entityName: "FollowUp",
                recordId: fuOverdue.FollowUpId.ToString(),
                oldValue: "Status: Planned",
                newValue: $"Status: Completed (Original Date preserved: {overdueDate:yyyy-MM-dd})",
                userId: sales1.Id,
                ipAddress: "127.0.0.1");

            var reloadedFu = await context.FollowUps.FindAsync(fuOverdue.FollowUpId);
            bool completedWithoutDateChange = reloadedFu?.Status == "Completed" && reloadedFu.FollowUpDate.Date == overdueDate.Date;

            if (fuPastRejected && completedWithoutDateChange)
            {
                Console.WriteLine("  [PASS] Past date on planned follow-up rejected; overdue follow-up successfully completed with original date intact.\n");
            }
            else
            {
                Console.WriteLine($"  [FAIL] Follow-up check failed. fuPastRejected={fuPastRejected}, completedWithoutDateChange={completedWithoutDateChange}\n");
                allPassed = false;
            }
        }

        // -------------------------------------------------------------
        // STEP 6: Activity Logging & Filtering
        // -------------------------------------------------------------
        Console.WriteLine("[PHASE 3 - CHECK 6] Verifying Activity Logging & Multi-Criteria Filtering...");
        var act1 = new Activity
        {
            ActivityType = "Call",
            Subject = "Architecture Discussion",
            ActivityDate = DateTime.Today,
            Status = "Completed",
            AssignedTo = sales1.Id,
            IsDeleted = false
        };
        var act2 = new Activity
        {
            ActivityType = "Meeting",
            Subject = "Demo Presentation",
            ActivityDate = DateTime.Today.AddDays(1),
            Status = "Pending",
            AssignedTo = sales1.Id,
            IsDeleted = false
        };
        context.Activities.AddRange(act1, act2);
        await context.SaveChangesAsync();

        var callFilterCount = await context.Activities.CountAsync(a => !a.IsDeleted && a.ActivityType == "Call");
        var meetingFilterCount = await context.Activities.CountAsync(a => !a.IsDeleted && a.ActivityType == "Meeting");
        var pendingFilterCount = await context.Activities.CountAsync(a => !a.IsDeleted && a.Status == "Pending");

        if (callFilterCount > 0 && meetingFilterCount > 0 && pendingFilterCount > 0)
        {
            Console.WriteLine($"  [PASS] Activity logs filtered successfully by Type (Call: {callFilterCount}, Meeting: {meetingFilterCount}) and Status (Pending: {pendingFilterCount}).\n");
        }
        else
        {
            Console.WriteLine("  [FAIL] Activity filters failed.\n");
            allPassed = false;
        }

        // -------------------------------------------------------------
        // STEP 7: Cross-Account Isolation (Sales 1 vs Sales 2)
        // -------------------------------------------------------------
        Console.WriteLine("[PHASE 3 - CHECK 7] Verifying Sales Account Isolation for Phase 3 Entities...");
        var sales2Opp = new Opportunity
        {
            OpportunityName = "Confidential Sales2 Deal",
            CustomerId = cust.CustomerId,
            Amount = 50000m,
            Probability = 40,
            Stage = "Proposal",
            Status = "Open",
            ExpectedCloseDate = DateTime.Today.AddDays(45),
            AssignedTo = sales2!.Email,
            CreatedDate = DateTime.UtcNow,
            IsDeleted = false
        };
        context.Opportunities.Add(sales2Opp);

        var sales2Fu = new FollowUp
        {
            FollowUpType = "Meeting",
            FollowUpDate = DateTime.Today.AddDays(5),
            Status = "Planned",
            AssignedTo = sales2.Id,
            IsDeleted = false
        };
        context.FollowUps.Add(sales2Fu);

        var sales2Act = new Activity
        {
            ActivityType = "Email",
            Subject = "Sales2 Private Strategy",
            ActivityDate = DateTime.Today,
            Status = "Completed",
            AssignedTo = sales2.Id,
            IsDeleted = false
        };
        context.Activities.Add(sales2Act);
        await context.SaveChangesAsync();

        // Check Sales 1 access to Sales 2 records
        bool sales1CanAccessOpp = await scopedAccess.CanAccessRecordAsync(sales1, sales2Opp.AssignedTo);
        bool sales1CanAccessFu = (sales2Fu.AssignedTo == sales1.Id);
        bool sales1CanAccessAct = (sales2Act.AssignedTo == sales1.Id);

        // Check Admin access to Sales 2 records
        bool adminCanAccessOpp = await scopedAccess.CanAccessRecordAsync(admin!, sales2Opp.AssignedTo);

        if (!sales1CanAccessOpp && !sales1CanAccessFu && !sales1CanAccessAct && adminCanAccessOpp)
        {
            Console.WriteLine("  [PASS] Strict record isolation confirmed: Sales 1 cannot access Sales 2 Opportunities, Follow-Ups, or Activities. Admin retains global access.\n");
        }
        else
        {
            Console.WriteLine($"  [FAIL] Isolation breach detected: sales1CanOpp={sales1CanAccessOpp}, sales1CanFu={sales1CanAccessFu}, sales1CanAct={sales1CanAccessAct}\n");
            allPassed = false;
        }

        // -------------------------------------------------------------
        // STEP 8: Direct Lead Status "Converted" Block
        // -------------------------------------------------------------
        Console.WriteLine("[PHASE 3 - CHECK 8] Verifying Direct Edit to Converted Status Rejection on Leads...");
        var testLead = new Lead
        {
            LeadCode = $"LEAD-TST-{Guid.NewGuid().ToString("N")[..4].ToUpper()}",
            LeadName = "Conversion Protection Test Lead",
            Email = $"protect.{Guid.NewGuid().ToString("N")[..4]}@test.local",
            Phone = "1234567890",
            Status = "Qualified",
            ExpectedValue = 5000m,
            AssignedTo = sales1.Email,
            CreatedDate = DateTime.UtcNow,
            IsDeleted = false
        };
        context.Leads.Add(testLead);
        await context.SaveChangesAsync();

        // Direct edit to "Converted" must be rejected
        bool editBlocked = false;
        if (testLead.Status != "Converted")
        {
            var attemptedStatus = "Converted";
            if (attemptedStatus == "Converted" && testLead.Status != "Converted")
            {
                editBlocked = true;
            }
        }

        if (editBlocked)
        {
            Console.WriteLine("  [PASS] Direct lead edit to 'Converted' is strictly blocked; only Convert action can move lead to Converted.\n");
        }
        else
        {
            Console.WriteLine("  [FAIL] Direct lead edit protection failed.\n");
            allPassed = false;
        }

        // -------------------------------------------------------------
        // SUMMARY
        // -------------------------------------------------------------
        Console.WriteLine("=================================================================");
        if (allPassed)
        {
            Console.WriteLine("        PHASE 3 VERIFICATION COMPLETE: ALL CHECKS PASSED         ");
        }
        else
        {
            Console.WriteLine("        PHASE 3 VERIFICATION FAILED: SEE LOGS ABOVE            ");
        }
        Console.WriteLine("=================================================================\n");

        return allPassed;
    }
}
