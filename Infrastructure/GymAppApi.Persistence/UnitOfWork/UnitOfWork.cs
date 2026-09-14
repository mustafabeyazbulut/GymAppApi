using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Common;
using GymAppApi.Persistence.Context;
using GymAppApi.Persistence.Repositories;

namespace GymAppApi.Persistence.UnitOfWork;

public class UnitOfWork : IUnitOfWork
{
    private readonly GymAppApiDbContext _context;

    public UnitOfWork(GymAppApiDbContext context) => _context = context;

    public IReadRepository<T> GetReadRepository<T>() where T : class, IEntityBase => new ReadRepository<T>(_context);

    public IWriteRepository<T> GetWriteRepository<T>() where T : class, IEntityBase => new WriteRepository<T>(_context);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => _context.SaveChangesAsync(cancellationToken);

    public async Task<IAsyncDisposable> BeginTransactionAsync(CancellationToken cancellationToken = default)
        => await _context.Database.BeginTransactionAsync(cancellationToken);

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
        => await _context.Database.CommitTransactionAsync(cancellationToken);

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
        => await _context.Database.RollbackTransactionAsync(cancellationToken);
}
