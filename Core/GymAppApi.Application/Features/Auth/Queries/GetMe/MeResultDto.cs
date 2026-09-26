namespace GymAppApi.Application.Features.Auth.Queries.GetMe;

public class MeResultDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = null!;
    public string Phone { get; set; } = null!;
    public string? Email { get; set; }
    public string PreferredLanguage { get; set; } = null!;
    public bool IsAccountFrozen { get; set; }
    public IReadOnlyList<MeAssignmentDto> Assignments { get; set; } = new List<MeAssignmentDto>();
    public IReadOnlyList<MePackageAssignmentDto> PackageAssignments { get; set; } = new List<MePackageAssignmentDto>();
}

public class MeAssignmentDto
{
    // Mobilin X-Active-Assignment-Id header'ında göndereceği değer.
    public int Id { get; set; }
    // Nullable: a SuperAdmin assignment is platform-wide (Assignment.CompanyId
    // is null by design for that role) — coercing null to 0 would make a
    // SuperAdmin's own profile indistinguishable from a data-integrity bug.
    public int? CompanyId { get; set; }
    public string? CompanyName { get; set; }
    public int? BranchId { get; set; }
    // Mobilde aktif rol seçicisinde şube etiketi için; GymAdmin (şubesiz) ve
    // SuperAdmin atamalarında null.
    public string? BranchName { get; set; }
    public string Role { get; set; } = null!;
}

public class MePackageAssignmentDto
{
    // The mobile client needs the assignment's own id (not just the
    // Package's) to call /api/package-assignments/{id}/... (payments,
    // reservations, check-ins) for the right row when a Member holds more
    // than one PackageAssignment.
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string? CompanyName { get; set; }
    public int? BranchId { get; set; }
    public int PackageId { get; set; }
    public string? PackageName { get; set; }
    // null = bu paket hiçbir grup dersine uygun değil (ör. 1:1 PT paketi) -
    // mobil taraf "Katıl" butonunu bu ders kategorisiyle eşleşen bir paket
    // yoksa devre dışı bırakmak için kullanır (bkz. Package.Category,
    // ClassScheduling modülü).
    public string? Category { get; set; }
    public decimal Price { get; set; }
    public string Status { get; set; } = null!;
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int? SessionCount { get; set; }
    public int? RemainingSessions { get; set; }
    // null = dondurma süresi sınırsız - üyenin kendi paketini dondururken
    // ne kadar hakkı kaldığını görebilmesi için (bkz. Package.MaxFreezeDays).
    public int? MaxFreezeDays { get; set; }
    public int TotalFrozenDays { get; set; }
}
