using GymAppApi.Domain.Common;
using GymAppApi.Domain.Enums;

namespace GymAppApi.Domain.Entities;

public class Reservation : EntityBase, ICompanyScoped
{
    public int PackageAssignmentId { get; set; }
    public PackageAssignment? PackageAssignment { get; set; }

    // Snapshot of PackageAssignment.MemberUserId/CompanyId/BranchId at
    // creation time - same pattern as PackageAssignment itself snapshotting
    // Package's scope.
    public int MemberUserId { get; set; }
    public int TrainerId { get; set; }
    public int CompanyId { get; set; }
    public int? BranchId { get; set; }

    public DateTime ScheduledAt { get; set; }
    public ReservationStatus Status { get; set; } = ReservationStatus.Booked;
    public int CreatedByUserId { get; set; }

    // Lets staff check the member in by code (manual entry or scanned) instead
    // of looking the reservation up by id - only meaningful while Status is
    // still Booked, see CheckInReservationByCodeCommandHandler.
    public string QrCode { get; set; } = null!;
}
