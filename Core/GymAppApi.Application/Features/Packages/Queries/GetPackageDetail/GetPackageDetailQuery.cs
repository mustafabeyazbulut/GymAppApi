using GymAppApi.Application.Features.Packages.Queries.GetPackages;
using MediatR;

namespace GymAppApi.Application.Features.Packages.Queries.GetPackageDetail;

public class GetPackageDetailQuery : IRequest<PackageDto>
{
    public GetPackageDetailQuery(int packageId) => PackageId = packageId;

    public int PackageId { get; }
}
