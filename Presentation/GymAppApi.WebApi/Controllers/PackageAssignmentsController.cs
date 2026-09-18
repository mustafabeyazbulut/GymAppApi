using System.IdentityModel.Tokens.Jwt;
using GymAppApi.Application.Features.Packages.Commands.CancelPackageAssignment;
using GymAppApi.Application.Features.Packages.Commands.ConfirmPackageAssignment;
using GymAppApi.Application.Features.Packages.Commands.CreatePackageAssignment;
using GymAppApi.Application.Features.Packages.Commands.FreezePackageAssignment;
using GymAppApi.Application.Features.Packages.Commands.RecordGeneralCheckIn;
using GymAppApi.Application.Features.Packages.Commands.RecordPackageAssignmentPayment;
using GymAppApi.Application.Features.Packages.Commands.RecordProgressNote;
using GymAppApi.Application.Features.Packages.Commands.UnfreezePackageAssignment;
using GymAppApi.Application.Features.Packages.Queries.GetPackageAssignmentCheckIns;
using GymAppApi.Application.Features.Packages.Queries.GetPackageAssignmentPayments;
using GymAppApi.Application.Features.Packages.Queries.GetPackageAssignmentProgressNotes;
using GymAppApi.Application.Features.Reservations.Queries.GetPackageAssignmentReservations;
using GymAppApi.Application.Features.Reservations.Queries.GetPackageAssignmentTrainers;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymAppApi.WebApi.Controllers;

// Explicit route, not "api/[controller]" - this codebase has no kebab-case
// route token transformer configured (see Program.cs), so the [controller]
// token would resolve to the literal class name "PackageAssignments"
// (/api/packageassignments, no hyphen) instead of the hyphenated path the
// design spec calls for.
[ApiController]
[Route("api/package-assignments")]
public class PackageAssignmentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PackageAssignmentsController(IMediator mediator) => _mediator = mediator;

    private int CurrentUserId => int.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    [Authorize(Policy = "StaffManagement")]
    [HttpPost]
    public async Task<IActionResult> Create(CreatePackageAssignmentCommand command, CancellationToken cancellationToken)
    {
        command.RequestedByUserId = CurrentUserId;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    // Any authenticated user can call this - it's how the MEMBER (not the
    // inviter) confirms a pending package invitation sent to their own phone.
    [Authorize]
    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm(ConfirmPackageAssignmentCommand command, CancellationToken cancellationToken)
    {
        command.UserId = CurrentUserId;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [Authorize(Policy = "StaffManagement")]
    [HttpPost("{id}/freeze")]
    public async Task<IActionResult> Freeze(int id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new FreezePackageAssignmentCommand { PackageAssignmentId = id, RequestedByUserId = CurrentUserId }, cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = "StaffManagement")]
    [HttpPost("{id}/unfreeze")]
    public async Task<IActionResult> Unfreeze(int id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new UnfreezePackageAssignmentCommand { PackageAssignmentId = id, RequestedByUserId = CurrentUserId }, cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = "StaffManagement")]
    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> Cancel(int id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new CancelPackageAssignmentCommand { PackageAssignmentId = id, RequestedByUserId = CurrentUserId }, cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = "StaffManagement")]
    [HttpPost("{id}/payments")]
    public async Task<IActionResult> RecordPayment(int id, RecordPackageAssignmentPaymentCommand command, CancellationToken cancellationToken)
    {
        command.PackageAssignmentId = id;
        command.RequestedByUserId = CurrentUserId;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    // [Authorize] not StaffManagement-only - the handler itself allows the
    // assignment's own Member to see their own payment history, in addition
    // to staff.
    [Authorize]
    [HttpGet("{id}/payments")]
    public async Task<IActionResult> GetPayments(int id, CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetPackageAssignmentPaymentsQuery(id, CurrentUserId), cancellationToken));

    // [Authorize] not StaffManagement-only - the handler allows the
    // assignment's own Member to see their own reservations too.
    [Authorize]
    [HttpGet("{id}/reservations")]
    public async Task<IActionResult> GetReservations(int id, CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetPackageAssignmentReservationsQuery(id, CurrentUserId), cancellationToken));

    // Which Trainer ids a caller may pass into POST /api/reservations for
    // this assignment - see GetPackageAssignmentTrainersQueryHandler's own
    // comment for why this is scoped per-assignment rather than a general
    // trainer directory.
    [Authorize]
    [HttpGet("{id}/trainers")]
    public async Task<IActionResult> GetTrainers(int id, CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetPackageAssignmentTrainersQuery(id, CurrentUserId), cancellationToken));

    // Walk-in/no-reservation check-in - front desk only, unlike the
    // reservation-based check-in endpoints on ReservationsController which a
    // Trainer may also call.
    [Authorize(Policy = "StaffManagement")]
    [HttpPost("{id}/check-in")]
    public async Task<IActionResult> CheckIn(int id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new RecordGeneralCheckInCommand { PackageAssignmentId = id, RequestedByUserId = CurrentUserId }, cancellationToken);
        return NoContent();
    }

    [Authorize]
    [HttpGet("{id}/check-ins")]
    public async Task<IActionResult> GetCheckIns(int id, CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetPackageAssignmentCheckInsQuery(id, CurrentUserId), cancellationToken));

    // [Authorize(Policy = "StaffManagement")] DEĞİL - handler'ın kendisi bir
    // Trainer'ın da (bu şubede çalışıyorsa) not bırakabilmesine izin veriyor,
    // StaffManagement policy'si Trainer rolünü hiç kapsamıyor.
    [Authorize]
    [HttpPost("{id}/progress-notes")]
    public async Task<IActionResult> RecordProgressNote(int id, RecordProgressNoteCommand command, CancellationToken cancellationToken)
    {
        command.PackageAssignmentId = id;
        command.RequestedByUserId = CurrentUserId;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    // [Authorize] - handler'ın kendisi hem atamanın sahibi Member'a hem de
    // ilgili şubedeki staff/Trainer'a izin veriyor.
    [Authorize]
    [HttpGet("{id}/progress-notes")]
    public async Task<IActionResult> GetProgressNotes(int id, CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetPackageAssignmentProgressNotesQuery(id, CurrentUserId), cancellationToken));
}
