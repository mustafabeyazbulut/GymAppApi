using MediatR;

namespace GymAppApi.Application.Features.Assignments.Commands.ConfirmAssignmentInvitation;

public class ConfirmAssignmentInvitationCommand : IRequest<ConfirmAssignmentInvitationCommandResult>
{
    public string Code { get; set; } = null!;

    // Set by the controller from the caller's own JWT sub claim - only the
    // invited person themselves can confirm an invitation addressed to them.
    // This is the whole security point: knowing someone's phone number is
    // never enough to attach them to a company, only they can do that.
    public int UserId { get; set; }
}
