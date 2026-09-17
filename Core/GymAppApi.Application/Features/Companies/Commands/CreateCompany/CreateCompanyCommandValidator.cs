using FluentValidation;

namespace GymAppApi.Application.Features.Companies.Commands.CreateCompany;

public class CreateCompanyCommandValidator : AbstractValidator<CreateCompanyCommand>
{
    public CreateCompanyCommandValidator()
    {
        RuleFor(x => x.CompanyName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.GymAdminPhone).NotEmpty().Matches(@"^\+[1-9]\d{7,14}$");
    }
}
