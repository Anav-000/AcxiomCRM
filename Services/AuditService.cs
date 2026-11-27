using AcxiomCRM.Data;
using AcxiomCRM.Models;

namespace AcxiomCRM.Services;

public class AuditService : IAuditService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<AuditService> _logger;

    public AuditService(ApplicationDbContext context, ILogger<AuditService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task LogAsync(
        string action,
        string entityName,
        string? recordId = null,
        string? oldValue = null,
        string? newValue = null,
        string? userId = null,
        string? ipAddress = null)
    {
        try
        {
            var auditLog = new AuditLog
            {
                Action = action,
                EntityName = entityName,
                RecordId = recordId,
                OldValue = oldValue,
                NewValue = newValue,
                UserId = userId,
                IpAddress = ipAddress,
                CreatedDate = DateTime.UtcNow
            };

            _context.AuditLogs.Add(auditLog);
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record audit log for action: {Action}", action);
        }
    }
}
