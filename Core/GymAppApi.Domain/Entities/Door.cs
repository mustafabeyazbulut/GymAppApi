using GymAppApi.Domain.Common;

namespace GymAppApi.Domain.Entities;

public class Door : EntityBase, ICompanyScoped
{
    public int ZoneId { get; set; }
    public Zone? Zone { get; set; }

    // Zone.CompanyId'nin kayıt anındaki anlık görüntüsü - ClassEnrollment'ın
    // ClassSession.CompanyId'yi snapshot'lamasıyla aynı desen.
    public int CompanyId { get; set; }

    public string Name { get; set; } = null!;

    // Donanım seçilene kadar boş/placeholder JSON - hangi IDoorAccessProvider
    // implementasyonunun ve donanıma özel ayarların (ör. cihaz ID'si)
    // kullanılacağını taşıyacak.
    public string? ProviderConfig { get; set; }
}
