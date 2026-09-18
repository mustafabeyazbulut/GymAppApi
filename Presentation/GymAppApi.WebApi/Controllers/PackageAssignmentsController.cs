using System.IdentityModel.Tokens.Jwt;
using GymAppApi.Application.Features.Packages.Commands.CancelPackageAssignment;
using GymAppApi.Application.Features.Packages.Commands.ConfirmPackageAssignment;
using GymAppApi.Application.Features.Packages.Commands.CreatePackageAssignment;
using GymAppApi.Application.Features.Packages.Commands.FreezePackageAssignment;
using GymAppApi.Application.Features.Packages.Commands.UnfreezePackageAssignment;
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
}
