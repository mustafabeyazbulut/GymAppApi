using GymAppApi.Domain.Common;

namespace GymAppApi.Domain.Entities;

// Şubenin sunduğu hizmet (Pilates, Kickbox, PT...). Paketler hangi
// hizmetleri kapsadığını, dersler ve randevular hangi hizmete ait olduğunu
// bununla belirtir - katılım uygunluğu hizmet eşleşmesine göre (senaryo §5.3).
// Ad şube içinde tekil. Soft close: pasif hizmet yeni paket/ders için
// seçilemez ama geçmiş kayıtlar bozulmaz.
public class Service : EntityBase, ICompanyScoped, IDeactivatable
{
    public int CompanyId { get; set; }
    public Company? Company { get; set; }

    public int BranchId { get; set; }
    public Branch? Branch { get; set; }

    public string Name { get; set; } = null!;
    public bool IsActive { get; set; } = true;
}
