namespace GymAppApi.Application.Features.ContentLibrary.Queries.GetContentItems;

public class ContentItemDto
{
    public int Id { get; set; }
    // null = genel (platform) içerik.
    public int? CompanyId { get; set; }

    // "Platform" (Sistem Sahibi'nin genel içeriği) | "Gym" (firmanın içeriği).
    public string Source { get; set; } = null!;
    public int? BranchId { get; set; }
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public string RequiredAccessTier { get; set; } = null!;
    public int MediaFileId { get; set; }
    public string MediaContentType { get; set; } = null!;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }

    // Sadece Member için anlamlı - staff her zaman true görür (spec:
    // "staff tümünü görür"). Liste, medya gerçekten indirilebilir olmasa
    // bile Premium içeriği Standard bir üyeye "kilitli" olarak gösterir
    // (upsell) - gerçek erişim kontrolü GET /api/media/{id}'de tekrar
    // yapılır, bu alan sadece UI'ın kilit ikonunu göstermesi için.
    public bool HasAccess { get; set; }
}
