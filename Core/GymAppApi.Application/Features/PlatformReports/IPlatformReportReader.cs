using GymAppApi.Domain.Enums;

namespace GymAppApi.Application.Features.PlatformReports;

// Platform raporlarının ham verisi - her kalem TEK toplu (GroupBy/projeksiyon)
// sorguyla okunur, firma/şube başına ayrı sorgu (N+1) yoktur. Tüm okumalar
// filtresiz (IgnoreQueryFilters): Sistem Sahibi'nin tenant bağlamı yok ve
// rapor bilinçli olarak tüm firmaları kapsar (uç SuperAdminOnly).
public interface IPlatformReportReader
{
    // companyId verilirse sadece o firmanın satırları okunur (şube kırılımı).
    Task<PlatformReportData> ReadAsync(DateTime fromUtc, DateTime nowUtc, int? companyId, CancellationToken cancellationToken);
}

public sealed record PlatformReportData(
    int TotalUsers,
    IReadOnlyList<DateTime> NewUserCreatedAts,
    IReadOnlyList<CompanyRow> Companies,
    IReadOnlyList<BranchRow> Branches,
    IReadOnlyList<AssignmentCountRow> AssignmentCounts,
    IReadOnlyList<ActiveMemberRow> ActiveMembers,
    IReadOnlyList<ScopedCountRow> Sales,
    IReadOnlyList<ScopedAmountRow> Revenue);

public sealed record CompanyRow(int Id, string Name, bool IsActive);

public sealed record BranchRow(int Id, int CompanyId, string Name, bool IsActive);

// Aktif personel atamalarının (firma, şube, rol) başına sayısı.
public sealed record AssignmentCountRow(int CompanyId, int? BranchId, AssignmentRole Role, int Count);

// Geçerli paketi olan (firma, şube, üye) üçlüleri - tekil üye sayımı buradan.
public sealed record ActiveMemberRow(int CompanyId, int? BranchId, int MemberUserId);

public sealed record ScopedCountRow(int CompanyId, int? BranchId, int Count);

public sealed record ScopedAmountRow(int CompanyId, int? BranchId, decimal Amount);
