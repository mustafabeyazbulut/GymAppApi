using GymAppApi.Domain.Entities;

namespace GymAppApi.Application.Features.Services;

public class ServiceDto
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public string Name { get; set; } = null!;
    public bool IsActive { get; set; }

    public static ServiceDto From(Service service) => new()
    {
        Id = service.Id,
        BranchId = service.BranchId,
        Name = service.Name,
        IsActive = service.IsActive,
    };
}
