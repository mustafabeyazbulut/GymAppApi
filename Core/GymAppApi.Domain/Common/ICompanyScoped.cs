// Entities that always belong to exactly one Company (non-nullable FK) — the
// global query filter applies "WHERE CompanyId = @ambient" unconditionally.
namespace GymAppApi.Domain.Common;

public interface ICompanyScoped
{
    int CompanyId { get; set; }
}
