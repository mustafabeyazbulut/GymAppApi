using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Packages.Commands.CreatePackage;

public class CreatePackageCommand : IRequest<CreatePackageCommandResult>
{
    public int CompanyId { get; set; }
    // null = valid at every branch of the company - only a GymAdmin/SuperAdmin
    // may create one of these; a BranchManager must supply their own branch.
    public int? BranchId { get; set; }

    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public PackageType Type { get; set; }
    public int? DurationDays { get; set; }
    public int? SessionCount { get; set; }
    public decimal Price { get; set; }
    // null = dondurma süresi sınırsız.
    public int? MaxFreezeDays { get; set; }

    // Set by the controller from the caller's own JWT sub claim.
    public int RequestedByUserId { get; set; }
}
