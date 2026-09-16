using FluentValidation;

namespace GymAppApi.Application.Features.Auth.Commands.UnfreezeAccount;

public class UnfreezeAccountCommandValidator : AbstractValidator<UnfreezeAccountCommand>
{
    public UnfreezeAccountCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().Matches(@"^\d{6}$");
    }
}
