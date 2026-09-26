using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.PackageAssignments;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.ClassScheduling.Queries.GetClassSessions;

// Herkes çağırabilir ([Authorize]) - bir Member'ın ambient CompanyId'si yok,
// bu yüzden IgnoreQueryFilters + kendi görünürlük kapsamımızı burada
// uyguluyoruz (CreateReservationCommandHandler'daki standing rule). Kapsam
// (senaryo §10.2 ve §10.8 - hiçbir liste ilişkisi olmayan firma/şubenin
// verisini döndürmez) iki kaynağın birleşimi:
//   - Personel kapsamı: ambient tenant context (GymAdmin -> firmanın tüm
//     şubeleri, BranchManager/Trainer -> sadece kendi şubesi).
//   - Üye kapsamı: çağıranın kendi GEÇERLİ paketlerinin firma/şubesi.
// İkisi de yoksa boş liste döner. SuperAdmin bypass'ı mevcut davranış olarak
// korunuyor (kaldırılması ayrı bir adım, senaryo §10.6).
public class GetClassSessionsQueryHandler : IRequestHandler<GetClassSessionsQuery, IReadOnlyList<ClassSessionDto>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;

    public GetClassSessionsQueryHandler(IUnitOfWork unitOfWork, ITenantContext tenantContext)
    {
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
    }

    public async Task<IReadOnlyList<ClassSessionDto>> Handle(GetClassSessionsQuery request, CancellationToken cancellationToken)
    {
        var isSuperAdmin = _tenantContext.IsSuperAdmin;
        var scopes = isSuperAdmin
            ? new List<(int CompanyId, int? BranchId)>()
            : await GetVisibleScopesAsync(request.RequestedByUserId, cancellationToken);

        if (!isSuperAdmin && scopes.Count == 0)
        {
            return Array.Empty<ClassSessionDto>();
        }

        // Firma daraltması SQL'de, şube eşleşmesi (kapsam başına farklı
        // olabildiği için) aşağıda bellek içinde yapılıyor.
        var companyIds = scopes.Select(s => s.CompanyId).Distinct().ToList();
        var sessions = await _unitOfWork.GetReadRepository<ClassSession>().GetAllAsync(
            s => (isSuperAdmin || companyIds.Contains(s.CompanyId)) &&
                 (request.BranchId == null || s.BranchId == request.BranchId) &&
                 (request.From == null || s.Date >= request.From) &&
                 (request.To == null || s.Date <= request.To),
            include: q => q.IgnoreQueryFilters().Include(s => s.Branch),
            orderBy: q => q.OrderBy(s => s.Date).ThenBy(s => s.StartTime),
            cancellationToken: cancellationToken);

        if (!isSuperAdmin)
        {
            sessions = sessions
                .Where(s => scopes.Any(scope =>
                    scope.CompanyId == s.CompanyId && (scope.BranchId == null || scope.BranchId == s.BranchId)))
                .ToList();
        }

        if (sessions.Count == 0)
        {
            return Array.Empty<ClassSessionDto>();
        }

        // Doluluk sayısını her ders için ayrı bir sorguyla değil, TÜM
        // oturumlar için TEK bir sorguyla toplu çekip bellek içinde
        // grupluyoruz - N+1 yok (master spec'in performans notu).
        var sessionIds = sessions.Select(s => s.Id).ToList();
        var activeEnrollments = await _unitOfWork.GetReadRepository<ClassEnrollment>().GetAllAsync(
            e => sessionIds.Contains(e.ClassSessionId) &&
                 (e.Status == ClassEnrollmentStatus.Reserved || e.Status == ClassEnrollmentStatus.Attended),
            include: q => q.IgnoreQueryFilters().Include(e => e.ClassSession),
            cancellationToken: cancellationToken);

        var enrolledCountsBySessionId = activeEnrollments
            .GroupBy(e => e.ClassSessionId)
            .ToDictionary(g => g.Key, g => g.Count());

        return sessions.Select(s => new ClassSessionDto
        {
            Id = s.Id,
            BranchId = s.BranchId,
            TrainerUserId = s.TrainerUserId,
            Category = s.Category.ToString(),
            Name = s.Name,
            Date = s.Date,
            StartTime = s.StartTime,
            EndTime = s.EndTime,
            Capacity = s.Capacity,
            EnrolledCount = enrolledCountsBySessionId.GetValueOrDefault(s.Id),
            CancellationCutoffHours = s.CancellationCutoffHours,
        }).ToList();
    }

    // (CompanyId, BranchId) listesi - BranchId null = o firmanın tüm şubeleri.
    private async Task<List<(int CompanyId, int? BranchId)>> GetVisibleScopesAsync(int userId, CancellationToken cancellationToken)
    {
        var scopes = new List<(int CompanyId, int? BranchId)>();

        if (_tenantContext.CompanyId is int staffCompanyId)
        {
            scopes.Add((staffCompanyId, _tenantContext.BranchId));
        }

        // Ortak "geçerli paket" tanımı (PackageAssignmentValidity).
        // IgnoreQueryFilters: bir Member'ın ambient CompanyId'si yok, filtre
        // kendi paketlerini ondan gizlerdi - sorgu zaten çağıranın kendi
        // satırlarına (MemberUserId) sabitlendiği için sızıntı riski yok.
        var validPackageAssignments = await _unitOfWork.GetReadRepository<PackageAssignment>().GetAllAsync(
            PackageAssignmentValidity.UsableOwnedBy(userId, DateTime.UtcNow),
            include: q => q.IgnoreQueryFilters().Include(pa => pa.Package),
            cancellationToken: cancellationToken);

        scopes.AddRange(validPackageAssignments.Select(pa => (pa.CompanyId, pa.BranchId)));
        return scopes;
    }
}
