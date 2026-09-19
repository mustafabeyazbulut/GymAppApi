using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.ClassScheduling.Queries.GetClassSessions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.ClassScheduling;

public class GetClassSessionsQueryHandlerTests
{
    private static (Mock<IUnitOfWork> uow, Mock<IReadRepository<ClassEnrollment>> enrollmentReadRepo) Wire(
        IReadOnlyList<ClassSession> sessions, IReadOnlyList<ClassEnrollment> enrollments)
    {
        var sessionReadRepo = new Mock<IReadRepository<ClassSession>>();
        sessionReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<ClassSession, bool>>>(),
                It.IsAny<Func<IQueryable<ClassSession>, Microsoft.EntityFrameworkCore.Query.IIncludableQueryable<ClassSession, object>>?>(),
                It.IsAny<Func<IQueryable<ClassSession>, IOrderedQueryable<ClassSession>>?>(), false, default))
            .ReturnsAsync(sessions);

        var enrollmentReadRepo = new Mock<IReadRepository<ClassEnrollment>>();
        enrollmentReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<ClassEnrollment, bool>>>(),
                It.IsAny<Func<IQueryable<ClassEnrollment>, Microsoft.EntityFrameworkCore.Query.IIncludableQueryable<ClassEnrollment, object>>?>(), null, false, default))
            .ReturnsAsync(enrollments);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<ClassSession>()).Returns(sessionReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<ClassEnrollment>()).Returns(enrollmentReadRepo.Object);
        return (uow, enrollmentReadRepo);
    }

    [Fact]
    public async Task Handle_WhenNoSessionsMatch_ReturnsEmptyListAndDoesNotQueryEnrollments()
    {
        var (uow, enrollmentReadRepo) = Wire(sessions: new List<ClassSession>(), enrollments: new List<ClassEnrollment>());
        var handler = new GetClassSessionsQueryHandler(uow.Object);

        var result = await handler.Handle(new GetClassSessionsQuery(), CancellationToken.None);

        Assert.Empty(result);
        enrollmentReadRepo.Verify(r => r.GetAllAsync(
            It.IsAny<System.Linq.Expressions.Expression<Func<ClassEnrollment, bool>>>(),
            It.IsAny<Func<IQueryable<ClassEnrollment>, Microsoft.EntityFrameworkCore.Query.IIncludableQueryable<ClassEnrollment, object>>?>(), null, false, default),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsEachSessionsEnrolledCount_FromASingleBulkQuery()
    {
        var sessions = new List<ClassSession>
        {
            new() { Id = 1, BranchId = 10, TrainerUserId = 99, Category = ClassSessionCategory.GroupClass, Name = "Yoga", Date = new DateOnly(2026, 9, 21), StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(10, 0), Capacity = 2, CancellationCutoffHours = 2 },
            new() { Id = 2, BranchId = 10, TrainerUserId = 99, Category = ClassSessionCategory.MartialArts, Name = "Karate", Date = new DateOnly(2026, 9, 22), StartTime = new TimeOnly(18, 0), EndTime = new TimeOnly(19, 0), Capacity = 5, CancellationCutoffHours = 2 },
        };
        // Mock, gerçek repository'nin predicate'i (Reserved/Attended) zaten
        // uygulanmış hâliyle döneceği listeyi temsil eder - Cancelled/NoShow
        // kayıtlar SQL WHERE ile zaten elenmiş olurdu, bu yüzden burada hiç yok.
        var enrollments = new List<ClassEnrollment>
        {
            new() { Id = 1, ClassSessionId = 1, Status = ClassEnrollmentStatus.Reserved },
            new() { Id = 2, ClassSessionId = 1, Status = ClassEnrollmentStatus.Attended },
        };
        var (uow, _) = Wire(sessions, enrollments);
        var handler = new GetClassSessionsQueryHandler(uow.Object);

        var result = await handler.Handle(new GetClassSessionsQuery(), CancellationToken.None);

        Assert.Equal(2, result.Count);
        // Session 1: iki aktif kayıt var -> 2.
        Assert.Equal(2, result.Single(s => s.Id == 1).EnrolledCount);
        // Session 2: hiç aktif kayıt yok -> 0.
        Assert.Equal(0, result.Single(s => s.Id == 2).EnrolledCount);
    }
}
