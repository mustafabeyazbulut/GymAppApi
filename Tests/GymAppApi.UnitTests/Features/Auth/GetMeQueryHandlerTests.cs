using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Queries.GetMe;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Auth;

public class GetMeQueryHandlerTests
{
    [Fact]
    public async Task Handle_WhenUserNotFound_ThrowsNotFoundException()
    {
        var userReadRepo = new Mock<IReadRepository<User>>();
        userReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), It.IsAny<Func<IQueryable<User>, Microsoft.EntityFrameworkCore.Query.IIncludableQueryable<User, object>>?>(), false, default))
            .ReturnsAsync((User?)null);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<User>()).Returns(userReadRepo.Object);

        var handler = new GetMeQueryHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new GetMeQuery { UserId = 1 }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenUserHasNoAssignments_ReturnsEmptyAssignmentList()
    {
        var user = new User { Id = 1, FullName = "Ayşe", Phone = "+905551112233", Email = "ayse@test.com", PasswordHash = "x", Assignments = new List<Assignment>() };
        var userReadRepo = new Mock<IReadRepository<User>>();
        userReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), It.IsAny<Func<IQueryable<User>, Microsoft.EntityFrameworkCore.Query.IIncludableQueryable<User, object>>?>(), false, default))
            .ReturnsAsync(user);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<User>()).Returns(userReadRepo.Object);

        var handler = new GetMeQueryHandler(uow.Object);
        var result = await handler.Handle(new GetMeQuery { UserId = 1 }, CancellationToken.None);

        Assert.Equal("Ayşe", result.FullName);
        Assert.Empty(result.Assignments);
    }

    [Fact]
    public async Task Handle_ReturnsPreferredLanguageAndIsAccountFrozen()
    {
        var user = new User
        {
            Id = 1, FullName = "Ayşe", Phone = "+905551112233", PasswordHash = "x", PreferredLanguage = "en", IsAccountFrozen = true,
            Assignments = new List<Assignment>(),
        };
        var userReadRepo = new Mock<IReadRepository<User>>();
        userReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), It.IsAny<Func<IQueryable<User>, Microsoft.EntityFrameworkCore.Query.IIncludableQueryable<User, object>>?>(), false, default))
            .ReturnsAsync(user);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<User>()).Returns(userReadRepo.Object);

        var handler = new GetMeQueryHandler(uow.Object);
        var result = await handler.Handle(new GetMeQuery { UserId = 1 }, CancellationToken.None);

        Assert.Equal("en", result.PreferredLanguage);
        Assert.True(result.IsAccountFrozen);
    }

    [Fact]
    public async Task Handle_MapsActiveAssignmentsWithRoleAsString()
    {
        var company = new Company { Id = 3, Name = "MAT & MOVE Kadıköy", IsActive = true };
        var user = new User
        {
            Id = 1, FullName = "Ayşe", Phone = "+905551112233", PasswordHash = "x",
            Assignments = new List<Assignment>
            {
                new() { Id = 10, UserId = 1, CompanyId = 3, Company = company, BranchId = null, Role = AssignmentRole.Member, IsActive = true },
            },
        };
        var userReadRepo = new Mock<IReadRepository<User>>();
        userReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), It.IsAny<Func<IQueryable<User>, Microsoft.EntityFrameworkCore.Query.IIncludableQueryable<User, object>>?>(), false, default))
            .ReturnsAsync(user);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<User>()).Returns(userReadRepo.Object);

        var handler = new GetMeQueryHandler(uow.Object);
        var result = await handler.Handle(new GetMeQuery { UserId = 1 }, CancellationToken.None);

        var assignment = Assert.Single(result.Assignments);
        Assert.Equal(3, assignment.CompanyId);
        Assert.Equal("MAT & MOVE Kadıköy", assignment.CompanyName);
        Assert.Equal("Member", assignment.Role);
    }

    [Fact]
    public async Task Handle_MapsPackageAssignmentsExcludingCancelledOnes()
    {
        var company = new Company { Id = 3, Name = "MAT & MOVE Kadıköy", IsActive = true };
        var package = new Package { Id = 5, CompanyId = 3, Name = "10 Seans", IsActive = true, Price = 1500m, SessionCount = 10 };
        var startDate = new DateTime(2026, 1, 1);
        var user = new User
        {
            Id = 1, FullName = "Ayşe", Phone = "+905551112233", PasswordHash = "x",
            Assignments = new List<Assignment>(),
            PackageAssignments = new List<PackageAssignment>
            {
                new() { Id = 20, MemberUserId = 1, PackageId = 5, Package = package, CompanyId = 3, Company = company, BranchId = null, Status = PackageAssignmentStatus.Active, StartDate = startDate, EndDate = null, RemainingSessions = 7 },
                new() { Id = 21, MemberUserId = 1, PackageId = 5, Package = package, CompanyId = 3, Company = company, Status = PackageAssignmentStatus.Cancelled },
            },
        };
        var userReadRepo = new Mock<IReadRepository<User>>();
        userReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), It.IsAny<Func<IQueryable<User>, Microsoft.EntityFrameworkCore.Query.IIncludableQueryable<User, object>>?>(), false, default))
            .ReturnsAsync(user);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<User>()).Returns(userReadRepo.Object);

        var handler = new GetMeQueryHandler(uow.Object);
        var result = await handler.Handle(new GetMeQuery { UserId = 1 }, CancellationToken.None);

        var pa = Assert.Single(result.PackageAssignments);
        Assert.Equal(20, pa.Id);
        Assert.Equal(3, pa.CompanyId);
        Assert.Equal("MAT & MOVE Kadıköy", pa.CompanyName);
        Assert.Equal(5, pa.PackageId);
        Assert.Equal("10 Seans", pa.PackageName);
        Assert.Equal(1500m, pa.Price);
        Assert.Equal("Active", pa.Status);
        Assert.Equal(startDate, pa.StartDate);
        Assert.Equal(10, pa.SessionCount);
        Assert.Equal(7, pa.RemainingSessions);
    }
}
