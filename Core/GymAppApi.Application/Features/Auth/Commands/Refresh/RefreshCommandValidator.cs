using FluentValidation;

namespace GymAppApi.Application.Features.Auth.Commands.Refresh;

public class RefreshCommandValidator : AbstractValidator<RefreshCommand>
{
    public RefreshCommandValidator() => RuleFor(x => x.RefreshToken).NotEmpty();
}
