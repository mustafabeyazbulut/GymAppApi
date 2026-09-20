using System.Linq.Expressions;
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.ContentLibrary.Commands.SetContentItemActive;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.ContentLibrary;

public class SetContentItemActiveCommandHandlerTests
{
    private const int CallerId = 42;
    private const int CompanyId = 1;
    private const int BranchId = 10;

    private static Mock<IUnitOfWork> Wire(ContentItem? contentItem, IReadOnlyList<Assignment> callerAssignments)
    {
        var contentItemReadRepo = new Mock<IReadRepository<ContentItem>>();
        contentItemReadRepo.Setup(r => r.GetAsync(
                It.IsAny<Expression<Func<ContentItem, bool>>>(), null, false, default))
            .ReturnsAsync(contentItem);

        var callerReadRepo = new Mock<IReadRepository<Assignment>>();
        callerReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);

        var contentItemWriteRepo = new Mock<IWriteRepository<ContentItem>>();

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<ContentItem>()).Returns(contentItemReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(callerReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<ContentItem>()).Returns(contentItemWriteRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        return uow;
    }

    private static ContentItem Item() => new() { Id = 1, CompanyId = CompanyId, BranchId = BranchId, Title = "x", MediaFileId = 1, IsActive = true };

    [Fact]
    public async Task Handle_WhenContentItemDoesNotExist_ThrowsNotFoundException()
    {
        var uow = Wire(contentItem: null, callerAssignments: new List<Assignment>());
        var handler = new SetContentItemActiveCommandHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new SetContentItemActiveCommand { ContentItemId = 1, IsActive = false, RequestedByUserId = CallerId }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCallerIsGymAdminOfTheCompany_SetsActive()
    {
        var callerAssignments = new List<Assignment>
        {
            new() { Id = 1, UserId = CallerId, CompanyId = CompanyId, Role = AssignmentRole.GymAdmin, IsActive = true },
        };
        var uow = Wire(Item(), callerAssignments);
        var handler = new SetContentItemActiveCommandHandler(uow.Object);

        await handler.Handle(new SetContentItemActiveCommand { ContentItemId = 1, IsActive = false, RequestedByUserId = CallerId }, CancellationToken.None);

        uow.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCallerIsUnrelated_ThrowsForbiddenException()
    {
        var uow = Wire(Item(), callerAssignments: new List<Assignment>());
        var handler = new SetContentItemActiveCommandHandler(uow.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new SetContentItemActiveCommand { ContentItemId = 1, IsActive = false, RequestedByUserId = CallerId }, CancellationToken.None));
    }
}
