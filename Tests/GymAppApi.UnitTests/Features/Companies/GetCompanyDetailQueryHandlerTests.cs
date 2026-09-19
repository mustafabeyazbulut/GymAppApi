using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Companies.Queries.GetCompanyDetail;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Microsoft.EntityFrameworkCore.Query;
using Moq;

namespace GymAppApi.UnitTests.Features.Companies;

public class GetCompanyDetailQueryHandlerTests
{
    [Fact]
    public async Task Handle_WhenCompanyNotFound_ThrowsNotFoundException()
    {
        var companyReadRepo = new Mock<IReadRepository<Company>>();
        companyReadRepo.Setup(r => r.GetAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Company, bool>>>(),
                It.IsAny<Func<IQueryable<Company>, IIncludableQueryable<Company, object>>?>(), false, default))
            .ReturnsAsync((Company?)null);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Company>()).Returns(companyReadRepo.Object);

        var handler = new GetCompanyDetailQueryHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new GetCompanyDetailQuery(1), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_ReturnsCompanyWithItsBranches()
    {
        var company = new Company
        {
            Id = 1, Name = "MAT & MOVE", IsActive = true,
            Branches = new List<Branch> { new() { Id = 5, CompanyId = 1, Name = "Kadıköy", Address = "Adres 1", IsActive = true } },
        };
        var companyReadRepo = new Mock<IReadRepository<Company>>();
        companyReadRepo.Setup(r => r.GetAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Company, bool>>>(),
                It.IsAny<Func<IQueryable<Company>, IIncludableQueryable<Company, object>>?>(), false, default))
            .ReturnsAsync(company);
        var assignmentReadRepo = new Mock<IReadRepository<Assignment>>();
        assignmentReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>?>(),
                It.IsAny<Func<IQueryable<Assignment>, IIncludableQueryable<Assignment, object>>?>(), null, false, default))
            .ReturnsAsync(new List<Assignment>
            {
                new() { Id = 1, UserId = 1, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true },
                new()
                {
                    Id = 2, UserId = 2, CompanyId = 1, BranchId = 5, Role = AssignmentRole.BranchManager, IsActive = true,
                    User = new User { Id = 2, FullName = "Ayşe Yılmaz", Phone = "+905551112233", PasswordHash = "x" },
                },
                new() { Id = 3, UserId = 3, CompanyId = 1, BranchId = 5, Role = AssignmentRole.Trainer, IsActive = true },
                new() { Id = 4, UserId = 4, CompanyId = 1, BranchId = 5, Role = AssignmentRole.Member, IsActive = true },
            });
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Company>()).Returns(companyReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(assignmentReadRepo.Object);

        var handler = new GetCompanyDetailQueryHandler(uow.Object);
        var result = await handler.Handle(new GetCompanyDetailQuery(1), CancellationToken.None);

        Assert.Equal("MAT & MOVE", result.Name);
        var branch = Assert.Single(result.Branches);
        Assert.Equal("Kadıköy", branch.Name);
        Assert.Equal("Ayşe Yılmaz", branch.ManagerName);
        Assert.Equal(1, result.GymAdminCount);
        Assert.Equal(1, result.BranchManagerCount);
        Assert.Equal(1, result.TrainerCount);
        Assert.Equal(1, result.MemberCount);
    }
}
