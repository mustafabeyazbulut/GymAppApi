using System.IdentityModel.Tokens.Jwt;
using GymAppApi.Application.Features.Assignments.Commands.AddStaffMember;
using GymAppApi.Application.Features.Assignments.Commands.ConfirmAssignmentInvitation;
using GymAppApi.Application.Features.Assignments.Commands.CreateAssignment;
using GymAppApi.Application.Features.Assignments.Commands.InviteGymAdmin;
using GymAppApi.Application.Features.Assignments.Commands.RemoveAssignment;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymAppApi.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AssignmentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AssignmentsController(IMediator mediator) => _mediator = mediator;

    private int CurrentUserId => int.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    [Authorize(Policy = "GymAdminOrSuperAdmin")]
    [HttpPost]
    public async Task<IActionResult> Create(CreateAssignmentCommand command, CancellationToken cancellationToken)
    {
        command.RequestedByUserId = CurrentUserId;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [Authorize(Policy = "GymAdminOrSuperAdmin")]
    [HttpPost("gym-admin")]
    public async Task<IActionResult> InviteGymAdmin(InviteGymAdminCommand command, CancellationToken cancellationToken)
    {
        command.RequestedByUserId = CurrentUserId;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [Authorize(Policy = "StaffManagement")]
    [HttpPost("staff")]
    public async Task<IActionResult> AddStaffMember(AddStaffMemberCommand command, CancellationToken cancellationToken)
    {
        command.RequestedByUserId = CurrentUserId;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    // Any authenticated user can call this - it's how the INVITEE (not the
    // inviter) confirms a pending GymAdmin/Member/Trainer invitation sent to
    // their own phone. See ConfirmAssignmentInvitationCommand.
    [Authorize]
    [HttpPost("confirm")]
    public async Task<IActionResult> ConfirmInvitation(ConfirmAssignmentInvitationCommand command, CancellationToken cancellationToken)
    {
        command.UserId = CurrentUserId;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    // Any authenticated user can call this - authorization is fully
    // role-dependent (see RemoveAssignmentCommandHandler) and can't be
    // expressed as one static policy the way Create/AddStaffMember can.
    [Authorize]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Remove(int id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new RemoveAssignmentCommand { AssignmentId = id, RequestedByUserId = CurrentUserId }, cancellationToken);
        return NoContent();
    }
}
