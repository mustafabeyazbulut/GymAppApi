using GymAppApi.Domain.Common;
using GymAppApi.Domain.Enums;

namespace GymAppApi.Domain.Entities;

public class ClassEnrollment : EntityBase, ICompanyScoped
{
    public int ClassSessionId { get; set; }
    public ClassSession? ClassSession { get; set; }

    public int PackageAssignmentId { get; set; }
    public PackageAssignment? PackageAssignment { get; set; }

    // Snapshot of ClassSession.CompanyId/BranchId ve PackageAssignment.MemberUserId
    // kayıt anında - Reservation'ın PackageAssignment'ı snapshot'lamasıyla aynı desen.
    public int MemberUserId { get; set; }
    public int CompanyId { get; set; }
    public int? BranchId { get; set; }

    public ClassEnrollmentStatus Status { get; set; } = ClassEnrollmentStatus.Reserved;
    public DateTime? ReservedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
}
