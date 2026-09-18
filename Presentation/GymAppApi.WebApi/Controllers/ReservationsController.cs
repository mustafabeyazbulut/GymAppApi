using System.IdentityModel.Tokens.Jwt;
using GymAppApi.Application.Features.Reservations.Commands.CancelReservation;
using GymAppApi.Application.Features.Reservations.Commands.CheckInReservation;
using GymAppApi.Application.Features.Reservations.Commands.CheckInReservationByCode;
using GymAppApi.Application.Features.Reservations.Commands.CreateReservation;
using GymAppApi.Application.Features.Reservations.Commands.MarkReservationNoShow;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymAppApi.WebApi.Controllers;

// [Authorize] (not StaffManagement) on every action here - every one of these
// is reachable by a caller who isn't BranchManager/GymAdmin/SuperAdmin (a
// Member booking/cancelling their own reservation, or a Trainer acting on
// their own reservation) - the real authorization is each handler's own
// explicit check, not the controller-level policy.
[ApiController]
[Route("api/reservations")]
[Authorize]
public class ReservationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ReservationsController(IMediator mediator) => _mediator = mediator;

    private int CurrentUserId => int.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    [HttpPost]
    public async Task<IActionResult> Create(CreateReservationCommand command, CancellationToken cancellationToken)
    {
        command.RequestedByUserId = CurrentUserId;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> Cancel(int id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new CancelReservationCommand { ReservationId = id, RequestedByUserId = CurrentUserId }, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id}/no-show")]
    public async Task<IActionResult> NoShow(int id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new MarkReservationNoShowCommand { ReservationId = id, RequestedByUserId = CurrentUserId }, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id}/check-in")]
    public async Task<IActionResult> CheckIn(int id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new CheckInReservationCommand { ReservationId = id, RequestedByUserId = CurrentUserId }, cancellationToken);
        return NoContent();
    }

    [HttpPost("checkin-by-code")]
    public async Task<IActionResult> CheckInByCode(CheckInReservationByCodeCommand command, CancellationToken cancellationToken)
    {
        command.RequestedByUserId = CurrentUserId;
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }
}
