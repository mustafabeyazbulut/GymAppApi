using GymAppApi.Application.Common.PackageAssignments;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;

namespace GymAppApi.UnitTests.Common.PackageAssignments;

public class PackageAssignmentValidityTests
{
    private static readonly DateTime Now = new(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);

    private static PackageAssignment Assignment(
        PackageAssignmentStatus status = PackageAssignmentStatus.Active, DateTime? endDate = null, int? remainingSessions = null, int memberUserId = 7) => new()
    {
        MemberUserId = memberUserId, Status = status, EndDate = endDate, RemainingSessions = remainingSessions,
    };

    public static TheoryData<PackageAssignment, bool> Cases => new()
    {
        // Süresiz, seanssız aktif paket.
        { Assignment(), true },
        // Bitişi gelecekte.
        { Assignment(endDate: Now.AddDays(1)), true },
        // Süresi dolmuş ama Status hâlâ Active (Expired saklanan bir durum değil).
        { Assignment(endDate: Now.AddDays(-1)), false },
        // Bitiş tam şimdi -> artık geçerli değil.
        { Assignment(endDate: Now), false },
        // Seans bazlı, hakkı var.
        { Assignment(remainingSessions: 3), true },
        // Seans bazlı, hakkı bitmiş.
        { Assignment(remainingSessions: 0), false },
        // Seans bazlı, hakkı var ama süresi dolmuş ("60 gün içinde kullan").
        { Assignment(remainingSessions: 3, endDate: Now.AddDays(-1)), false },
        // Dondurulmuş / iptal.
        { Assignment(status: PackageAssignmentStatus.Frozen), false },
        { Assignment(status: PackageAssignmentStatus.Cancelled), false },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void IsUsable_AppliesTheSingleValidPackageDefinition(PackageAssignment assignment, bool expected)
    {
        Assert.Equal(expected, PackageAssignmentValidity.IsUsable(assignment, Now));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void UsableOwnedBy_ExpressionAgreesWithIsUsable(PackageAssignment assignment, bool expected)
    {
        var predicate = PackageAssignmentValidity.UsableOwnedBy(assignment.MemberUserId, Now).Compile();

        Assert.Equal(expected, predicate(assignment));
    }

    [Fact]
    public void UsableOwnedBy_ExcludesAnotherMembersValidPackage()
    {
        var predicate = PackageAssignmentValidity.UsableOwnedBy(memberUserId: 7, Now).Compile();

        Assert.False(predicate(Assignment(memberUserId: 8)));
    }
}
