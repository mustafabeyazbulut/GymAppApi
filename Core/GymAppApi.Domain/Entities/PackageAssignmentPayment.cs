using GymAppApi.Domain.Common;
using GymAppApi.Domain.Enums;

namespace GymAppApi.Domain.Entities;

public class PackageAssignmentPayment : EntityBase, ICompanyScoped
{
    public int PackageAssignmentId { get; set; }
    public PackageAssignment? PackageAssignment { get; set; }

    // Snapshot of PackageAssignment.CompanyId at record time - same pattern
    // as PackageAssignment itself snapshotting Package.CompanyId.
    public int CompanyId { get; set; }
    public Company? Company { get; set; }

    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public DateTime PaidAt { get; set; }
    public int RecordedByUserId { get; set; }
    public string? Note { get; set; }
}
