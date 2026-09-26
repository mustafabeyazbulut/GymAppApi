using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Companies.Queries.GetCompanyDetail;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.UnitTests.TestHelpers;
using Moq;

namespace GymAppApi.UnitTests.Features.Companies;

public class GetCompanyDetailQueryHandlerTests
{
    private static Mock<IUnitOfWork> Wire(IEnumerable<Company> companies, IEnumerable<Assignment> assignments, IEnumerable<PackageAssignment> packageAssignments)
    {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Company>()).Returns(FakeReadRepository.For(companies).Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(FakeReadRepository.For(assignments).Object);
        uow.Setup(u => u.GetReadRepository<PackageAssignment>()).Returns(FakeReadRepository.For(packageAssignments).Object);
        return uow;
    }

    [Fact]
    public async Task Handle_WhenCompanyNotFound_ThrowsNotFoundException()
    {
        var uow = Wire(new List<Company>(), new List<Assignment>(), new List<PackageAssignment>());

        await Assert.ThrowsAsync<NotFoundException>(() => new GetCompanyDetailQueryHandler(uow.Object).Handle(new GetCompanyDetailQuery(1), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_ReturnsCompanyWithItsBranches_StaffCounts_AndMemberCountFromValidPackages()
    {
        var company = new Company
        {
            Id = 1, Name = "MAT & MOVE", IsActive = true,
            Branches = new List<Branch> { new() { Id = 5, CompanyId = 1, Name = "Kadıköy", Address = "Adres 1", IsActive = true } },
        };
        var assignments = new List<Assignment>
        {
            new()
            {
                Id = 1, UserId = 1, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true,
                User = new User { Id = 1, FullName = "Mehmet Kaya", Phone = "+905550001122", PasswordHash = "x" },
            },
            new()
            {
                Id = 2, UserId = 2, CompanyId = 1, BranchId = 5, Role = AssignmentRole.BranchManager, IsActive = true,
                User = new User { Id = 2, FullName = "Ayşe Yılmaz", Phone = "+905551112233", PasswordHash = "x" },
            },
            new() { Id = 3, UserId = 3, CompanyId = 1, BranchId = 5, Role = AssignmentRole.Trainer, IsActive = true },
        };
        var packageAssignments = new List<PackageAssignment>
        {
            new() { Id = 1, CompanyId = 1, MemberUserId = 20, Status = PackageAssignmentStatus.Active },
            new() { Id = 2, CompanyId = 1, MemberUserId = 20, Status = PackageAssignmentStatus.Active, RemainingSessions = 3 },
            new() { Id = 3, CompanyId = 1, MemberUserId = 21, Status = PackageAssignmentStatus.Active, RemainingSessions = 0 },
            new() { Id = 4, CompanyId = 2, MemberUserId = 22, Status = PackageAssignmentStatus.Active },
        };
        var uow = Wire(new[] { company }, assignments, packageAssignments);

        var result = await new GetCompanyDetailQueryHandler(uow.Object).Handle(new GetCompanyDetailQuery(1), CancellationToken.None);

        Assert.Equal("MAT & MOVE", result.Name);
        var branch = Assert.Single(result.Branches);
        Assert.Equal("Kadıköy", branch.Name);
        Assert.Equal("Ayşe Yılmaz", branch.ManagerName);
        var gymAdmin = Assert.Single(result.GymAdmins);
        Assert.Equal(1, gymAdmin.AssignmentId);
        Assert.Equal("Mehmet Kaya", gymAdmin.FullName);
        Assert.Equal("+905550001122", gymAdmin.Phone);
        Assert.Equal(1, result.GymAdminCount);
        Assert.Equal(1, result.BranchManagerCount);
        Assert.Equal(1, result.TrainerCount);
        // Üye 20 (iki geçerli paket) tek sayılır; üye 21'in hakkı bitmiş;
        // üye 22 başka firmada.
        Assert.Equal(1, result.MemberCount);
    }
}
