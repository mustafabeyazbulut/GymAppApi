using GymAppApi.Application.Common.Interfaces;
using Microsoft.Extensions.Options;

namespace GymAppApi.Infrastructure.Storage;

// Bulut depolama (S3/Azure Blob) hesap/kimlik bilgisi gerektirdiği ve bu
// oturumda sağlanamadığı için tek implementasyon bu - backend'in kendi
// diskinde saklar (bkz. docs/superpowers/specs/2026-09-20-content-library-design.md).
public class LocalDiskMediaStorage : IMediaStorage
{
    private readonly string _rootPath;

    public LocalDiskMediaStorage(IOptions<MediaStorageOptions> options)
    {
        _rootPath = options.Value.RootPath;
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<string> SaveAsync(Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        var extension = contentType switch
        {
            "video/mp4" => ".mp4",
            "video/quicktime" => ".mov",
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            _ => "",
        };
        var fileName = $"{Guid.NewGuid():N}{extension}";
        var fullPath = Path.Combine(_rootPath, fileName);

        await using var fileStream = File.Create(fullPath);
        await content.CopyToAsync(fileStream, cancellationToken);

        // Opak anahtar olarak sadece dosya adı saklanır (tam yol değil) -
        // ileride RootPath değişirse (ör. farklı bir disk/makine) mevcut
        // kayıtlar bozulmaz.
        return fileName;
    }

    public Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.Combine(_rootPath, storagePath);
        Stream stream = File.OpenRead(fullPath);
        return Task.FromResult(stream);
    }
}
