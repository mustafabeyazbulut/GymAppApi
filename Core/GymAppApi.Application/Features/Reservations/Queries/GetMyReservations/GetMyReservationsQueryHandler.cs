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

    public GetMyReservationsQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<IReadOnlyList<MyReservationDto>> Handle(GetMyReservationsQuery request, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters: bir antrenörün ambient CompanyId'si (kendi
        // Assignment'ından çözülür) sadece TEK bir şirkete işaret eder, ama
        // bir antrenör birden fazla şubede/şirkette çalışıyor olabilir -
        // filtre olmadan bu sorgu o antrenörün diğer şirketlerdeki
        // rezervasyonlarını sessizce gizlerdi. Güvenli, çünkü tek gerçek
        // yetki kontrolü zaten r.TrainerId == caller eşleşmesi.
        var reservations = await _unitOfWork.GetReadRepository<Reservation>().GetAllAsync(
            r => r.TrainerId == request.TrainerUserId,
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
