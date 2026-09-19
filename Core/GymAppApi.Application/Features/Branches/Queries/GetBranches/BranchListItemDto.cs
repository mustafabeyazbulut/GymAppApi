namespace GymAppApi.Application.Features.Branches.Queries.GetBranches;

public class BranchListItemDto
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string Name { get; set; } = null!;
    public string Address { get; set; } = null!;
    public bool IsActive { get; set; }
    // Sadece GetCompanyDetailQueryHandler dolduruyor (SuperAdmin'in firma
    // detay ekranı için) - GetBranchesQueryHandler'ın "Şubelerim" ekranında
    // henüz kullanılmıyor, null kalması güvenli.
    public string? ManagerName { get; set; }
}
