using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Common;
using GymAppApi.Persistence.Context;

namespace GymAppApi.Persistence.Repositories;

public class WriteRepository<T> : IWriteRepository<T> where T : class, IEntityBase
{
    private readonly GymAppApiDbContext _context;

    public WriteRepository(GymAppApiDbContext context) => _context = context;

    public async Task AddAsync(T entity, CancellationToken cancellationToken = default)
        => await _context.Set<T>().AddAsync(entity, cancellationToken);

    public void Update(T entity) => _context.Set<T>().Update(entity);

    public void Remove(T entity) => _context.Set<T>().Remove(entity);
}
