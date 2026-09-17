using FluentValidation;

namespace GymAppApi.Application.Features.Assignments.Commands.ConfirmAssignmentInvitation;

public class ConfirmAssignmentInvitationCommandValidator : AbstractValidator<ConfirmAssignmentInvitationCommand>
{
    public ConfirmAssignmentInvitationCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().Length(6);
    }
}
