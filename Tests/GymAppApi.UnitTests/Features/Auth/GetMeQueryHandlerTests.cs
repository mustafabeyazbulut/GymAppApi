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
}
