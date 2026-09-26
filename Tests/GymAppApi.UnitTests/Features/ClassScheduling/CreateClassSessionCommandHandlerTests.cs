using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.ClassScheduling.Commands.CreateClassSession;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.ClassScheduling;

public class CreateClassSessionCommandHandlerTests
{
    private const int CallerId = 42;
    private const int CompanyId = 1;
    private const int BranchId = 10;

    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<ClassSession>> writeRepo) Wire(
        Branch? branch, IReadOnlyList<Assignment> callerAssignments)
    {
        var branchReadRepo = new Mock<IReadRepository<Branch>>();
        branchReadRepo.Setup(r => r.GetAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Branch, bool>>>(), null, false, default))
            .ReturnsAsync(branch);

        var callerReadRepo = new Mock<IReadRepository<Assignment>>();
        callerReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);

        var writeRepo = new Mock<IWriteRepository<ClassSession>>();

        var uow = new Mock<IUnitOfWork>();

        uow.Setup(u => u.GetReadRepository<Company>()).Returns(GymAppApi.UnitTests.TestHelpers.TestCompanies.AllActive());
        uow.Setup(u => u.GetReadRepository<Branch>()).Returns(branchReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(callerReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<ClassSession>()).Returns(writeRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        return (uow, writeRepo);
    }

    private static CreateClassSessionCommand ValidCommand() => new()
    {
        BranchId = BranchId,
        TrainerUserId = 99,
        Category = ClassSessionCategory.GroupClass,
        Name = "Sabah Yogası",
        Date = new DateOnly(2026, 9, 21),
        StartTime = new TimeOnly(9, 0),
        EndTime = new TimeOnly(10, 0),
        Capacity = 15,
        CancellationCutoffHours = 2,
        RequestedByUserId = CallerId,
    };

    [Fact]
    public async Task Handle_WhenBranchDoesNotExist_ThrowsNotFoundException()
    {
        var (uow, writeRepo) = Wire(branch: null, callerAssignments: new List<Assignment>());
        var handler = new CreateClassSessionCommandHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
        writeRepo.Verify(r => r.AddAsync(It.IsAny<ClassSession>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenCallerIsGymAdminOfTheBranchsCompany_CreatesTheClassSession()
    {
        var branch = new Branch { Id = BranchId, CompanyId = CompanyId, Name = "Merkez", Address = "..." };
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = CompanyId, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, writeRepo) = Wire(branch, callerAssignments);
        var handler = new CreateClassSessionCommandHandler(uow.Object);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.Equal("Sabah Yogası", result.Name);
        writeRepo.Verify(r => r.AddAsync(It.Is<ClassSession>(s =>
            s.CompanyId == CompanyId && s.BranchId == BranchId && s.Category == ClassSessionCategory.GroupClass &&
            s.Capacity == 15 && s.CreatedByUserId == CallerId), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCallerIsBranchManagerOfThatExactBranch_CreatesTheClassSession()
    {
        var branch = new Branch { Id = BranchId, CompanyId = CompanyId, Name = "Merkez", Address = "..." };
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = CompanyId, BranchId = BranchId, Role = AssignmentRole.BranchManager, IsActive = true } };
        var (uow, writeRepo) = Wire(branch, callerAssignments);
        var handler = new CreateClassSessionCommandHandler(uow.Object);

        await handler.Handle(ValidCommand(), CancellationToken.None);

        writeRepo.Verify(r => r.AddAsync(It.IsAny<ClassSession>(), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCallerIsBranchManagerOfADifferentBranch_ThrowsForbiddenException()
    {
        var branch = new Branch { Id = BranchId, CompanyId = CompanyId, Name = "Merkez", Address = "..." };
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = CompanyId, BranchId = 999, Role = AssignmentRole.BranchManager, IsActive = true } };
        var (uow, writeRepo) = Wire(branch, callerAssignments);
        var handler = new CreateClassSessionCommandHandler(uow.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
        writeRepo.Verify(r => r.AddAsync(It.IsAny<ClassSession>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenCallerHasNoRelevantAssignment_ThrowsForbiddenException()
    {
        var branch = new Branch { Id = BranchId, CompanyId = CompanyId, Name = "Merkez", Address = "..." };
        var (uow, writeRepo) = Wire(branch, callerAssignments: new List<Assignment>());
        var handler = new CreateClassSessionCommandHandler(uow.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
        writeRepo.Verify(r => r.AddAsync(It.IsAny<ClassSession>(), default), Times.Never);
    }
}
