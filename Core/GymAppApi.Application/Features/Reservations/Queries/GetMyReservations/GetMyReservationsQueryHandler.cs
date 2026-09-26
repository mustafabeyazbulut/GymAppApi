using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.Reservations.Queries.GetMyReservations;

// Bir antrenörün "bugün/bu hafta programım ne?" sorusunu cevaplayabilmesi
// için - daha önce bunun hiçbir karşılığı yoktu, sadece belirli bir
// PackageAssignmentId biliniyorsa GetPackageAssignmentReservationsQuery
// üzerinden tek tek bakılabiliyordu. Burada yetki kontrolü için ayrı bir
// Assignment sorgusu YOK - r.TrainerId == caller filtresi zaten
// kendiliğinden çağıranın SADECE kendi rezervasyonlarını görmesini sağlıyor,
// tıpkı bir Member'ın kendi PackageAssignment'ını görmesi gibi.
public class GetMyReservationsQueryHandler : IRequestHandler<GetMyReservationsQuery, IReadOnlyList<MyReservationDto>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;

    public GetMyReservationsQueryHandler(IUnitOfWork unitOfWork, ITenantContext tenantContext)
    {
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
    }

    public async Task<IReadOnlyList<MyReservationDto>> Handle(GetMyReservationsQuery request, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters + elle kapsam: antrenör birden fazla şubede/
        // firmada çalışabilir; liste AKTİF atamanın (X-Active-Assignment-Id)
        // firması/şubesiyle sınırlanır - A1'de antrenör olarak bakan biri
        // A2'deki rezervasyonlarını görmez, rol değiştirince görür. Aktif
        // personel bağlamı yoksa kapsam daraltılmaz; tek gerçek yetki kontrolü
        // yine r.TrainerId == caller eşleşmesi.
        var companyId = _tenantContext.CompanyId;
        var branchId = _tenantContext.BranchId;
        var reservations = await _unitOfWork.GetReadRepository<Reservation>().GetAllAsync(
            r => r.TrainerId == request.TrainerUserId &&
                 (companyId == null || r.CompanyId == companyId) &&
                 (branchId == null || r.BranchId == branchId),
            include: q => q.IgnoreQueryFilters()
                .Include(r => r.PackageAssignment!).ThenInclude(pa => pa!.MemberUser),
            cancellationToken: cancellationToken);

        return reservations
            .OrderBy(r => r.ScheduledAt)
            .Select(r => new MyReservationDto
            {
                Id = r.Id,
                PackageAssignmentId = r.PackageAssignmentId,
                MemberUserId = r.MemberUserId,
                MemberFullName = r.PackageAssignment?.MemberUser?.FullName,
                CompanyId = r.CompanyId,
                BranchId = r.BranchId,
                ScheduledAt = r.ScheduledAt,
                Status = r.Status.ToString(),
                QrCode = r.QrCode,
            })
            .ToList();
    }
}
