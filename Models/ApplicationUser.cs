using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace AcxiomCRM.Models;

public class ApplicationUser : IdentityUser
{
    public string Name { get; set; } = string.Empty;
    public string FullName => string.IsNullOrWhiteSpace(Name) ? (UserName ?? Email ?? "User") : Name;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    [MaxLength(100)]
    public string? ManagerId { get; set; }
}
