// Entities whose tenant scope can legitimately be null (Assignment: a Super
// Admin's assignment has no CompanyId/BranchId; AuditLog: a platform-level
// action may have no branch). The query filter treats null as "not scoped
// at that level" rather than "belongs to nobody".
namespace GymAppApi.Domain.Common;

public interface ITenantScoped
{
    int? CompanyId { get; set; }
    int? BranchId { get; set; }
}
