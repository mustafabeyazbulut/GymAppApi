using System.Linq.Expressions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Common;
using Microsoft.EntityFrameworkCore.Query;
using Moq;

namespace GymAppApi.UnitTests.TestHelpers;

// Handler'ın verdiği GERÇEK predicate'i bellek içi listeye uygulayan sahte
// okuma repository'si - kapsam/filtre mantığı handler'ın kendi predicate'inde
// olduğunda (şube kapsamı, geçerli paket, rol kontrolleri) testin o mantığı
// gerçekten sınayabilmesi için. include/orderBy/tracking parametreleri
// yok sayılır (include'lar test verisinde navigation property olarak zaten
// dolduruluyor); global query filter'lar da simüle edilmez.
public static class FakeReadRepository
{
    public static Mock<IReadRepository<T>> For<T>(IEnumerable<T> items) where T : class, IEntityBase
    {
        var data = items.ToList();
        var mock = new Mock<IReadRepository<T>>();

        mock.Setup(r => r.GetAllAsync(
                It.IsAny<Expression<Func<T, bool>>?>(),
                It.IsAny<Func<IQueryable<T>, IIncludableQueryable<T, object>>?>(),
                It.IsAny<Func<IQueryable<T>, IOrderedQueryable<T>>?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>>? predicate,
                Func<IQueryable<T>, IIncludableQueryable<T, object>>? _,
                Func<IQueryable<T>, IOrderedQueryable<T>>? orderBy,
                bool __,
                CancellationToken ___) =>
            {
                var query = data.AsQueryable();
                var filtered = predicate is null ? query : query.Where(predicate);
                return (IReadOnlyList<T>)(orderBy is null ? filtered : orderBy(filtered)).ToList();
            });

        mock.Setup(r => r.GetAsync(
                It.IsAny<Expression<Func<T, bool>>>(),
                It.IsAny<Func<IQueryable<T>, IIncludableQueryable<T, object>>?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>> predicate,
                Func<IQueryable<T>, IIncludableQueryable<T, object>>? _,
                bool __,
                CancellationToken ___) => data.AsQueryable().FirstOrDefault(predicate));

        mock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<T, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>> predicate, CancellationToken _) => data.AsQueryable().Any(predicate));

        mock.Setup(r => r.CountAsync(It.IsAny<Expression<Func<T, bool>>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>>? predicate, CancellationToken _) =>
                predicate is null ? data.Count : data.AsQueryable().Count(predicate));

        return mock;
    }
}
