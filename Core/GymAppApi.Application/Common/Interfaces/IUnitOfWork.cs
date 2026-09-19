using GymAppApi.Domain.Common;

namespace GymAppApi.Application.Common.Interfaces;

public interface IUnitOfWork
{
    IReadRepository<T> GetReadRepository<T>() where T : class, IEntityBase;
    IWriteRepository<T> GetWriteRepository<T>() where T : class, IEntityBase;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<IAsyncDisposable> BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);

    // DbContext'in EnableRetryOnFailure ile kurulan execution strategy'si,
    // kullanici tarafindan baslatilan (BeginTransactionAsync ile acilan) bir
    // transaction'i sadece tum islem (begin+commit/rollback dahil) bu metodun
    // verdigi delegate'in ICINDE calisirsa yeniden deneyebiliyor - TransactionBehavior
    // bu yuzden BeginTransactionAsync'i dogrudan degil, bunun icinden cagirmali.
    Task<TResult> ExecuteWithRetryAsync<TResult>(Func<Task<TResult>> operation);
}
