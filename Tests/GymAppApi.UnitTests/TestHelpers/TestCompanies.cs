using System.Linq.Expressions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using Microsoft.EntityFrameworkCore.Query;
using Moq;

namespace GymAppApi.UnitTests.TestHelpers;

// CompanyStatusGuard'ın okuduğu Company deposu: istenen her firma aktif.
// Pasif firma senaryoları integration testlerinde (InactiveCompanyWriteTests).
public static class TestCompanies
{
    public static IReadRepository<Company> AllActive()
    {
        var mock = new Mock<IReadRepository<Company>>();
        mock.Setup(r => r.GetAsync(
                It.IsAny<Expression<Func<Company, bool>>>(),
                It.IsAny<Func<IQueryable<Company>, IIncludableQueryable<Company, object>>?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Company { Id = 1, Name = "Aktif", IsActive = true });
        return mock.Object;
    }
}
