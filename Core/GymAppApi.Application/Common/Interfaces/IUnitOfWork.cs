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
}
