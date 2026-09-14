using GymAppApi.Domain.Common;

namespace GymAppApi.Domain.Entities;

public class AuditLog : EntityBase, ITenantScoped
{
    public int? CompanyId { get; set; }
    public int? BranchId { get; set; }

    public int? ActorAssignmentId { get; set; }
    public string Action { get; set; } = null!;
    public string EntityType { get; set; } = null!;
    public int EntityId { get; set; }
    public string? DetailsJson { get; set; }
}
