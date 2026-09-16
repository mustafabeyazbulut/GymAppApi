using FluentValidation;

namespace GymAppApi.Application.Features.Auth.Commands.FreezeAccount;

public class FreezeAccountCommandValidator : AbstractValidator<FreezeAccountCommand>
{
    public FreezeAccountCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().Matches(@"^\d{6}$");
    }
}
