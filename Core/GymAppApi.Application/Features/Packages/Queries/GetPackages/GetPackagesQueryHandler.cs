using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using MediatR;

namespace GymAppApi.Application.Features.Packages.Queries.GetPackages;

public class GetPackagesQueryHandler : IRequestHandler<GetPackagesQuery, IReadOnlyList<PackageDto>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetPackagesQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<IReadOnlyList<PackageDto>> Handle(GetPackagesQuery request, CancellationToken cancellationToken)
    {
        var packages = await _unitOfWork.GetReadRepository<Package>().GetAllAsync(cancellationToken: cancellationToken);

        return packages.Select(ToDto).ToList();
    }

    internal static PackageDto ToDto(Package p) => new()
    {
        Id = p.Id,
        CompanyId = p.CompanyId,
        BranchId = p.BranchId,
        Name = p.Name,
        Description = p.Description,
        Type = p.Type.ToString(),
        DurationDays = p.DurationDays,
        SessionCount = p.SessionCount,
        Price = p.Price,
        AccessTier = p.AccessTier.ToString(),
        IsActive = p.IsActive,
    };
}
