using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Assignments.Queries.GetStaffMembers;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Assignments;

public class GetStaffMembersQueryHandlerTests
{
    private static Mock<IUnitOfWork> Wire(IReadOnlyList<Assignment> assignments)
    {
        var assignmentRepo = new Mock<IReadRepository<Assignment>>();
        assignmentRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>?>(),
                It.IsAny<Func<IQueryable<Assignment>, Microsoft.EntityFrameworkCore.Query.IIncludableQueryable<Assignment, object>>?>(),
                It.IsAny<Func<IQueryable<Assignment>, IOrderedQueryable<Assignment>>?>(), false, default))
            .ReturnsAsync(assignments);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(assignmentRepo.Object);
        return uow;
    }

    [Fact]
    public async Task Handle_MapsEachAssignmentToAStaffMemberDto()
    {
        var trainer = new User { Id = 7, FullName = "Ayşe Yılmaz", Phone = "+905551112233" };
        var branch = new Branch { Id = 10, Name = "Merkez" };
        var assignments = new List<Assignment>
        {
            new()
            {
                Id = 1, UserId = 7, User = trainer, Role = AssignmentRole.Trainer,
                CompanyId = 3, BranchId = 10, Branch = branch, IsActive = true,
            },
        };
        var uow = Wire(assignments);
        var handler = new GetStaffMembersQueryHandler(uow.Object);

        var result = await handler.Handle(new GetStaffMembersQuery(), CancellationToken.None);

        var dto = Assert.Single(result);
        Assert.Equal("Ayşe Yılmaz", dto.FullName);
        Assert.Equal("Trainer", dto.Role);
        Assert.Equal("Merkez", dto.BranchName);
    }

    [Fact]
    public async Task Handle_WhenNoAssignments_ReturnsEmptyList()
    {
        var uow = Wire(new List<Assignment>());
        var handler = new GetStaffMembersQueryHandler(uow.Object);

        var result = await handler.Handle(new GetStaffMembersQuery(), CancellationToken.None);

        Assert.Empty(result);
    }
}
