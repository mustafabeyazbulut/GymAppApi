using GymAppApi.Application.Common.Interfaces;
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
}
