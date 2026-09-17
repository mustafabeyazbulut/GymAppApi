using System.Linq.Expressions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Exceptions;
using GymAppApi.Application.Features.Companies.Commands.CreateCompany;
using GymAppApi.Domain.Entities;
using Moq;

namespace GymAppApi.UnitTests.Features.Companies;

public class CreateCompanyCommandHandlerTests
{
    private static (Mock<IUnitOfWork> uow, Mock<IReadRepository<User>> userReadRepo, Mock<IWriteRepository<User>> userWriteRepo,
        Mock<IWriteRepository<Company>> companyWriteRepo, Mock<IWriteRepository<Branch>> branchWriteRepo,
        Mock<IWriteRepository<Assignment>> assignmentWriteRepo) Wire(bool phoneAlreadyExists, int? existingUserId = null)
    {
        var userReadRepo = new Mock<IReadRepository<User>>();
        userReadRepo.Setup(r => r.GetAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), null, false, default))
            .ReturnsAsync(phoneAlreadyExists ? new User { Id = existingUserId!.Value, FullName = "Existing", Phone = "+905551112233", PasswordHash = "x" } : null);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<User>()).Returns(userReadRepo.Object);
        var userWriteRepo = new Mock<IWriteRepository<User>>();
        uow.Setup(u => u.GetWriteRepository<User>()).Returns(userWriteRepo.Object);
        var companyWriteRepo = new Mock<IWriteRepository<Company>>();
        uow.Setup(u => u.GetWriteRepository<Company>()).Returns(companyWriteRepo.Object);
        var branchWriteRepo = new Mock<IWriteRepository<Branch>>();
        uow.Setup(u => u.GetWriteRepository<Branch>()).Returns(branchWriteRepo.Object);
        var assignmentWriteRepo = new Mock<IWriteRepository<Assignment>>();
        uow.Setup(u => u.GetWriteRepository<Assignment>()).Returns(assignmentWriteRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        uow.Setup(u => u.BeginTransactionAsync(default)).ReturnsAsync(Mock.Of<IAsyncDisposable>());

        return (uow, userReadRepo, userWriteRepo, companyWriteRepo, branchWriteRepo, assignmentWriteRepo);
    }

    private static CreateCompanyCommand ValidCommand() => new()
    {
        CompanyName = "Test Gym",
        BranchName = "Merkez",
        BranchAddress = "Adres",
        GymAdminFullName = "Ada Admin",
        GymAdminPhone = "+905551112233",
        GymAdminEmail = null,
    };

    [Fact]
    public async Task Handle_WhenPhoneIsNew_CreatesCompanyBranchUserAndGymAdminAssignment()
    {
        var (uow, _, userWriteRepo, companyWriteRepo, branchWriteRepo, assignmentWriteRepo) = Wire(phoneAlreadyExists: false);
        var passwordHasher = new Mock<IPasswordHasher>();
        passwordHasher.Setup(p => p.Hash(It.IsAny<string>())).Returns("hashed");
        var smsSender = new Mock<ISmsSender>();
        var handler = new CreateCompanyCommandHandler(uow.Object, passwordHasher.Object, smsSender.Object);

        await handler.Handle(ValidCommand(), CancellationToken.None);

        companyWriteRepo.Verify(r => r.AddAsync(It.Is<Company>(c => c.Name == "Test Gym" && c.IsActive), default), Times.Once);
        branchWriteRepo.Verify(r => r.AddAsync(It.Is<Branch>(b => b.Name == "Merkez" && b.Address == "Adres"), default), Times.Once);
        userWriteRepo.Verify(r => r.AddAsync(It.Is<User>(u => u.Phone == "+905551112233" && u.FullName == "Ada Admin"), default), Times.Once);
        assignmentWriteRepo.Verify(r => r.AddAsync(It.Is<Assignment>(a => a.Role == GymAppApi.Domain.Enums.AssignmentRole.GymAdmin && a.BranchId == null), default), Times.Once);
        smsSender.Verify(s => s.SendAsync("+905551112233", It.IsAny<string>(), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenPhoneAlreadyBelongsToAUser_ReusesThatUserInsteadOfCreatingANewOne()
    {
        var (uow, _, userWriteRepo, _, _, assignmentWriteRepo) = Wire(phoneAlreadyExists: true, existingUserId: 55);
        var passwordHasher = new Mock<IPasswordHasher>();
        var smsSender = new Mock<ISmsSender>();
        var handler = new CreateCompanyCommandHandler(uow.Object, passwordHasher.Object, smsSender.Object);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.Equal(55, result.GymAdminUserId);
        userWriteRepo.Verify(r => r.AddAsync(It.IsAny<User>(), default), Times.Never);
        assignmentWriteRepo.Verify(r => r.AddAsync(It.Is<Assignment>(a => a.UserId == 55), default), Times.Once);
    }

    [Fact]
    public async Task Handle_CallsSmsSenderOnlyAfterTheTransactionHasBeenCommitted()
    {
        var (uow, _, _, _, _, _) = Wire(phoneAlreadyExists: false);
        var passwordHasher = new Mock<IPasswordHasher>();
        passwordHasher.Setup(p => p.Hash(It.IsAny<string>())).Returns("hashed");
        var smsSender = new Mock<ISmsSender>();

        // A MockSequence on loose mocks only re-routes matching calls; an
        // out-of-order call would still be silently satisfied by Moq's
        // default async fallback (Task.CompletedTask) instead of failing.
        // Recording actual invocation order is what genuinely fails this
        // test if SendAsync were ever called before CommitTransactionAsync.
        var callOrder = new List<string>();
        uow.Setup(u => u.CommitTransactionAsync(default))
            .Callback(() => callOrder.Add("commit"))
            .Returns(Task.CompletedTask);
        smsSender.Setup(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), default))
            .Callback(() => callOrder.Add("sms"))
            .Returns(Task.CompletedTask);

        var handler = new CreateCompanyCommandHandler(uow.Object, passwordHasher.Object, smsSender.Object);

        await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.Equal(new[] { "commit", "sms" }, callOrder);
    }

    [Fact]
    public async Task Handle_WhenAWriteFailsMidTransaction_RollsBackAndNeverSendsSms()
    {
        var (uow, _, _, _, _, assignmentWriteRepo) = Wire(phoneAlreadyExists: false);
        assignmentWriteRepo.Setup(r => r.AddAsync(It.IsAny<Assignment>(), default))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var passwordHasher = new Mock<IPasswordHasher>();
        passwordHasher.Setup(p => p.Hash(It.IsAny<string>())).Returns("hashed");
        var smsSender = new Mock<ISmsSender>();
        var handler = new CreateCompanyCommandHandler(uow.Object, passwordHasher.Object, smsSender.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(ValidCommand(), CancellationToken.None));

        uow.Verify(u => u.RollbackTransactionAsync(default), Times.Once);
        uow.Verify(u => u.CommitTransactionAsync(default), Times.Never);
        smsSender.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenGymAdminEmailAlreadyBelongsToAnotherUser_ThrowsAndCreatesNothing()
    {
        var (uow, userReadRepo, userWriteRepo, companyWriteRepo, _, assignmentWriteRepo) = Wire(phoneAlreadyExists: false);
        userReadRepo.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<User, bool>>>(), default))
            .ReturnsAsync(true);
        var passwordHasher = new Mock<IPasswordHasher>();
        var smsSender = new Mock<ISmsSender>();
        var handler = new CreateCompanyCommandHandler(uow.Object, passwordHasher.Object, smsSender.Object);
        var command = ValidCommand();
        command.GymAdminEmail = "taken@example.com";

        await Assert.ThrowsAsync<EmailAlreadyRegisteredException>(() => handler.Handle(command, CancellationToken.None));

        companyWriteRepo.Verify(r => r.AddAsync(It.IsAny<Company>(), default), Times.Never);
        userWriteRepo.Verify(r => r.AddAsync(It.IsAny<User>(), default), Times.Never);
        assignmentWriteRepo.Verify(r => r.AddAsync(It.IsAny<Assignment>(), default), Times.Never);
        smsSender.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }
}
