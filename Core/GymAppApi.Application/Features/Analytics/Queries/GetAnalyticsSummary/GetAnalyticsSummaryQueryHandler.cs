using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.Analytics.Queries.GetAnalyticsSummary;

// Salt-okunur, toplama (aggregate) niteliğinde bir raporlama sorgusu - yeni
// bir veri modeli gerektirmez (bkz.
// docs/superpowers/specs/2026-09-20-analytics-design.md). Kapsam tamamen
// ambient ITenantContext'ten gelir:
//   - Sistem Sahibi: bu uca erişemez (StaffManagement, senaryo §10.6).
//   - Gym Admin: ambient BranchId null -> global query filter sadece
//     CompanyId'ye göre daraltır, şube bazında ek bir kısıtlama YOK -> kendi
//     şirketinin tüm şubeleri.
//   - Şube Yöneticisi: ambient BranchId set -> global filtreye (CompanyId) EK
//     olarak burada elle BranchId filtresi uygulanır -> sadece kendi şubesi.
// Her sorgu TEK bir toplu (bulk) sorgu - N+1 yok, GetClassSessionsQuery'nin
// "tüm oturumlar için tek sorgu + bellek içinde gruplama" desenini izler.
public class GetAnalyticsSummaryQueryHandler : IRequestHandler<GetAnalyticsSummaryQuery, AnalyticsSummaryDto>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;

    public GetAnalyticsSummaryQueryHandler(IUnitOfWork unitOfWork, ITenantContext tenantContext)
    {
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
    }

    public async Task<AnalyticsSummaryDto> Handle(GetAnalyticsSummaryQuery request, CancellationToken cancellationToken)
    {
        var branchId = _tenantContext.BranchId;
        var now = DateTime.UtcNow;
        var thirtyDaysAgo = now.AddDays(-30);
        var today = DateOnly.FromDateTime(now);
        var periodStart = today.AddDays(-30);

        // --- Metrik 1 (aktif üye sayısı) + Metrik 4'ün (antrenör başına
        // aktif öğrenci) ortak girdisi: Status == Active PackageAssignment'lar. ---
        var activeAssignments = await _unitOfWork.GetReadRepository<PackageAssignment>().GetAllAsync(
            pa => pa.Status == PackageAssignmentStatus.Active && (branchId == null || pa.BranchId == branchId),
            cancellationToken: cancellationToken);

        var activeMemberMetric = BuildActiveMemberMetric(activeAssignments, now, thirtyDaysAgo);
        var currentActiveMemberIds = activeAssignments
            .Where(pa => pa.EndDate == null || pa.EndDate > now)
            .Select(pa => pa.MemberUserId)
            .ToHashSet();

        // --- Metrik 2 (ders doluluk oranı) + Metrik 4'ün (antrenör-üye
        // ilişkisi) ortak girdisi: ClassSession + ClassEnrollment. ---
        var allClassSessions = await _unitOfWork.GetReadRepository<ClassSession>().GetAllAsync(
            cs => branchId == null || cs.BranchId == branchId,
            cancellationToken: cancellationToken);

        var allSessionIds = allClassSessions.Select(cs => cs.Id).ToList();
        var activeEnrollments = allSessionIds.Count == 0
            ? Array.Empty<ClassEnrollment>()
            : await _unitOfWork.GetReadRepository<ClassEnrollment>().GetAllAsync(
                e => allSessionIds.Contains(e.ClassSessionId) &&
                     (e.Status == ClassEnrollmentStatus.Reserved || e.Status == ClassEnrollmentStatus.Attended),
                cancellationToken: cancellationToken);

        var enrollmentsBySessionId = activeEnrollments.GroupBy(e => e.ClassSessionId).ToDictionary(g => g.Key, g => g.ToList());

        var periodSessions = allClassSessions.Where(cs => cs.Date >= periodStart && cs.Date <= today).ToList();
        var classOccupancy = BuildClassOccupancyMetric(periodSessions, enrollmentsBySessionId);

        // --- Metrik 3 (paket satış sayısı, son 30 gün) ---
        var newAssignments = await _unitOfWork.GetReadRepository<PackageAssignment>().GetAllAsync(
            pa => pa.CreatedAt >= thirtyDaysAgo && (branchId == null || pa.BranchId == branchId),
            include: q => q.Include(pa => pa.Package),
            cancellationToken: cancellationToken);

        var packageSales = BuildPackageSalesMetric(newAssignments);

        // --- Metrik 4 (antrenör başına aktif öğrenci sayısı) ---
        // Zaman penceresi yok (spec'te bu metrik için "son 30 gün" belirtilmemiş,
        // sadece "aktif PackageAssignment'ı olan üye" şartı var) - Reservation
        // ve ClassSession TÜM zamanları kapsar. Çok yüksek hacimli bir
        // kiracıda bu sorgunun büyümesi ileride ayrı bir iyileştirme konusu
        // olabilir, ama spec'in kapsamı bunu şu an gerektirmiyor.
        var reservations = await _unitOfWork.GetReadRepository<Reservation>().GetAllAsync(
            r => branchId == null || r.BranchId == branchId,
            cancellationToken: cancellationToken);

        var trainerActiveStudents = await BuildTrainerActiveStudentsAsync(
            allClassSessions, enrollmentsBySessionId, reservations, currentActiveMemberIds, cancellationToken);

        return new AnalyticsSummaryDto
        {
            ActiveMembers = activeMemberMetric,
            ClassOccupancy = classOccupancy,
            PackageSales = packageSales,
            TrainerActiveStudents = trainerActiveStudents,
        };
    }

    private static ActiveMemberMetricDto BuildActiveMemberMetric(
        IReadOnlyList<PackageAssignment> activeAssignments, DateTime now, DateTime thirtyDaysAgo)
    {
        var currentCount = activeAssignments
            .Where(pa => pa.EndDate == null || pa.EndDate > now)
            .Select(pa => pa.MemberUserId)
            .Distinct()
            .Count();

        // Not: PackageAssignment'ın geçmiş durum geçmişi tutulmuyor (audit log
        // yok) - "30 gün önce aktifti" sorgusu SADECE hâlâ Status == Active
        // olan atamalar üzerinden, o tarihte StartDate/EndDate penceresine
        // girip girmediğine bakarak yaklaşık hesaplanır. 30 gün içinde iptal
        // edilmiş bir atama bu trendde "30 gün önce de aktif değildi" gibi
        // görünür - basit bir raporlama ekranı için kabul edilebilir bir
        // yaklaşıklık.
        var countThirtyDaysAgo = activeAssignments
            .Where(pa => pa.StartDate <= thirtyDaysAgo && (pa.EndDate == null || pa.EndDate > thirtyDaysAgo))
            .Select(pa => pa.MemberUserId)
            .Distinct()
            .Count();

        return new ActiveMemberMetricDto
        {
            CurrentCount = currentCount,
            CountThirtyDaysAgo = countThirtyDaysAgo,
            TrendPercentage = countThirtyDaysAgo == 0
                ? null
                : Math.Round((currentCount - countThirtyDaysAgo) * 100m / countThirtyDaysAgo, 1),
        };
    }

    private static ClassOccupancyMetricDto BuildClassOccupancyMetric(
        IReadOnlyList<ClassSession> periodSessions, IReadOnlyDictionary<int, List<ClassEnrollment>> enrollmentsBySessionId)
    {
        if (periodSessions.Count == 0)
        {
            return new ClassOccupancyMetricDto { OverallOccupancyRate = 0, ClassBreakdown = Array.Empty<ClassOccupancyBreakdownDto>() };
        }

        var breakdown = periodSessions
            .GroupBy(cs => cs.Name)
            .Select(g =>
            {
                var totalCapacity = g.Sum(cs => cs.Capacity);
                var totalEnrolled = g.Sum(cs => enrollmentsBySessionId.GetValueOrDefault(cs.Id)?.Count ?? 0);
                return new ClassOccupancyBreakdownDto
                {
                    ClassName = g.Key,
                    SessionCount = g.Count(),
                    TotalCapacity = totalCapacity,
                    TotalEnrolled = totalEnrolled,
                    OccupancyRate = totalCapacity == 0 ? 0 : Math.Round(totalEnrolled / (decimal)totalCapacity, 4),
                };
            })
            .OrderByDescending(x => x.OccupancyRate)
            .ToList();

        var overallCapacity = breakdown.Sum(x => x.TotalCapacity);
        var overallEnrolled = breakdown.Sum(x => x.TotalEnrolled);

        return new ClassOccupancyMetricDto
        {
            OverallOccupancyRate = overallCapacity == 0 ? 0 : Math.Round(overallEnrolled / (decimal)overallCapacity, 4),
            ClassBreakdown = breakdown,
        };
    }

    private static PackageSalesMetricDto BuildPackageSalesMetric(IReadOnlyList<PackageAssignment> newAssignments)
    {
        var breakdown = newAssignments
            .GroupBy(pa => pa.Package?.Category)
            .Select(g => new PackageSalesCategoryBreakdownDto { Category = g.Key?.ToString(), Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToList();

        return new PackageSalesMetricDto { TotalCount = newAssignments.Count, CategoryBreakdown = breakdown };
    }

    private async Task<IReadOnlyList<TrainerActiveStudentDto>> BuildTrainerActiveStudentsAsync(
        IReadOnlyList<ClassSession> allClassSessions,
        IReadOnlyDictionary<int, List<ClassEnrollment>> enrollmentsBySessionId,
        IReadOnlyList<Reservation> reservations,
        HashSet<int> currentActiveMemberIds,
        CancellationToken cancellationToken)
    {
        var trainerMemberMap = new Dictionary<int, HashSet<int>>();

        void AddRelation(int trainerUserId, int memberUserId)
        {
            if (!trainerMemberMap.TryGetValue(trainerUserId, out var members))
            {
                members = new HashSet<int>();
                trainerMemberMap[trainerUserId] = members;
            }

            members.Add(memberUserId);
        }

        foreach (var session in allClassSessions)
        {
            if (!enrollmentsBySessionId.TryGetValue(session.Id, out var enrollments))
            {
                continue;
            }

            foreach (var enrollment in enrollments)
            {
                AddRelation(session.TrainerUserId, enrollment.MemberUserId);
            }
        }

        foreach (var reservation in reservations)
        {
            AddRelation(reservation.TrainerId, reservation.MemberUserId);
        }

        if (trainerMemberMap.Count == 0)
        {
            return Array.Empty<TrainerActiveStudentDto>();
        }

        var trainerIds = trainerMemberMap.Keys.ToList();
        var trainers = await _unitOfWork.GetReadRepository<User>().GetAllAsync(
            u => trainerIds.Contains(u.Id), cancellationToken: cancellationToken);
        var trainerNameById = trainers.ToDictionary(u => u.Id, u => u.FullName);

        return trainerMemberMap
            .Select(kvp => new TrainerActiveStudentDto
            {
                TrainerUserId = kvp.Key,
                TrainerFullName = trainerNameById.GetValueOrDefault(kvp.Key, string.Empty),
                ActiveStudentCount = kvp.Value.Count(memberId => currentActiveMemberIds.Contains(memberId)),
            })
            .OrderByDescending(dto => dto.ActiveStudentCount)
            .ToList();
    }
}
