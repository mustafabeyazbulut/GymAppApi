using GymAppApi.Domain.Common;
using GymAppApi.Domain.Enums;

namespace GymAppApi.Domain.Entities;

// CompanyId/BranchId are nullable by design: a SuperAdmin assignment has
// both null (platform-wide), a GymAdmin assignment has CompanyId set and
// BranchId null (all branches of that company).
public class Assignment : EntityBase, ITenantScoped
{
    public int UserId { get; set; }
    public User? User { get; set; }

    public int? CompanyId { get; set; }
    public Company? Company { get; set; }

    public int? BranchId { get; set; }
    public Branch? Branch { get; set; }

    public AssignmentRole Role { get; set; }
    public bool IsActive { get; set; } = true;
}
