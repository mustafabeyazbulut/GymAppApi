using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.ClassScheduling.Commands.CancelClassEnrollment;
using GymAppApi.Application.Features.ClassScheduling.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.ClassScheduling;

public class CancelClassEnrollmentCommandHandlerTests
{
    private const int MemberId = 7;
    private const int CallerId = 42;
    private const int CompanyId = 1;
    private const int BranchId = 10;
    private const int EnrollmentId = 200;

    // Handler'ın kendi hesapladığı "TR yerel saati" ile aynı formül - fixture'lar
    // gerçek çalışma anına göre deterministik kalsın diye.
    private static DateTime TurkeyNow => DateTime.UtcNow.AddHours(CancelClassEnrollmentCommandHandler.TurkeyUtcOffsetHours);

    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<ClassEnrollment>> enrollmentWriteRepo, Mock<IWriteRepository<PackageAssignment>> assignmentWriteRepo) Wire(
        ClassEnrollment? enrollment, IReadOnlyList<Assignment> callerAssignments)
    {
        var enrollmentReadRepo = new Mock<IReadRepository<ClassEnrollment>>();
        enrollmentReadRepo.Setup(r => r.GetAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<ClassEnrollment, bool>>>(),
                It.IsAny<Func<IQueryable<ClassEnrollment>, Microsoft.EntityFrameworkCore.Query.IIncludableQueryable<ClassEnrollment, object>>?>(), false, default))
            .ReturnsAsync(enrollment);
        var enrollmentWriteRepo = new Mock<IWriteRepository<ClassEnrollment>>();
        var assignmentWriteRepo = new Mock<IWriteRepository<PackageAssignment>>();

        var callerReadRepo = new Mock<IReadRepository<Assignment>>();
        callerReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);

        var uow = new Mock<IUnitOfWork>();

        uow.Setup(u => u.GetReadRepository<Company>()).Returns(GymAppApi.UnitTests.TestHelpers.TestCompanies.AllActive());
        // Kilitli (FOR UPDATE) okumalar: ders -> kayıt -> paket ataması.
        uow.Setup(u => u.GetForUpdateAsync<ClassSession>(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(enrollment?.ClassSession);
        uow.Setup(u => u.GetForUpdateAsync<ClassEnrollment>(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(enrollment);
        uow.Setup(u => u.GetForUpdateAsync<PackageAssignment>(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(enrollment?.PackageAssignment);
        uow.Setup(u => u.GetReadRepository<ClassEnrollment>()).Returns(enrollmentReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<ClassEnrollment>()).Returns(enrollmentWriteRepo.Object);
        uow.Setup(u => u.GetWriteRepository<PackageAssignment>()).Returns(assignmentWriteRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(callerReadRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        return (uow, enrollmentWriteRepo, assignmentWriteRepo);
    }

    // sessionStartsInHours saat sonra başlayan, cutoffHours iptal sınırı olan bir
    // ClassSession'a bağlı Reserved bir kayıt oluşturur.
    private static ClassEnrollment ReservedEnrollment(
        int sessionStartsInHours, int cutoffHours, PackageAssignment? packageAssignment = null)
    {
        var sessionStart = TurkeyNow.AddHours(sessionStartsInHours);
        var classSession = new ClassSession
        {
            Id = 300,
            CompanyId = CompanyId,
            BranchId = BranchId,
            TrainerUserId = 99,
            Category = ClassSessionCategory.GroupClass,
            Name = "Yoga",
            Date = DateOnly.FromDateTime(sessionStart),
            StartTime = TimeOnly.FromDateTime(sessionStart),
            EndTime = TimeOnly.FromDateTime(sessionStart.AddHours(1)),
            Capacity = 10,
            CancellationCutoffHours = cutoffHours,
        };

        return new ClassEnrollment
        {
            Id = EnrollmentId,
            ClassSessionId = classSession.Id,
            ClassSession = classSession,
            PackageAssignmentId = packageAssignment?.Id ?? 1,
            PackageAssignment = packageAssignment,
            MemberUserId = MemberId,
            CompanyId = CompanyId,
            BranchId = BranchId,
            Status = ClassEnrollmentStatus.Reserved,
        };
    }

    private static CancelClassEnrollmentCommand ValidCommand(int requestedBy) => new()
    {
        ClassEnrollmentId = EnrollmentId,
        RequestedByUserId = requestedBy,
    };

    [Fact]
    public async Task Handle_WhenEnrollmentDoesNotExist_ThrowsNotFoundException()
    {
        var (uow, _, _) = Wire(enrollment: null, callerAssignments: new List<Assignment>());
        var handler = new CancelClassEnrollmentCommandHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(ValidCommand(MemberId), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCallerIsUnrelated_ThrowsForbiddenException()
    {
        var enrollment = ReservedEnrollment(sessionStartsInHours: 10, cutoffHours: 2);
        var (uow, enrollmentWriteRepo, _) = Wire(enrollment, callerAssignments: new List<Assignment>());
        var handler = new CancelClassEnrollmentCommandHandler(uow.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(ValidCommand(CallerId), CancellationToken.None));
        enrollmentWriteRepo.Verify(r => r.Update(It.IsAny<ClassEnrollment>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenStaffOfTheSameCompanyCancels_Succeeds()
    {
        var enrollment = ReservedEnrollment(sessionStartsInHours: 10, cutoffHours: 2);
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = CompanyId, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, enrollmentWriteRepo, _) = Wire(enrollment, callerAssignments);
        var handler = new CancelClassEnrollmentCommandHandler(uow.Object);

        await handler.Handle(ValidCommand(CallerId), CancellationToken.None);

        enrollmentWriteRepo.Verify(r => r.Update(It.IsAny<ClassEnrollment>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenAlreadyCancelled_ThrowsClassEnrollmentNotReservedException()
    {
        var enrollment = ReservedEnrollment(sessionStartsInHours: 10, cutoffHours: 2);
        enrollment.Status = ClassEnrollmentStatus.Cancelled;
        var (uow, enrollmentWriteRepo, _) = Wire(enrollment, callerAssignments: new List<Assignment>());
        var handler = new CancelClassEnrollmentCommandHandler(uow.Object);

        await Assert.ThrowsAsync<ClassEnrollmentNotReservedException>(() => handler.Handle(ValidCommand(MemberId), CancellationToken.None));
        enrollmentWriteRepo.Verify(r => r.Update(It.IsAny<ClassEnrollment>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenCancelledBeforeCutoff_MarksCancelledAndRefundsRemainingSessions()
    {
        var assignment = new PackageAssignment { Id = 1, CompanyId = CompanyId, MemberUserId = MemberId, RemainingSessions = 3 };
        // Ders 10 saat sonra başlıyor, cutoff 2 saat - şu an cutoff'tan (8 saat
        // sonrasından) önce olduğu için iptal iade edilmeli.
        var enrollment = ReservedEnrollment(sessionStartsInHours: 10, cutoffHours: 2, packageAssignment: assignment);
        var (uow, enrollmentWriteRepo, assignmentWriteRepo) = Wire(enrollment, callerAssignments: new List<Assignment>());
        var handler = new CancelClassEnrollmentCommandHandler(uow.Object);

        await handler.Handle(ValidCommand(MemberId), CancellationToken.None);

        Assert.Equal(ClassEnrollmentStatus.Cancelled, enrollment.Status);
        Assert.NotNull(enrollment.CancelledAt);
        Assert.Equal(4, assignment.RemainingSessions);
        assignmentWriteRepo.Verify(r => r.Update(assignment), Times.Once);
        enrollmentWriteRepo.Verify(r => r.Update(enrollment), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCancelledAfterCutoff_MarksNoShowAndDoesNotRefund()
    {
        var assignment = new PackageAssignment { Id = 1, CompanyId = CompanyId, MemberUserId = MemberId, RemainingSessions = 3 };
        // Ders 1 saat sonra başlıyor, cutoff 2 saat - cutoff anı (1 saat önce)
        // zaten geçmiş, bu yüzden iptal NoShow sayılmalı, iade yok.
        var enrollment = ReservedEnrollment(sessionStartsInHours: 1, cutoffHours: 2, packageAssignment: assignment);
        var (uow, enrollmentWriteRepo, assignmentWriteRepo) = Wire(enrollment, callerAssignments: new List<Assignment>());
        var handler = new CancelClassEnrollmentCommandHandler(uow.Object);

        await handler.Handle(ValidCommand(MemberId), CancellationToken.None);

        Assert.Equal(ClassEnrollmentStatus.NoShow, enrollment.Status);
        Assert.NotNull(enrollment.CancelledAt);
        Assert.Equal(3, assignment.RemainingSessions);
        assignmentWriteRepo.Verify(r => r.Update(It.IsAny<PackageAssignment>()), Times.Never);
        enrollmentWriteRepo.Verify(r => r.Update(enrollment), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCancelledBeforeCutoffOnDurationPackage_DoesNotTouchRemainingSessions()
    {
        var assignment = new PackageAssignment { Id = 1, CompanyId = CompanyId, MemberUserId = MemberId, RemainingSessions = null };
        var enrollment = ReservedEnrollment(sessionStartsInHours: 10, cutoffHours: 2, packageAssignment: assignment);
        var (uow, _, assignmentWriteRepo) = Wire(enrollment, callerAssignments: new List<Assignment>());
        var handler = new CancelClassEnrollmentCommandHandler(uow.Object);

        await handler.Handle(ValidCommand(MemberId), CancellationToken.None);

        Assert.Null(assignment.RemainingSessions);
        assignmentWriteRepo.Verify(r => r.Update(It.IsAny<PackageAssignment>()), Times.Never);
    }
}
