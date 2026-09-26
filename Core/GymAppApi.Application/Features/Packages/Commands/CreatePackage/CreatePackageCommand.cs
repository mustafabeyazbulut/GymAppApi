using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Packages.Commands.CreatePackage;

public class CreatePackageCommand : IRequest<CreatePackageCommandResult>
{
    public int CompanyId { get; set; }
    // Zorunlu (senaryo §10.5: firma geneli paket yok). Nullable tipi sadece
    // eksik alanın validator tarafından yerelleştirilmiş hatayla
    // reddedilebilmesi için; Package.BranchId kolonu da eski kayıtlar için
    // şimdilik nullable.
    public int? BranchId { get; set; }

    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public PackageType Type { get; set; }
    public int? DurationDays { get; set; }
    public int? SessionCount { get; set; }
    public decimal Price { get; set; }
    // null = dondurma süresi sınırsız.
    public int? MaxFreezeDays { get; set; }

    // Zorunlu, en az bir: paketin kapsadığı hizmetler. Hepsi paketin şubesine
    // ait ve aktif olmalı (handler'da kontrol).
    public List<int>? ServiceIds { get; set; }

    // Set by the controller from the caller's own JWT sub claim.
    public int RequestedByUserId { get; set; }
}
