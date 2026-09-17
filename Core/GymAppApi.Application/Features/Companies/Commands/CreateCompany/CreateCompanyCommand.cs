using MediatR;

namespace GymAppApi.Application.Features.Companies.Commands.CreateCompany;

public class CreateCompanyCommand : IRequest<CreateCompanyCommandResult>
{
    public string CompanyName { get; set; } = null!;
    public string BranchName { get; set; } = null!;
    public string BranchAddress { get; set; } = null!;
    public string GymAdminFullName { get; set; } = null!;
    public string GymAdminPhone { get; set; } = null!;
    public string? GymAdminEmail { get; set; }
}
