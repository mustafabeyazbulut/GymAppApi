using GymAppApi.Domain.Common;
using GymAppApi.Domain.Enums;

namespace GymAppApi.Domain.Entities;

// The security gate for CreateCompany's GymAdmin and AddStaffMember's
// Trainer/BranchManager flows: attaching an existing, already-registered user to a
// company/branch never happens immediately from the inviter's request alone
// — it only takes effect once the INVITEE proves control of their own phone
// by confirming this row's Code (see ConfirmAssignmentInvitationCommand).
// Not tied to Company/Branch tenant scoping (like OtpVerification/DeviceToken
// are user-owned, not company-owned) since the invitee may not have any
// assignment to that company yet — that's the whole point of this row.
public class PendingAssignmentInvitation : EntityBase
{
    public int TargetUserId { get; set; }
    public User? TargetUser { get; set; }

    public int CompanyId { get; set; }
    public int? BranchId { get; set; }
    public AssignmentRole Role { get; set; }
    public int RequestedByUserId { get; set; }

    public string Code { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public int AttemptCount { get; set; }
    public bool IsUsed { get; set; }
}
