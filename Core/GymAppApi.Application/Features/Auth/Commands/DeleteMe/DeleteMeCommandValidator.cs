using FluentValidation;

namespace GymAppApi.Application.Features.Auth.Commands.DeleteMe;

public class DeleteMeCommandValidator : AbstractValidator<DeleteMeCommand>
{
    public DeleteMeCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().Matches(@"^\d{6}$");
    }
}
