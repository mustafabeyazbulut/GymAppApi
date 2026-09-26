using MediatR;

namespace GymAppApi.Application.Features.Services.Queries.GetBranchServices;

public class GetBranchServicesQuery : IRequest<IReadOnlyList<ServiceDto>>
{
    public int BranchId { get; set; }

    // Sadece şubeyi yöneten personel (GymAdmin / o şubenin BranchManager'ı)
    // için geçerli; diğerleri her zaman sadece aktif hizmetleri görür.
    public bool IncludeInactive { get; set; }

    // Çağıranın kendi JWT sub claim'inden controller tarafından set edilir.
    public int RequestedByUserId { get; set; }
}
