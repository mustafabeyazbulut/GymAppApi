using System.IdentityModel.Tokens.Jwt;
using GymAppApi.Application.Features.ClassScheduling.Commands.CancelClassEnrollment;
using GymAppApi.Application.Features.ClassScheduling.Queries.GetMyClassEnrollments;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymAppApi.WebApi.Controllers;

// [Authorize] (not StaffManagement) - Cancel is reachable by the enrollment's
// own Member as well as staff; the real authorization is the handler's own
// explicit check, same shape as ReservationsController.
[ApiController]
[Route("api/class-enrollments")]
[Authorize]
public class ClassEnrollmentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ClassEnrollmentsController(IMediator mediator) => _mediator = mediator;

    private int CurrentUserId => int.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> Cancel(int id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new CancelClassEnrollmentCommand { ClassEnrollmentId = id, RequestedByUserId = CurrentUserId }, cancellationToken);
        return NoContent();
    }

    [HttpGet("mine")]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetMyClassEnrollmentsQuery(CurrentUserId), cancellationToken));
}
