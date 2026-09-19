using GymAppApi.Domain.Common;
using GymAppApi.Domain.Enums;

namespace GymAppApi.Domain.Entities;

public class Package : EntityBase, ICompanyScoped, IDeactivatable
{
    public int CompanyId { get; set; }
    public Company? Company { get; set; }

    // null = valid at every branch of the company; set = valid only at this
    // one branch. Decided at template level, not per-assignment - see
    // .claude/memory/project-member-package-linkage-design.md.
    public int? BranchId { get; set; }
    public Branch? Branch { get; set; }

    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    public PackageType Type { get; set; }
    // Duration: required. SessionBased: optional upper bound ("60 gün içinde kullan").
    public int? DurationDays { get; set; }
    // SessionBased: required. Duration: always null.
    public int? SessionCount { get; set; }

    public decimal Price { get; set; }
    public PackageAccessTier AccessTier { get; set; } = PackageAccessTier.Standard;
    public bool IsActive { get; set; } = true;

    // null = dondurma süresi sınırsız. Set edilmişse, bu paketten atanan bir
    // PackageAssignment toplamda bu kadar günden fazla dondurulamaz (bkz.
    // PackageAssignment.TotalFrozenDays ve Freeze/UnfreezePackageAssignment
    // handler'ları) - bir GymAdmin'in paketi kendi kafasına göre, sınırsız
    // dondurulabilir şekilde tanımlamasını engeller.
    public int? MaxFreezeDays { get; set; }

    // null = bu paket hiçbir grup dersi/kapasiteli ders için geçerli değil
    // (ör. sadece 1:1 Reservation için kullanılan bir PT paketi). Set
    // edilmişse, bu paketten atanan bir PackageAssignment SADECE aynı
    // Category'deki ClassSession'lara kayıt olabilir (bkz.
    // EnrollInClassSessionCommandHandler ve
    // docs/superpowers/specs/2026-09-20-group-class-scheduling-design.md'deki
    // "Package.Category'si ClassSession.Category'sine uygun" kuralı). Not:
    // spec bu alanın zaten var olduğunu varsayıyordu, ama Package
    // entity'sinde hiç yoktu - grup dersi uygunluk eşleştirmesini yapabilmek
    // için buraya eklendi.
    public ClassSessionCategory? Category { get; set; }
}
