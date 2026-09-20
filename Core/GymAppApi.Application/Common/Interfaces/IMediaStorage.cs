namespace GymAppApi.Application.Common.Interfaces;

// Bulut depolamaya (S3/Azure Blob) geçiş bu arayüzün yeni bir
// implementasyonuyla yapılabilir, tüketen kod (Application katmanındaki
// handler'lar) değişmez - bkz.
// docs/superpowers/specs/2026-09-20-content-library-design.md.
public interface IMediaStorage
{
    // Döndürülen string, MediaFile.StoragePath'e yazılan opak bir anahtardır.
    Task<string> SaveAsync(Stream content, string contentType, CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default);
}
