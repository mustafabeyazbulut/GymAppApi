using FluentValidation;

namespace GymAppApi.Application.Features.Companies.Commands.CreateCompany;

public class CreateCompanyCommandValidator : AbstractValidator<CreateCompanyCommand>
{
    public CreateCompanyCommandValidator()
    {
        RuleFor(x => x.CompanyName).NotEmpty();
        RuleFor(x => x.BranchName).NotEmpty();
        RuleFor(x => x.BranchAddress).NotEmpty();
        RuleFor(x => x.GymAdminFullName).NotEmpty();
        RuleFor(x => x.GymAdminPhone).NotEmpty();
    }
}
