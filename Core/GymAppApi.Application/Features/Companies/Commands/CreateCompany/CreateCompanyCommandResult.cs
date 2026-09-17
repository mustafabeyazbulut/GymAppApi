namespace GymAppApi.Application.Features.Companies.Commands.CreateCompany;

public class CreateCompanyCommandResult
{
    public int CompanyId { get; set; }
    public int GymAdminUserId { get; set; }
    public string GymAdminPhone { get; set; } = null!;
}
