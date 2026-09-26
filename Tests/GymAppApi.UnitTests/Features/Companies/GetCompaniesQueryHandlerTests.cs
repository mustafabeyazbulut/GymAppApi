using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Companies.Queries.GetCompanies;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.UnitTests.TestHelpers;
using Moq;

namespace GymAppApi.UnitTests.Features.Companies;

public class GetCompaniesQueryHandlerTests
{
    private static PackageAssignment ValidPackage(int id, int companyId, int memberUserId) => new()
    {
        Id = id, CompanyId = companyId, MemberUserId = memberUserId, Status = PackageAssignmentStatus.Active,
        EndDate = DateTime.UtcNow.AddDays(10),
    };

    [Fact]
    public async Task Handle_ReturnsEachCompanyWithItsBranchAndStaffCounts_AndMemberCountFromValidPackages()
    {
        var companies = new List<Company>
        {
            new()
            {
                Id = 1, Name = "MAT & MOVE", IsActive = true,
                Branches = new List<Branch>
                {
                    new() { Id = 1, CompanyId = 1, Name = "Kadıköy", Address = "x" },
                    // Kapatılmış şube: BranchCount'a girmez, InactiveBranchCount'ta sayılır.
                    new() { Id = 3, CompanyId = 1, Name = "Moda", Address = "x", IsActive = false },
                },
            },
            new() { Id = 2, Name = "Deactivated Co", IsActive = false, Branches = new List<Branch>() },
        };
        var assignments = new List<Assignment>
        {
            new() { Id = 1, UserId = 1, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true },
            new() { Id = 2, UserId = 2, CompanyId = 1, BranchId = 1, Role = AssignmentRole.BranchManager, IsActive = true },
        };
        var packageAssignments = new List<PackageAssignment>
        {
            // Üye 10: aynı firmada iki geçerli paket -> TEK üye sayılır.
            ValidPackage(1, companyId: 1, memberUserId: 10),
            ValidPackage(2, companyId: 1, memberUserId: 10),
            // Üye 11: geçerli paket.
            ValidPackage(3, companyId: 1, memberUserId: 11),
            // Üye 12: süresi dolmuş (Status hâlâ Active) -> sayılmaz.
            new() { Id = 4, CompanyId = 1, MemberUserId = 12, Status = PackageAssignmentStatus.Active, EndDate = DateTime.UtcNow.AddDays(-1) },
            // Üye 13: iptal -> sayılmaz.
            new() { Id = 5, CompanyId = 1, MemberUserId = 13, Status = PackageAssignmentStatus.Cancelled },
        };
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Company>()).Returns(FakeReadRepository.For(companies).Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(FakeReadRepository.For(assignments).Object);
        uow.Setup(u => u.GetReadRepository<PackageAssignment>()).Returns(FakeReadRepository.For(packageAssignments).Object);

        var result = await new GetCompaniesQueryHandler(uow.Object).Handle(new GetCompaniesQuery(), CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].BranchCount);
        Assert.Equal(1, result[0].InactiveBranchCount);
        Assert.True(result[0].IsActive);
        Assert.Equal(1, result[0].GymAdminCount);
        Assert.Equal(1, result[0].BranchManagerCount);
        Assert.Equal(0, result[0].TrainerCount);
        Assert.Equal(2, result[0].MemberCount);
        Assert.Equal(0, result[1].BranchCount);
        Assert.False(result[1].IsActive);
        Assert.Equal(0, result[1].GymAdminCount);
        Assert.Equal(0, result[1].MemberCount);
    }
}
