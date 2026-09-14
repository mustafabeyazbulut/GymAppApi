using System.Linq.Expressions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Common;
using GymAppApi.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace GymAppApi.Persistence.Repositories;

public class ReadRepository<T> : IReadRepository<T> where T : class, IEntityBase
{
    private readonly GymAppApiDbContext _context;

    public ReadRepository(GymAppApiDbContext context) => _context = context;

    private IQueryable<T> Table => _context.Set<T>();

    public async Task<T?> GetAsync(
        Expression<Func<T, bool>> predicate,
        Func<IQueryable<T>, IIncludableQueryable<T, object>>? include = null,
        bool enableTracking = false,
        CancellationToken cancellationToken = default)
    {
        IQueryable<T> query = enableTracking ? Table : Table.AsNoTracking();
        if (include != null) query = include(query);
        return await query.FirstOrDefaultAsync(predicate, cancellationToken);
    }

    public async Task<IReadOnlyList<T>> GetAllAsync(
        Expression<Func<T, bool>>? predicate = null,
        Func<IQueryable<T>, IIncludableQueryable<T, object>>? include = null,
        Func<IQueryable<T>, IOrderedQueryable<T>>? orderBy = null,
        bool enableTracking = false,
        CancellationToken cancellationToken = default)
    {
        IQueryable<T> query = enableTracking ? Table : Table.AsNoTracking();
        if (predicate != null) query = query.Where(predicate);
        if (include != null) query = include(query);
        if (orderBy != null) query = orderBy(query);
        return await query.ToListAsync(cancellationToken);
    }

    public async Task<bool> AnyAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        => await Table.AnyAsync(predicate, cancellationToken);

    public async Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default)
        => predicate == null
            ? await Table.CountAsync(cancellationToken)
            : await Table.CountAsync(predicate, cancellationToken);
}
