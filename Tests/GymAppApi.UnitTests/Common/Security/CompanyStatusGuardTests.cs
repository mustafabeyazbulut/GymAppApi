using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Security;
using GymAppApi.Domain.Entities;
using GymAppApi.UnitTests.TestHelpers;
using Moq;

namespace GymAppApi.UnitTests.Common.Security;

public class CompanyStatusGuardTests
{
    private static Task Run(params Company[] companies)
    {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Company>()).Returns(FakeReadRepository.For(companies).Object);
        return CompanyStatusGuard.EnsureActiveAsync(uow.Object, companyId: 1, CancellationToken.None);
    }

    [Fact]
    public Task ActiveCompany_Passes() => Run(new Company { Id = 1, Name = "A", IsActive = true });

    [Fact]
    public async Task InactiveCompany_ThrowsCompanyInactive()
    {
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => Run(new Company { Id = 1, Name = "A", IsActive = false }));
        Assert.Equal("CompanyInactive", ex.Code);
    }

    [Fact]
    public async Task MissingCompany_IsTreatedAsInactive()
    {
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => Run());
        Assert.Equal("CompanyInactive", ex.Code);
    }
}
