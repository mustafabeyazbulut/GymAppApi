using FluentValidation;

namespace GymAppApi.Application.Features.Companies.Commands.CreateCompany;

public class CreateCompanyCommandValidator : AbstractValidator<CreateCompanyCommand>
{
    public CreateCompanyCommandValidator()
    {
        RuleFor(x => x.CompanyName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.BranchName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.BranchAddress).NotEmpty().MaximumLength(500);
        RuleFor(x => x.GymAdminFullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.GymAdminPhone).NotEmpty().Matches(@"^\+[1-9]\d{7,14}$");
        RuleFor(x => x.GymAdminEmail).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.GymAdminEmail));
    }
}
