using GymAppApi.Application.Common.Assignments;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Assignments.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.UnitTests.TestHelpers;
using Moq;

namespace GymAppApi.UnitTests.Common.Assignments;

public class LastGymAdminGuardTests
{
    private const int UserId = 1;

    private static readonly Company ActiveCompany = new() { Id = 10, Name = "A", IsActive = true };
    private static readonly Company SecondCompany = new() { Id = 20, Name = "B", IsActive = true };
    private static readonly Company ClosedCompany = new() { Id = 30, Name = "C", IsActive = false };

    private static Assignment GymAdmin(int userId, Company company, bool frozen = false, bool isActive = true) => new()
    {
        UserId = userId,
        CompanyId = company.Id,
        Company = company,
        User = new User { Id = userId, FullName = "x", Phone = "x", PasswordHash = "x", IsAccountFrozen = frozen },
        Role = AssignmentRole.GymAdmin,
        IsActive = isActive,
    };

    private static Task Run(params Assignment[] assignments)
    {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(FakeReadRepository.For(assignments).Object);
        return LastGymAdminGuard.EnsureNotLastGymAdminAnywhereAsync(uow.Object, UserId, CancellationToken.None);
    }

    [Fact]
    public Task NotAGymAdmin_Passes() => Run();

    [Fact]
    public async Task SoleGymAdmin_Throws() =>
        await Assert.ThrowsAsync<LastGymAdminException>(() => Run(GymAdmin(UserId, ActiveCompany)));

    [Fact]
    public Task WithAnotherActiveGymAdmin_Passes() => Run(GymAdmin(UserId, ActiveCompany), GymAdmin(2, ActiveCompany));

    [Fact]
    public async Task OtherGymAdminFrozen_Throws() =>
        await Assert.ThrowsAsync<LastGymAdminException>(() => Run(GymAdmin(UserId, ActiveCompany), GymAdmin(2, ActiveCompany, frozen: true)));

    [Fact]
    public async Task OtherGymAdminAssignmentInactive_Throws() =>
        await Assert.ThrowsAsync<LastGymAdminException>(() => Run(GymAdmin(UserId, ActiveCompany), GymAdmin(2, ActiveCompany, isActive: false)));

    [Fact]
    public async Task CoveredInOneCompany_ButSoleInAnother_Throws() =>
        await Assert.ThrowsAsync<LastGymAdminException>(() => Run(
            GymAdmin(UserId, ActiveCompany), GymAdmin(2, ActiveCompany), GymAdmin(UserId, SecondCompany)));

    [Fact]
    public Task SoleGymAdminOfAClosedCompany_Passes() => Run(GymAdmin(UserId, ClosedCompany));
}
