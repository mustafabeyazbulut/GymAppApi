using System.Linq.Expressions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Companies.Queries.GetCompanies;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Microsoft.EntityFrameworkCore.Query;
using Moq;

namespace GymAppApi.UnitTests.Features.Companies;

public class GetCompaniesQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsEachCompanyWithItsBranchCount()
    {
        var companies = new List<Company>
        {
            new()
            {
                Id = 1, Name = "MAT & MOVE", IsActive = true,
                Branches = new List<Branch> { new() { Id = 1, CompanyId = 1, Name = "Kadıköy", Address = "x" } },
            },
            new() { Id = 2, Name = "Deactivated Co", IsActive = false, Branches = new List<Branch>() },
        };
        var companyReadRepo = new Mock<IReadRepository<Company>>();
        companyReadRepo.Setup(r => r.GetAllAsync(
                null,
                It.IsAny<Func<IQueryable<Company>, IIncludableQueryable<Company, object>>?>(),
                null, false, default))
            .ReturnsAsync(companies);
        var assignmentReadRepo = new Mock<IReadRepository<Assignment>>();
        assignmentReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<Expression<Func<Assignment, bool>>?>(), null, null, false, default))
            .ReturnsAsync(new List<Assignment>
            {
                new() { Id = 1, UserId = 1, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true },
                new() { Id = 2, UserId = 2, CompanyId = 1, BranchId = 1, Role = AssignmentRole.BranchManager, IsActive = true },
                new() { Id = 3, UserId = 3, CompanyId = 1, BranchId = 1, Role = AssignmentRole.Member, IsActive = true },
                new() { Id = 4, UserId = 4, CompanyId = 1, BranchId = 1, Role = AssignmentRole.Member, IsActive = true },
            });
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Company>()).Returns(companyReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(assignmentReadRepo.Object);

        var handler = new GetCompaniesQueryHandler(uow.Object);
        var result = await handler.Handle(new GetCompaniesQuery(), CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].BranchCount);
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
