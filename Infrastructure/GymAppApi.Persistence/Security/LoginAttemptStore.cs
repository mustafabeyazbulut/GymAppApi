using GymAppApi.Application.Common.Security;
using GymAppApi.Domain.Entities;
using GymAppApi.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Persistence.Security;

// DB tabanlı (LoginFailures tablosu): çok instance'lı dağıtımda tüm API
// örnekleri aynı sayacı görür - bellek içi cache instance başına ayrı
// sayaç tutar ve kilidi aşılabilir yapardı.
//
// Atomiklik: satır uygulama yönetimli bir Version concurrency token taşır.
// Sayaç takip edilen (tracked) okumayla artırılır; eşzamanlı başka bir yazma
// araya girdiyse DbUpdateConcurrencyException alınır ve güncel satırla
// yeniden denenir - kayıp güncelleme olmaz. (ExecuteUpdate yerine bu seçildi:
// integration testlerinin InMemory sağlayıcısı ExecuteUpdate'i desteklemiyor,
// optimistik token her iki sağlayıcıda da gerçek çakışma tespiti yapıyor.)
public class LoginAttemptStore : ILoginAttemptStore
{
    private const int MaxRetries = 20;

    private readonly GymAppApiDbContext _dbContext;

    public LoginAttemptStore(GymAppApiDbContext dbContext) => _dbContext = dbContext;

    public async Task<bool> IsLockedAsync(int userId, string ipHash, DateTime now, CancellationToken cancellationToken) =>
        await _dbContext.LoginFailures.AsNoTracking()
            .AnyAsync(f => f.UserId == userId && f.IpHash == ipHash && f.LockedUntil != null && f.LockedUntil > now, cancellationToken);

    public async Task RecordFailureAsync(int userId, string ipHash, DateTime now, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var row = await _dbContext.LoginFailures
                .SingleOrDefaultAsync(f => f.UserId == userId && f.IpHash == ipHash, cancellationToken);
            if (row is null)
            {
                row = new LoginFailure { UserId = userId, IpHash = ipHash };
                _dbContext.LoginFailures.Add(row);
            }

            // Süresi dolmuş kilit: sayaç baştan başlar.
            if (row.LockedUntil is not null && row.LockedUntil <= now)
            {
                row.LockedUntil = null;
                row.FailedCount = 0;
            }

            row.FailedCount += 1;
            if (row.FailedCount >= LoginLockoutPolicy.MaxFailedAttempts)
            {
                row.LockedUntil = now.Add(LoginLockoutPolicy.LockoutDuration);
                row.FailedCount = 0;
            }
            row.Version += 1;

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException) when (attempt < MaxRetries)
            {
                // Eşzamanlı güncelleme (concurrency) veya eşzamanlı ilk ekleme
                // (unique index) - izlenen kopyayı bırak, güncel satırla tekrar dene.
                _dbContext.Entry(row).State = EntityState.Detached;
            }
        }
    }

    public async Task ResetAsync(int userId, string ipHash, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.LoginFailures
            .Where(f => f.UserId == userId && f.IpHash == ipHash)
            .ToListAsync(cancellationToken);
        await DeleteAsync(rows, cancellationToken);
    }

    public async Task ClearAllForUserAsync(int userId, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.LoginFailures.Where(f => f.UserId == userId).ToListAsync(cancellationToken);
        await DeleteAsync(rows, cancellationToken);
    }

    public async Task<int> PurgeStaleAsync(DateTime now, CancellationToken cancellationToken)
    {
        var cutoff = now - LoginLockoutPolicy.StaleRowRetention;
        // Son etkinlik: güncellenmediyse oluşturulma zamanı. (ExecuteDelete
        // yerine RemoveRange: InMemory test sağlayıcısı ExecuteDelete'i desteklemiyor.)
        var rows = await _dbContext.LoginFailures
            .Where(f => (f.UpdatedAt ?? f.CreatedAt) < cutoff && (f.LockedUntil == null || f.LockedUntil < cutoff))
            .ToListAsync(cancellationToken);
        await DeleteAsync(rows, cancellationToken);
        return rows.Count;
    }

    private async Task DeleteAsync(List<LoginFailure> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return;
        }

        _dbContext.LoginFailures.RemoveRange(rows);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Satır bu arada değişti/silindi - temizleme amacına zaten ulaşılmış
            // ya da bir sonraki başarılı işlemde tekrar denenecek; girişi bozmaz.
            foreach (var row in rows)
            {
                _dbContext.Entry(row).State = EntityState.Detached;
            }
        }
    }
}
