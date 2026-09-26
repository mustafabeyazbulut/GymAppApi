// Bir firmaya ait OLABİLEN ya da platform geneli (CompanyId = null) olan
// varlıklar (ContentItem: genel içerik). Global query filter, ICompanyScoped
// ile aynı şekilde sadece ambient firmanın satırlarını gösterir - platform
// satırları filtrede GİZLİ kalır ve bilinçli olarak IgnoreQueryFilters +
// CompanyId == null ile okunur. SaveChanges'teki otomatik CompanyId damgası
// bu varlıklara uygulanmaz (platform satırı bir firmaya damgalanmasın).
namespace GymAppApi.Domain.Common;

public interface IOptionalCompanyScoped
{
    int? CompanyId { get; set; }
}
