using MediatR;

namespace GymAppApi.Application.Features.ClassScheduling.Commands.CancelClassEnrollment;

public class CancelClassEnrollmentCommand : IRequest
{
    // Set by the controller from the route segment.
    public int ClassEnrollmentId { get; set; }
    // Set by the controller from the caller's own JWT sub claim.
    public int RequestedByUserId { get; set; }
}
