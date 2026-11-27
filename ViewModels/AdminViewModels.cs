using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using AcxiomCRM.Models;

namespace AcxiomCRM.ViewModels;

public class AdminUserListViewModel
{
    public List<AdminUserItemViewModel> Users { get; set; } = new();
    public string? SearchTerm { get; set; }
    public string? RoleFilter { get; set; }
}

public class AdminUserItemViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsLockedOut { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
    public int AccessFailedCount { get; set; }
    public DateTime CreatedDate { get; set; }
    public string? ManagerName { get; set; }
}

public class AdminUserEditViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Role { get; set; } = "SalesExecutive";

    public string? ManagerId { get; set; }

    public bool IsActive { get; set; }

    public SelectList? AvailableRoles { get; set; }
    public SelectList? AvailableManagers { get; set; }
}

public class AdminResetPasswordViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters long.")]
    public string NewPassword { get; set; } = string.Empty;
}

public class AdminAuditLogsViewModel
{
    public List<AuditLog> Logs { get; set; } = new();
    public string? UserFilter { get; set; }
    public string? ActionFilter { get; set; }
    public string? ModuleFilter { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }

    public SelectList? AvailableActions { get; set; }
    public SelectList? AvailableModules { get; set; }
}
