using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.ClassScheduling.Queries.GetMyClassEnrollments;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.ClassScheduling;

public class GetMyClassEnrollmentsQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsOnlyTheCallersOwnEnrollments_OrderedByMostRecentSessionFirst()
    {
        const int memberId = 7;
        var earlierSession = new ClassSession { Id = 1, Name = "Yoga", Category = ClassSessionCategory.GroupClass, Date = new DateOnly(2026, 9, 20), StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(10, 0) };
        var laterSession = new ClassSession { Id = 2, Name = "Karate", Category = ClassSessionCategory.MartialArts, Date = new DateOnly(2026, 9, 25), StartTime = new TimeOnly(18, 0), EndTime = new TimeOnly(19, 0) };

        var enrollments = new List<ClassEnrollment>
        {
            new() { Id = 10, MemberUserId = memberId, ClassSessionId = 1, ClassSession = earlierSession, Status = ClassEnrollmentStatus.Attended },
            new() { Id = 11, MemberUserId = memberId, ClassSessionId = 2, ClassSession = laterSession, Status = ClassEnrollmentStatus.Reserved },
        };

        var readRepo = new Mock<IReadRepository<ClassEnrollment>>();
        readRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<ClassEnrollment, bool>>>(),
                It.IsAny<Func<IQueryable<ClassEnrollment>, Microsoft.EntityFrameworkCore.Query.IIncludableQueryable<ClassEnrollment, object>>?>(), null, false, default))
            .ReturnsAsync(enrollments);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<ClassEnrollment>()).Returns(readRepo.Object);

        var handler = new GetMyClassEnrollmentsQueryHandler(uow.Object);
        var result = await handler.Handle(new GetMyClassEnrollmentsQuery(memberId), CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("Karate", result[0].ClassName);
        Assert.Equal("Reserved", result[0].Status);
        Assert.Equal("Yoga", result[1].ClassName);
        Assert.Equal("Attended", result[1].Status);
    }
}
