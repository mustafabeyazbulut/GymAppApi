using GymAppApi.Domain.Common;

namespace GymAppApi.Application.Common.Interfaces;

public interface IWriteRepository<T> where T : class, IEntityBase
{
    Task AddAsync(T entity, CancellationToken cancellationToken = default);
    void Update(T entity);
    void Remove(T entity);
}
