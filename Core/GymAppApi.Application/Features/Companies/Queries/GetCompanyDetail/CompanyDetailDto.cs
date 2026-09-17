using GymAppApi.Application.Features.Branches.Queries.GetBranches;

namespace GymAppApi.Application.Features.Companies.Queries.GetCompanyDetail;

public class CompanyDetailDto
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public bool IsActive { get; set; }
    public List<BranchListItemDto> Branches { get; set; } = new();
}
