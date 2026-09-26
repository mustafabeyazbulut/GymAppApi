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

    // Deliberately no navigation property to Package - Package carries a
    // company-scoped query filter and this row is intentionally unscoped
    // (the target member may not have any assignment to that company yet),
    // so a required FK navigation between them would filter out the Package
    // for exactly the caller this row exists to serve. Same reasoning as
    // PendingAssignmentInvitation's plain CompanyId/BranchId ints below.
    public int PackageId { get; set; }

    // Snapshot of Package.CompanyId/BranchId at issue time.
    public int CompanyId { get; set; }
    public int? BranchId { get; set; }
    public int RequestedByUserId { get; set; }

    public string Code { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public int AttemptCount { get; set; }
    public bool IsUsed { get; set; }
    // Postgres xmin concurrency token (RefreshToken ile aynı desen, bkz.
    // RefreshTokenConfiguration): davetin "kullanıldı" işaretlemesi atomik olsun -
    // aynı davete eşzamanlı iki onayda kaybeden DbUpdateConcurrencyException alır
    // ve ikinci atama oluşmaz. Gerçek CLR özelliği olmak ZORUNDA (AsNoTracking
    // okuma -> Update() döngüsünde orijinal değeri taşıyabilsin).
    public uint ConcurrencyToken { get; set; }
}
