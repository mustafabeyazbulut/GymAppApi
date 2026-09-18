using GymAppApi.Domain.Common;

namespace GymAppApi.Domain.Entities;

// One row per actual visit - both reservation-fulfilling (ReservationId set)
// and walk-in/no-reservation (ReservationId null) check-ins land here, so
// this is the single source of truth for a PackageAssignment's attendance
// history regardless of how the visit was checked in.
public class CheckIn : EntityBase, ICompanyScoped
{
    public int PackageAssignmentId { get; set; }
    public PackageAssignment? PackageAssignment { get; set; }
    public int? ReservationId { get; set; }
    public Reservation? Reservation { get; set; }

    // Snapshot of PackageAssignment.CompanyId/BranchId at check-in time.
    public int CompanyId { get; set; }
    public int? BranchId { get; set; }

    public DateTime CheckedInAt { get; set; }
    public int RecordedByUserId { get; set; }
}
