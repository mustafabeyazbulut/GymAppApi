using GymAppApi.Domain.Common;

namespace GymAppApi.Domain.Entities;

// The security gate for CreatePackageAssignmentCommand - a Package is never
// attached to a Member immediately from the inviter's request alone, it only
// takes effect once the MEMBER proves control of their own phone by
// confirming this row's Code (see ConfirmPackageAssignmentCommand). Mirrors
// PendingAssignmentInvitation exactly. Not tenant-scoped - the member may not
// have any assignment to that company yet, that's the whole point of this row.
public class PendingPackageAssignmentInvitation : EntityBase
{
    public int TargetUserId { get; set; }
    public User? TargetUser { get; set; }

    public int PackageId { get; set; }
    public Package? Package { get; set; }

    // Snapshot of Package.CompanyId/BranchId at issue time.
    public int CompanyId { get; set; }
    public int? BranchId { get; set; }
    public int RequestedByUserId { get; set; }

    public string Code { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public int AttemptCount { get; set; }
    public bool IsUsed { get; set; }
}
