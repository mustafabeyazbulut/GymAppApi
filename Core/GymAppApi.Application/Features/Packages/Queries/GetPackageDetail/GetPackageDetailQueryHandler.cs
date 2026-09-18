using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Packages.Queries.GetPackages;
using GymAppApi.Domain.Entities;
using MediatR;

namespace GymAppApi.Application.Features.Packages.Queries.GetPackageDetail;

public class GetPackageDetailQueryHandler : IRequestHandler<GetPackageDetailQuery, PackageDto>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetPackageDetailQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<PackageDto> Handle(GetPackageDetailQuery request, CancellationToken cancellationToken)
    {
        var package = await _unitOfWork.GetReadRepository<Package>()
            .GetAsync(p => p.Id == request.PackageId, cancellationToken: cancellationToken);
        if (package is null)
        {
            throw new NotFoundException($"Paket {request.PackageId} bulunamadı.");
        }

        return GetPackagesQueryHandler.ToDto(package);
    }
}
