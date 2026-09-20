namespace GymAppApi.Application.Common.Interfaces;

// Donanım seçildiğinde somut bir implementasyon bu arayüzü dolduracak; şu an
// hiçbir DI kaydı yok, hiçbir kod bunu çağırmıyor (bkz.
// docs/superpowers/specs/2026-09-20-door-access-skeleton-design.md).
public interface IDoorAccessProvider
{
    Task<bool> TryOpenDoorAsync(int doorId, CancellationToken cancellationToken);
}
