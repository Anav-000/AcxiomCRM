using AcxiomCRM.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Customer> Customers { get; set; } = null!;
    public DbSet<Lead> Leads { get; set; } = null!;
    public DbSet<Opportunity> Opportunities { get; set; } = null!;
    public DbSet<FollowUp> FollowUps { get; set; } = null!;
    public DbSet<Activity> Activities { get; set; } = null!;
    public DbSet<AuditLog> AuditLogs { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Explicit mandatory table names
        builder.Entity<Customer>(entity =>
        {
            entity.ToTable("Customer");
            entity.HasIndex(c => c.NormalizedEmail)
                  .IsUnique()
                  .HasDatabaseName("IX_Customer_NormalizedEmail");
            entity.HasIndex(c => c.NormalizedPhone)
                  .IsUnique()
                  .HasDatabaseName("IX_Customer_NormalizedPhone");
        });

        builder.Entity<Lead>(entity =>
        {
            entity.ToTable("Lead");

            entity.HasOne(l => l.ConvertedCustomer)
                  .WithMany()
                  .HasForeignKey(l => l.ConvertedCustomerId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Opportunity>(entity =>
        {
            entity.ToTable("Opportunity");

            entity.HasOne(o => o.Customer)
                  .WithMany(c => c.Opportunities)
                  .HasForeignKey(o => o.CustomerId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(o => o.Lead)
                  .WithMany(l => l.Opportunities)
                  .HasForeignKey(o => o.LeadId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<FollowUp>(entity =>
        {
            entity.ToTable("FollowUp");

            entity.HasOne(f => f.Customer)
                  .WithMany(c => c.FollowUps)
                  .HasForeignKey(f => f.CustomerId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(f => f.Lead)
                  .WithMany(l => l.FollowUps)
                  .HasForeignKey(f => f.LeadId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(f => f.Opportunity)
                  .WithMany()
                  .HasForeignKey(f => f.OpportunityId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Activity>(entity =>
        {
            entity.ToTable("Activity");

            entity.HasOne(a => a.Customer)
                  .WithMany(c => c.Activities)
                  .HasForeignKey(a => a.CustomerId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(a => a.Lead)
                  .WithMany(l => l.Activities)
                  .HasForeignKey(a => a.LeadId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(a => a.Opportunity)
                  .WithMany()
                  .HasForeignKey(a => a.OpportunityId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("AuditLog");
        });
    }
}
