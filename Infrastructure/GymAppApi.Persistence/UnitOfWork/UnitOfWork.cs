using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Common;
using GymAppApi.Persistence.Context;
using GymAppApi.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Persistence.UnitOfWork;

public class UnitOfWork : IUnitOfWork
{
    private readonly GymAppApiDbContext _context;

    public UnitOfWork(GymAppApiDbContext context) => _context = context;

    public IReadRepository<T> GetReadRepository<T>() where T : class, IEntityBase => new ReadRepository<T>(_context);

    public IWriteRepository<T> GetWriteRepository<T>() where T : class, IEntityBase => new WriteRepository<T>(_context);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => _context.SaveChangesAsync(cancellationToken);

    public void ClearChangeTracker() => _context.ChangeTracker.Clear();

    public async Task<IAsyncDisposable> BeginTransactionAsync(CancellationToken cancellationToken = default)
        => await _context.Database.BeginTransactionAsync(cancellationToken);

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
        => await _context.Database.CommitTransactionAsync(cancellationToken);

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
        => await _context.Database.RollbackTransactionAsync(cancellationToken);

    public Task<TResult> ExecuteWithRetryAsync<TResult>(Func<Task<TResult>> operation)
        => _context.Database.CreateExecutionStrategy().ExecuteAsync(operation);

    public async Task<T?> GetForUpdateAsync<T>(int id, CancellationToken cancellationToken = default) where T : class, IEntityBase
    {
        var entityType = _context.Model.FindEntityType(typeof(T))
            ?? throw new InvalidOperationException($"'{typeof(T).Name}' is not a mapped entity type.");
        var tableName = entityType.GetTableName()
            ?? throw new InvalidOperationException($"'{typeof(T).Name}' has no mapped table name.");
        var schema = entityType.GetSchema();
        var qualifiedTable = schema is null ? $"\"{tableName}\"" : $"\"{schema}\".\"{tableName}\"";

        // IgnoreQueryFilters: bu metod tenant filtresinden bağımsız - çağıran
        // handler zaten kendi açık yetkilendirme kontrolünü yapmış olmalı
        // (Reservation modülündeki standing rule ile aynı). FromSqlRaw +
        // FOR UPDATE, EF Core InMemory sağlayıcısında desteklenmez - bu
        // yüzden sadece gerçek bir ilişkisel sağlayıcıyla (Postgres) çalışır.
        return await _context.Set<T>()
            .FromSqlRaw($"SELECT * FROM {qualifiedTable} WHERE \"Id\" = {{0}} FOR UPDATE", id)
            .IgnoreQueryFilters()
            .AsTracking()
            .FirstOrDefaultAsync(cancellationToken);
    }
}
