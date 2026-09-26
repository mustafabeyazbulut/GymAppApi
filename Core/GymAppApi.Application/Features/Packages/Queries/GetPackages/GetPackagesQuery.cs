using MediatR;

namespace GymAppApi.Application.Features.Packages.Queries.GetPackages;

public class GetPackagesQuery : IRequest<IReadOnlyList<PackageDto>>
{
    // true: sadece aktif paketler (ör. "Paket Tanımla" / atama listesi).
    // false (varsayılan): yöneticiler (GymAdmin/BranchManager) pasifleri de
    // IsActive alanıyla görür - yönetim listesi.
    public bool ActiveOnly { get; set; }
}
