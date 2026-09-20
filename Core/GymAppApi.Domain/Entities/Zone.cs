using GymAppApi.Domain.Common;

namespace GymAppApi.Domain.Entities;

// Kapı Erişim Sistemi'nin yazılım iskeleti - gerçek donanım seçilmeden ÖNCE
// kurulabilecek kısım (veri modeli + yönetim ekranı). Hiçbir kod bir
// AccessLog OLUŞTURMUYOR (bkz.
// docs/superpowers/specs/2026-09-20-door-access-skeleton-design.md).
public class Zone : EntityBase, ICompanyScoped
{
    public int CompanyId { get; set; }
    public Company? Company { get; set; }

    public int BranchId { get; set; }
    public Branch? Branch { get; set; }

    public string Name { get; set; } = null!;

    public int CreatedByUserId { get; set; }
}
