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

    // Tekillik anahtarı: Name'in baş/son boşluksuz, küçük harfli (invariant)
    // hali. DB'deki tekil indeks bunun üzerinde - "Yoga" ve "yoga" eşzamanlı
    // eklense bile ikisi birden kaydedilemez. Her zaman Normalize ile set edilir.
    public string NameNormalized { get; set; } = null!;

    public bool IsActive { get; set; } = true;

    public static string Normalize(string name) => name.Trim().ToLowerInvariant();

    public void Rename(string name)
    {
        Name = name.Trim();
        NameNormalized = Normalize(name);
    }
}
