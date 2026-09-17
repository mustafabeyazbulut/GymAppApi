using FluentValidation;

namespace GymAppApi.Application.Features.Companies.Commands.UpdateCompanyName;

public class UpdateCompanyNameCommandValidator : AbstractValidator<UpdateCompanyNameCommand>
{
    public UpdateCompanyNameCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
    }
}
