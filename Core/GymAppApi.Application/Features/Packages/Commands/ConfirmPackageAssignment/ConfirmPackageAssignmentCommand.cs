using MediatR;

namespace GymAppApi.Application.Features.Packages.Commands.ConfirmPackageAssignment;

public class ConfirmPackageAssignmentCommand : IRequest<ConfirmPackageAssignmentCommandResult>
{
    public string Code { get; set; } = null!;

    // Set by the controller from the caller's own JWT sub claim - only the
    // invited member themselves can confirm an invitation addressed to them.
    public int UserId { get; set; }
}
