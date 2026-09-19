namespace GymAppApi.Application.Features.Companies.Queries.GetCompanyDetail;

public class CompanyGymAdminDto
{
    public int UserId { get; set; }
    public string FullName { get; set; } = null!;
    public string Phone { get; set; } = null!;
}
