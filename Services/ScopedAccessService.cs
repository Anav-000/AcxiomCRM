using AcxiomCRM.Data;
using AcxiomCRM.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace AcxiomCRM.Services;

public interface IScopedAccessService
{
    Task<bool> CanAccessRecordAsync(ApplicationUser user, string? assignedTo);
    Task<bool> CanReassignRecordAsync(ApplicationUser user);
    Task<IQueryable<T>> ApplyScopeAsync<T>(IQueryable<T> query, ApplicationUser user, Expression<Func<T, string?>> assignedToSelector) where T : class;
    Task<List<ApplicationUser>> GetAssignableUsersAsync(ApplicationUser user);
}

public class ScopedAccessService : IScopedAccessService
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public ScopedAccessService(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    public async Task<bool> CanAccessRecordAsync(ApplicationUser user, string? assignedTo)
    {
        if (await _userManager.IsInRoleAsync(user, DbInitializer.RoleAdmin))
        {
            return true;
        }

        var userEmail = user.Email ?? string.Empty;
        var userId = user.Id;

        // SalesExecutive: only directly assigned records
        if (await _userManager.IsInRoleAsync(user, DbInitializer.RoleSalesExecutive))
        {
            return assignedTo == userEmail || assignedTo == userId;
        }

        // Manager: records assigned to manager or anyone in manager's team
        if (await _userManager.IsInRoleAsync(user, DbInitializer.RoleManager))
        {
            if (assignedTo == userEmail || assignedTo == userId)
            {
                return true;
            }

            var teamMemberIds = await _context.Users
                .Where(u => u.ManagerId == userId || u.ManagerId == userEmail)
                .Select(u => u.Id)
                .ToListAsync();

            var teamMemberEmails = await _context.Users
                .Where(u => u.ManagerId == userId || u.ManagerId == userEmail)
                .Select(u => u.Email)
                .ToListAsync();

            return teamMemberIds.Contains(assignedTo ?? "") || teamMemberEmails.Contains(assignedTo ?? "");
        }

        return false;
    }

    public async Task<bool> CanReassignRecordAsync(ApplicationUser user)
    {
        return await _userManager.IsInRoleAsync(user, DbInitializer.RoleAdmin) ||
               await _userManager.IsInRoleAsync(user, DbInitializer.RoleManager);
    }

    public async Task<IQueryable<T>> ApplyScopeAsync<T>(
        IQueryable<T> query,
        ApplicationUser user,
        Expression<Func<T, string?>> assignedToSelector) where T : class
    {
        if (await _userManager.IsInRoleAsync(user, DbInitializer.RoleAdmin))
        {
            return query;
        }

        var userEmail = user.Email ?? string.Empty;
        var userId = user.Id;

        if (await _userManager.IsInRoleAsync(user, DbInitializer.RoleSalesExecutive))
        {
            var parameter = assignedToSelector.Parameters[0];
            var member = assignedToSelector.Body;
            var emailConst = Expression.Constant(userEmail, typeof(string));
            var idConst = Expression.Constant(userId, typeof(string));

            var equalsEmail = Expression.Equal(member, emailConst);
            var equalsId = Expression.Equal(member, idConst);
            var orExpr = Expression.OrElse(equalsEmail, equalsId);

            var lambda = Expression.Lambda<Func<T, bool>>(orExpr, parameter);
            return query.Where(lambda);
        }

        if (await _userManager.IsInRoleAsync(user, DbInitializer.RoleManager))
        {
            var teamMembers = await _context.Users
                .Where(u => u.ManagerId == userId || u.ManagerId == userEmail)
                .Select(u => u.Email)
                .ToListAsync();

            teamMembers.Add(userEmail);
            teamMembers.Add(userId);

            var parameter = assignedToSelector.Parameters[0];
            var member = assignedToSelector.Body;
            var listConst = Expression.Constant(teamMembers);
            var containsMethod = typeof(List<string>).GetMethod("Contains", new[] { typeof(string) })!;
            var containsCall = Expression.Call(listConst, containsMethod, member);

            var lambda = Expression.Lambda<Func<T, bool>>(containsCall, parameter);
            return query.Where(lambda);
        }

        return query.Where(_ => false);
    }

    public async Task<List<ApplicationUser>> GetAssignableUsersAsync(ApplicationUser user)
    {
        if (await _userManager.IsInRoleAsync(user, DbInitializer.RoleAdmin))
        {
            return await _context.Users.Where(u => u.IsActive).OrderBy(u => u.Name).ToListAsync();
        }

        if (await _userManager.IsInRoleAsync(user, DbInitializer.RoleManager))
        {
            var userEmail = user.Email ?? string.Empty;
            var userId = user.Id;
            return await _context.Users
                .Where(u => u.IsActive && (u.Id == userId || u.ManagerId == userId || u.ManagerId == userEmail))
                .OrderBy(u => u.Name)
                .ToListAsync();
        }

        return new List<ApplicationUser> { user };
    }
}
