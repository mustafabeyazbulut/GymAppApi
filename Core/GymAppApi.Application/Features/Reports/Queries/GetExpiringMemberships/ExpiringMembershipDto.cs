namespace GymAppApi.Application.Features.Reports.Queries.GetExpiringMemberships;

public class ExpiringMembershipDto
{
    public int PackageAssignmentId { get; set; }
    public string MemberFullName { get; set; } = null!;
    public string MemberPhone { get; set; } = null!;
    public string PackageName { get; set; } = null!;
    public string? BranchName { get; set; }
    public DateTime EndDate { get; set; }
    // Negatif = süresi zaten dolmuş (kaç gün önce). Pozitif = kaç gün sonra dolacak.
    public int DaysRemaining { get; set; }
}
