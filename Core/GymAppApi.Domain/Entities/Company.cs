using GymAppApi.Domain.Common;

namespace GymAppApi.Domain.Entities;

public class Company : EntityBase, IDeactivatable
{
    public string Name { get; set; } = null!;
    public bool IsActive { get; set; } = true;

    public ICollection<Branch> Branches { get; set; } = new List<Branch>();
}
