using GymAppApi.Domain.Common;

namespace GymAppApi.Application.Common.Interfaces;

public interface IUnitOfWork
{
    IReadRepository<T> GetReadRepository<T>() where T : class, IEntityBase;
    IWriteRepository<T> GetWriteRepository<T>() where T : class, IEntityBase;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    // İzlenen tüm varlıkları bırakır - optimistik concurrency çakışmasından sonra
    // güncel satırları yeniden okuyup tekrar denemek için (bayat kopyalar
    // izlenmeye devam ederse yeniden okuma aynı bayat örneği döndürürdü).
    void ClearChangeTracker();

    Task<IAsyncDisposable> BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);

    // DbContext'in EnableRetryOnFailure ile kurulan execution strategy'si,
    // kullanici tarafindan baslatilan (BeginTransactionAsync ile acilan) bir
    // transaction'i sadece tum islem (begin+commit/rollback dahil) bu metodun
    // verdigi delegate'in ICINDE calisirsa yeniden deneyebiliyor - TransactionBehavior
    // bu yuzden BeginTransactionAsync'i dogrudan degil, bunun icinden cagirmali.
    Task<TResult> ExecuteWithRetryAsync<TResult>(Func<Task<TResult>> operation);

    // Postgres'te "SELECT ... FOR UPDATE" ile satır seviyesinde gerçek bir
    // kilit alır - kapasite kontrolü gibi "oku + sınırı denetle + yaz"
    // dizilerinde iki eşzamanlı isteğin ikisinin de aynı anda "kapasite dolu
    // değil" sonucunu okumasını (race condition) engellemek için kullanılır
    // (bkz. EnrollInClassSessionCommandHandler). Bu metod, ITransactionalRequest
    // ile işaretlenmiş bir komutun TransactionBehavior tarafından açılan
    // transaction'ı İÇİNDE çağrılmalı - aksi halde kilit satır okunur
    // okunmaz (transaction commit/rollback olmadan) serbest kalır ve hiçbir
    // koruma sağlamaz.
    Task<T?> GetForUpdateAsync<T>(int id, CancellationToken cancellationToken = default) where T : class, IEntityBase;
}
