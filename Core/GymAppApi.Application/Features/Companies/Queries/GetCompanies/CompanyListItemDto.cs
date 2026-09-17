namespace GymAppApi.Application.Features.Companies.Queries.GetCompanies;

public class CompanyListItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public bool IsActive { get; set; }
    public int BranchCount { get; set; }
}
