using GymAppApi.Domain.Common;
using GymAppApi.Domain.Enums;

namespace GymAppApi.Domain.Entities;

public class PackageAssignment : EntityBase, ICompanyScoped
{
    public int PackageId { get; set; }
    public Package? Package { get; set; }

    public int MemberUserId { get; set; }
    public User? MemberUser { get; set; }

    // Snapshot of Package.CompanyId/BranchId at confirmation time - if the
    // Package is retired or changed later, this assignment's own scope stays
    // exactly as it was when granted.
    public int CompanyId { get; set; }
    public Company? Company { get; set; }
    public int? BranchId { get; set; }
    public Branch? Branch { get; set; }

    public int AssignedByUserId { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    // Copied from Package.SessionCount - informational only in this module,
    // does not decrement (no check-in system yet).
    public int? RemainingSessions { get; set; }

    public PackageAssignmentStatus Status { get; set; } = PackageAssignmentStatus.Active;
    // Sadece Status == Frozen iken set edilir. Unfreeze'de EndDate
    // (now - FrozenAt) kadar ileri itilir ve bu tekrar null'a döner.
    public DateTime? FrozenAt { get; set; }

    // Bugüne kadar bu atamada birden fazla dondurma döngüsünde kullanılmış
    // toplam gün sayısı - Package.MaxFreezeDays karşısında biriktirilir,
    // her yeni freeze/unfreeze döngüsünde sıfırlanmaz (aksi hâlde üye
    // sürekli dondurup açarak sınırı aşabilirdi).
    public int TotalFrozenDays { get; set; }
}
