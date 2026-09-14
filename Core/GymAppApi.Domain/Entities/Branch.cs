using GymAppApi.Domain.Common;

namespace GymAppApi.Domain.Entities;

public class Branch : EntityBase, ICompanyScoped, IDeactivatable
{
    public int CompanyId { get; set; }
    public Company? Company { get; set; }

    public string Name { get; set; } = null!;
    public string Address { get; set; } = null!;
    public bool IsActive { get; set; } = true;
}
