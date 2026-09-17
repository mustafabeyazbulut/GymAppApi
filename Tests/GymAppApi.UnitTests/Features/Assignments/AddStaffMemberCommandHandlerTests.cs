using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Assignments.Commands.AddStaffMember;
using GymAppApi.Application.Features.Assignments.Exceptions;
using GymAppApi.Application.Features.Auth.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Assignments;

public class AddStaffMemberCommandHandlerTests
{
    private const int CallerId = 42;
    private const int BranchIdInCompany1 = 10;

    private static (Mock<IUnitOfWork> uow, Mock<IReadRepository<User>> userReadRepo, Mock<IWriteRepository<User>> userWriteRepo, Mock<IWriteRepository<Assignment>> assignmentWriteRepo) Wire(
        IReadOnlyList<Assignment> callerAssignments, Branch? branch, User? existingUser, bool alreadyAssigned)
    {
        var uow = new Mock<IUnitOfWork>();

        var assignmentReadRepo = new Mock<IReadRepository<Assignment>>();
        assignmentReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);
        assignmentReadRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), default))
            .ReturnsAsync(alreadyAssigned);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(assignmentReadRepo.Object);
        var assignmentWriteRepo = new Mock<IWriteRepository<Assignment>>();
        uow.Setup(u => u.GetWriteRepository<Assignment>()).Returns(assignmentWriteRepo.Object);

        var branchReadRepo = new Mock<IReadRepository<Branch>>();
        branchReadRepo.Setup(r => r.GetAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Branch, bool>>>(), null, false, default))
            .ReturnsAsync(branch);
        uow.Setup(u => u.GetReadRepository<Branch>()).Returns(branchReadRepo.Object);

        var userReadRepo = new Mock<IReadRepository<User>>();
        userReadRepo.Setup(r => r.GetAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), null, false, default))
            .ReturnsAsync(existingUser);
        uow.Setup(u => u.GetReadRepository<User>()).Returns(userReadRepo.Object);
        var userWriteRepo = new Mock<IWriteRepository<User>>();
        uow.Setup(u => u.GetWriteRepository<User>()).Returns(userWriteRepo.Object);

        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        uow.Setup(u => u.BeginTransactionAsync(default)).ReturnsAsync(Mock.Of<IAsyncDisposable>());

        return (uow, userReadRepo, userWriteRepo, assignmentWriteRepo);
    }

    private static Branch Branch1() => new() { Id = BranchIdInCompany1, CompanyId = 1, Name = "Merkez", Address = "..." };

    private static AddStaffMemberCommand ValidCommand() => new()
    {
        FullName = "New Trainer",
        Phone = "+905550003333",
        Role = AssignmentRole.Trainer,
        BranchId = BranchIdInCompany1,
        RequestedByUserId = CallerId,
    };

    [Fact]
    public async Task Handle_WhenCallerIsGymAdminOfTheBranchsCompany_CreatesNewUserAndAssignment()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, _, userWriteRepo, assignmentWriteRepo) = Wire(callerAssignments, Branch1(), existingUser: null, alreadyAssigned: false);
        var smsSender = new Mock<ISmsSender>();
        var passwordHasher = new Mock<IPasswordHasher>();
        passwordHasher.Setup(p => p.Hash(It.IsAny<string>())).Returns("hashed");
        var handler = new AddStaffMemberCommandHandler(uow.Object, passwordHasher.Object, smsSender.Object);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.Equal(1, result.CompanyId);
        userWriteRepo.Verify(r => r.AddAsync(It.Is<User>(u => u.Phone == "+905550003333"), default), Times.Once);
        assignmentWriteRepo.Verify(r => r.AddAsync(It.Is<Assignment>(a => a.Role == AssignmentRole.Trainer && a.BranchId == BranchIdInCompany1), default), Times.Once);
        smsSender.Verify(s => s.SendAsync("+905550003333", It.IsAny<string>(), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenExistingUserIsNotYetAssignedInThisCompany_AttachesExistingUserInsteadOfCreatingANewOne()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var existingUser = new User { Id = 7, FullName = "Existing", Phone = "+905550003333", PasswordHash = "x" };
        var (uow, _, userWriteRepo, assignmentWriteRepo) = Wire(callerAssignments, Branch1(), existingUser, alreadyAssigned: false);
        var smsSender = new Mock<ISmsSender>();
        var handler = new AddStaffMemberCommandHandler(uow.Object, Mock.Of<IPasswordHasher>(), smsSender.Object);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.Equal(7, result.UserId);
        userWriteRepo.Verify(r => r.AddAsync(It.IsAny<User>(), default), Times.Never);
        assignmentWriteRepo.Verify(r => r.AddAsync(It.Is<Assignment>(a => a.UserId == 7 && a.BranchId == BranchIdInCompany1), default), Times.Once);
        smsSender.Verify(s => s.SendAsync("+905550003333", It.IsAny<string>(), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCallerIsBranchManagerOfADifferentBranch_ThrowsForbiddenException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, BranchId = 999, Role = AssignmentRole.BranchManager, IsActive = true } };
        var (uow, _, _, assignmentWriteRepo) = Wire(callerAssignments, Branch1(), existingUser: null, alreadyAssigned: false);
        var handler = new AddStaffMemberCommandHandler(uow.Object, Mock.Of<IPasswordHasher>(), Mock.Of<ISmsSender>());

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
        assignmentWriteRepo.Verify(r => r.AddAsync(It.IsAny<Assignment>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenBranchDoesNotExist_ThrowsNotFoundException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, _, _, _) = Wire(callerAssignments, branch: null, existingUser: null, alreadyAssigned: false);
        var handler = new AddStaffMemberCommandHandler(uow.Object, Mock.Of<IPasswordHasher>(), Mock.Of<ISmsSender>());

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenPhoneAlreadyHasAnActiveAssignmentInThisCompany_ThrowsUserAlreadyAssignedException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var existingUser = new User { Id = 7, FullName = "Existing", Phone = "+905550003333", PasswordHash = "x" };
        var (uow, _, _, assignmentWriteRepo) = Wire(callerAssignments, Branch1(), existingUser, alreadyAssigned: true);
        var handler = new AddStaffMemberCommandHandler(uow.Object, Mock.Of<IPasswordHasher>(), Mock.Of<ISmsSender>());

        await Assert.ThrowsAsync<UserAlreadyAssignedException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
        assignmentWriteRepo.Verify(r => r.AddAsync(It.IsAny<Assignment>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenEmailAlreadyBelongsToAnotherUser_ThrowsAndCreatesNothing()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, userReadRepo, userWriteRepo, assignmentWriteRepo) = Wire(callerAssignments, Branch1(), existingUser: null, alreadyAssigned: false);
        userReadRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), default))
            .ReturnsAsync(true);
        var handler = new AddStaffMemberCommandHandler(uow.Object, Mock.Of<IPasswordHasher>(), Mock.Of<ISmsSender>());
        var command = ValidCommand();
        command.Email = "taken@example.com";

        await Assert.ThrowsAsync<EmailAlreadyRegisteredException>(() => handler.Handle(command, CancellationToken.None));

        userWriteRepo.Verify(r => r.AddAsync(It.IsAny<User>(), default), Times.Never);
        assignmentWriteRepo.Verify(r => r.AddAsync(It.IsAny<Assignment>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenAWriteFailsMidTransaction_RollsBackAndNeverSendsSms()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, _, _, assignmentWriteRepo) = Wire(callerAssignments, Branch1(), existingUser: null, alreadyAssigned: false);
        assignmentWriteRepo.Setup(r => r.AddAsync(It.IsAny<Assignment>(), default))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var passwordHasher = new Mock<IPasswordHasher>();
        passwordHasher.Setup(p => p.Hash(It.IsAny<string>())).Returns("hashed");
        var smsSender = new Mock<ISmsSender>();
        var handler = new AddStaffMemberCommandHandler(uow.Object, passwordHasher.Object, smsSender.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(ValidCommand(), CancellationToken.None));

        uow.Verify(u => u.RollbackTransactionAsync(default), Times.Once);
        uow.Verify(u => u.CommitTransactionAsync(default), Times.Never);
        smsSender.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }
}
