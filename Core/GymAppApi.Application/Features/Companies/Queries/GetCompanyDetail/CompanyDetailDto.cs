using GymAppApi.Application.Features.Branches.Queries.GetBranches;

namespace GymAppApi.Application.Features.Companies.Queries.GetCompanyDetail;

public class CompanyDetailDto
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public bool IsActive { get; set; }
    public List<BranchListItemDto> Branches { get; set; } = new();
    // Branches listesi kapatılmışları da (IsActive=false) içerir; sayılar ayrık.
    public int BranchCount { get; set; }
    public int InactiveBranchCount { get; set; }
    public List<CompanyGymAdminDto> GymAdmins { get; set; } = new();
    public int GymAdminCount { get; set; }
    public int BranchManagerCount { get; set; }
    public int TrainerCount { get; set; }
    public int MemberCount { get; set; }
}
