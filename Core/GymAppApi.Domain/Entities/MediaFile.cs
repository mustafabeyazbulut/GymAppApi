using GymAppApi.Domain.Common;

namespace GymAppApi.Domain.Entities;

// KASITLI OLARAK ICompanyScoped DEĞİL - tenant izolasyonu onu kullanan
// ContentItem/ProgressNote üzerinden zaten sağlanıyor (bkz.
// docs/superpowers/specs/2026-09-20-content-library-design.md), bu sadece
// paylaşımlı bir blob kaydı. GymAppApiDbContext.IntentionallyUnscopedEntityTypes
// listesine eklenmesi gerekir.
public class MediaFile : EntityBase
{
    // IMediaStorage'ın döndürdüğü opak anahtar - depolama implementasyonu
    // (yerel disk, ileride S3/Azure Blob) değişse bile bu alanın anlamı
    // değişmez, sadece IMediaStorage.OpenReadAsync'e geçilen bir parametredir.
    public string StoragePath { get; set; } = null!;
    public string ContentType { get; set; } = null!;
    public long SizeBytes { get; set; }
    public int UploadedByUserId { get; set; }
}
