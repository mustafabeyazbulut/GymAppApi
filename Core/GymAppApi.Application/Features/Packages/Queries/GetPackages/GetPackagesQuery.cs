using MediatR;

namespace GymAppApi.Application.Features.Packages.Queries.GetPackages;

public class GetPackagesQuery : IRequest<IReadOnlyList<PackageDto>>
{
}
