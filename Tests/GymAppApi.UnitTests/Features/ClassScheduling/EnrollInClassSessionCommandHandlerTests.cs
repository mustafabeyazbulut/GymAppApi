using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.ClassScheduling.Commands.EnrollInClassSession;
using GymAppApi.Application.Features.ClassScheduling.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.ClassScheduling;

public class EnrollInClassSessionCommandHandlerTests
{
    private const int MemberId = 7;
    private const int OtherMemberId = 8;
    private const int CompanyId = 1;
    private const int BranchId = 10;
    private const int ClassSessionId = 100;
    private const int PackageAssignmentId = 1;

    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<ClassEnrollment>> enrollmentWriteRepo, Mock<IWriteRepository<PackageAssignment>> assignmentWriteRepo) Wire(
        PackageAssignment? assignment, ClassSession? classSession, IReadOnlyList<ClassEnrollment> existingEnrollments)
    {
        var assignmentReadRepo = new Mock<IReadRepository<PackageAssignment>>();
        assignmentReadRepo.Setup(r => r.GetAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<PackageAssignment, bool>>>(),
                It.IsAny<Func<IQueryable<PackageAssignment>, Microsoft.EntityFrameworkCore.Query.IIncludableQueryable<PackageAssignment, object>>?>(), false, default))
            .ReturnsAsync(assignment);
        var assignmentWriteRepo = new Mock<IWriteRepository<PackageAssignment>>();

        var enrollmentReadRepo = new Mock<IReadRepository<ClassEnrollment>>();
        enrollmentReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<ClassEnrollment, bool>>>(),
                It.IsAny<Func<IQueryable<ClassEnrollment>, Microsoft.EntityFrameworkCore.Query.IIncludableQueryable<ClassEnrollment, object>>?>(), null, false, default))
            .ReturnsAsync(existingEnrollments);
        var enrollmentWriteRepo = new Mock<IWriteRepository<ClassEnrollment>>();

        var uow = new Mock<IUnitOfWork>();

        uow.Setup(u => u.GetReadRepository<Company>()).Returns(GymAppApi.UnitTests.TestHelpers.TestCompanies.AllActive());

        // Kilitli (FOR UPDATE) okuma - seans hakkı yarışı düzeltmesi.

        uow.Setup(u => u.GetForUpdateAsync<PackageAssignment>(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(assignment);
        uow.Setup(u => u.GetReadRepository<PackageAssignment>()).Returns(assignmentReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<PackageAssignment>()).Returns(assignmentWriteRepo.Object);
        uow.Setup(u => u.GetReadRepository<ClassEnrollment>()).Returns(enrollmentReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<ClassEnrollment>()).Returns(enrollmentWriteRepo.Object);
        uow.Setup(u => u.GetForUpdateAsync<ClassSession>(ClassSessionId, default)).ReturnsAsync(classSession);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        return (uow, enrollmentWriteRepo, assignmentWriteRepo);
    }

    private static ClassSession EligibleSession(int capacity = 10) => new()
    {
        Id = ClassSessionId,
        CompanyId = CompanyId,
        BranchId = BranchId,
        TrainerUserId = 99,
        Category = ClassSessionCategory.GroupClass,
        Name = "Yoga",
        Date = new DateOnly(2026, 9, 25),
        StartTime = new TimeOnly(9, 0),
        EndTime = new TimeOnly(10, 0),
        Capacity = capacity,
        CancellationCutoffHours = 2,
    };

    private static PackageAssignment SessionBasedAssignment(int remainingSessions = 5) => new()
    {
        Id = PackageAssignmentId,
        CompanyId = CompanyId,
        BranchId = BranchId,
        MemberUserId = MemberId,
        Status = PackageAssignmentStatus.Active,
        RemainingSessions = remainingSessions,
        Package = new Package
        {
            Id = 5, CompanyId = CompanyId, Name = "Grup Dersi Paketi",
            Type = PackageType.SessionBased, SessionCount = 10, Price = 500m,
            Category = ClassSessionCategory.GroupClass,
        },
    };

    private static PackageAssignment DurationAssignment(DateTime? endDate) => new()
    {
        Id = PackageAssignmentId,
        CompanyId = CompanyId,
        BranchId = BranchId,
        MemberUserId = MemberId,
        Status = PackageAssignmentStatus.Active,
        RemainingSessions = null,
        EndDate = endDate,
        Package = new Package
        {
            Id = 6, CompanyId = CompanyId, Name = "Aylık Grup Dersi",
            Type = PackageType.Duration, DurationDays = 30, Price = 800m,
            Category = ClassSessionCategory.GroupClass,
        },
    };

    private static EnrollInClassSessionCommand ValidCommand(int requestedBy) => new()
    {
        ClassSessionId = ClassSessionId,
        PackageAssignmentId = PackageAssignmentId,
        RequestedByUserId = requestedBy,
    };

    [Fact]
    public async Task Handle_WhenPackageAssignmentDoesNotExist_ThrowsNotFoundException()
    {
        var (uow, enrollmentWriteRepo, _) = Wire(assignment: null, classSession: EligibleSession(), existingEnrollments: new List<ClassEnrollment>());
        var handler = new EnrollInClassSessionCommandHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(ValidCommand(MemberId), CancellationToken.None));
        enrollmentWriteRepo.Verify(r => r.AddAsync(It.IsAny<ClassEnrollment>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenCallerIsNotTheAssignmentsOwnMember_ThrowsForbiddenException()
    {
        var (uow, enrollmentWriteRepo, _) = Wire(SessionBasedAssignment(), EligibleSession(), new List<ClassEnrollment>());
        var handler = new EnrollInClassSessionCommandHandler(uow.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(ValidCommand(OtherMemberId), CancellationToken.None));
        enrollmentWriteRepo.Verify(r => r.AddAsync(It.IsAny<ClassEnrollment>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenClassSessionDoesNotExist_ThrowsNotFoundException()
    {
        var (uow, enrollmentWriteRepo, _) = Wire(SessionBasedAssignment(), classSession: null, existingEnrollments: new List<ClassEnrollment>());
        var handler = new EnrollInClassSessionCommandHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(ValidCommand(MemberId), CancellationToken.None));
        enrollmentWriteRepo.Verify(r => r.AddAsync(It.IsAny<ClassEnrollment>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenPackageCategoryDoesNotMatchClassSessionCategory_ThrowsPackageAssignmentNotEligibleForClassException()
    {
        var assignment = SessionBasedAssignment();
        assignment.Package!.Category = ClassSessionCategory.MartialArts;
        var (uow, enrollmentWriteRepo, _) = Wire(assignment, EligibleSession(), new List<ClassEnrollment>());
        var handler = new EnrollInClassSessionCommandHandler(uow.Object);

        await Assert.ThrowsAsync<PackageAssignmentNotEligibleForClassException>(() => handler.Handle(ValidCommand(MemberId), CancellationToken.None));
        enrollmentWriteRepo.Verify(r => r.AddAsync(It.IsAny<ClassEnrollment>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenSessionBasedAssignmentHasNoRemainingSessions_ThrowsPackageAssignmentNotEligibleForClassException()
    {
        var assignment = SessionBasedAssignment(remainingSessions: 0);
        var (uow, enrollmentWriteRepo, _) = Wire(assignment, EligibleSession(), new List<ClassEnrollment>());
        var handler = new EnrollInClassSessionCommandHandler(uow.Object);

        await Assert.ThrowsAsync<PackageAssignmentNotEligibleForClassException>(() => handler.Handle(ValidCommand(MemberId), CancellationToken.None));
        enrollmentWriteRepo.Verify(r => r.AddAsync(It.IsAny<ClassEnrollment>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenSessionBasedAssignmentHasSessionsLeftButHasExpired_ThrowsPackageAssignmentNotEligibleForClassException()
    {
        // "60 gün içinde kullan" tipi seans paketi: hakkı kalmış ama süresi
        // dolmuş - ortak "geçerli paket" tanımına göre artık kullanılamaz.
        var assignment = SessionBasedAssignment(remainingSessions: 5);
        assignment.EndDate = DateTime.UtcNow.AddDays(-1);
        var (uow, enrollmentWriteRepo, _) = Wire(assignment, EligibleSession(), new List<ClassEnrollment>());
        var handler = new EnrollInClassSessionCommandHandler(uow.Object);

        await Assert.ThrowsAsync<PackageAssignmentNotEligibleForClassException>(() => handler.Handle(ValidCommand(MemberId), CancellationToken.None));
        enrollmentWriteRepo.Verify(r => r.AddAsync(It.IsAny<ClassEnrollment>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenDurationAssignmentHasExpired_ThrowsPackageAssignmentNotEligibleForClassException()
    {
        var assignment = DurationAssignment(endDate: DateTime.UtcNow.AddDays(-1));
        var (uow, enrollmentWriteRepo, _) = Wire(assignment, EligibleSession(), new List<ClassEnrollment>());
        var handler = new EnrollInClassSessionCommandHandler(uow.Object);

        await Assert.ThrowsAsync<PackageAssignmentNotEligibleForClassException>(() => handler.Handle(ValidCommand(MemberId), CancellationToken.None));
        enrollmentWriteRepo.Verify(r => r.AddAsync(It.IsAny<ClassEnrollment>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenAlreadyEnrolled_ThrowsAlreadyEnrolledInClassSessionException()
    {
        var existing = new List<ClassEnrollment>
        {
            new() { Id = 50, ClassSessionId = ClassSessionId, PackageAssignmentId = PackageAssignmentId, Status = ClassEnrollmentStatus.Reserved },
        };
        var (uow, enrollmentWriteRepo, _) = Wire(SessionBasedAssignment(), EligibleSession(), existing);
        var handler = new EnrollInClassSessionCommandHandler(uow.Object);

        await Assert.ThrowsAsync<AlreadyEnrolledInClassSessionException>(() => handler.Handle(ValidCommand(MemberId), CancellationToken.None));
        enrollmentWriteRepo.Verify(r => r.AddAsync(It.IsAny<ClassEnrollment>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenClassSessionIsFull_ThrowsClassSessionFullException()
    {
        var existing = new List<ClassEnrollment>
        {
            new() { Id = 51, ClassSessionId = ClassSessionId, PackageAssignmentId = 999, Status = ClassEnrollmentStatus.Reserved },
        };
        var (uow, enrollmentWriteRepo, _) = Wire(SessionBasedAssignment(), EligibleSession(capacity: 1), existing);
        var handler = new EnrollInClassSessionCommandHandler(uow.Object);

        await Assert.ThrowsAsync<ClassSessionFullException>(() => handler.Handle(ValidCommand(MemberId), CancellationToken.None));
        enrollmentWriteRepo.Verify(r => r.AddAsync(It.IsAny<ClassEnrollment>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenSessionBasedAssignmentIsEligible_CreatesEnrollmentAndDecrementsRemainingSessions()
    {
        var assignment = SessionBasedAssignment(remainingSessions: 5);
        var (uow, enrollmentWriteRepo, assignmentWriteRepo) = Wire(assignment, EligibleSession(), new List<ClassEnrollment>());
        var handler = new EnrollInClassSessionCommandHandler(uow.Object);

        var result = await handler.Handle(ValidCommand(MemberId), CancellationToken.None);

        Assert.NotNull(result.ReservedAt);
        Assert.Equal(4, assignment.RemainingSessions);
        assignmentWriteRepo.Verify(r => r.Update(assignment), Times.Once);
        enrollmentWriteRepo.Verify(r => r.AddAsync(It.Is<ClassEnrollment>(e =>
            e.ClassSessionId == ClassSessionId && e.PackageAssignmentId == PackageAssignmentId &&
            e.MemberUserId == MemberId && e.CompanyId == CompanyId && e.BranchId == BranchId &&
            e.Status == ClassEnrollmentStatus.Reserved), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenDurationAssignmentIsEligible_CreatesEnrollmentWithoutTouchingRemainingSessions()
    {
        var assignment = DurationAssignment(endDate: DateTime.UtcNow.AddDays(10));
        var (uow, enrollmentWriteRepo, assignmentWriteRepo) = Wire(assignment, EligibleSession(), new List<ClassEnrollment>());
        var handler = new EnrollInClassSessionCommandHandler(uow.Object);

        await handler.Handle(ValidCommand(MemberId), CancellationToken.None);

        Assert.Null(assignment.RemainingSessions);
        assignmentWriteRepo.Verify(r => r.Update(It.IsAny<PackageAssignment>()), Times.Never);
        enrollmentWriteRepo.Verify(r => r.AddAsync(It.IsAny<ClassEnrollment>(), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenDurationAssignmentHasNoEndDate_IsEligible()
    {
        var assignment = DurationAssignment(endDate: null);
        var (uow, enrollmentWriteRepo, _) = Wire(assignment, EligibleSession(), new List<ClassEnrollment>());
        var handler = new EnrollInClassSessionCommandHandler(uow.Object);

        await handler.Handle(ValidCommand(MemberId), CancellationToken.None);

        enrollmentWriteRepo.Verify(r => r.AddAsync(It.IsAny<ClassEnrollment>(), default), Times.Once);
    }
}
