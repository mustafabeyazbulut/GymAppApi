using System.IdentityModel.Tokens.Jwt;
using GymAppApi.Application.Features.Assignments.Commands.AddStaffMember;
using GymAppApi.Application.Features.Assignments.Commands.CreateAssignment;
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

    [Authorize(Policy = "StaffManagement")]
    [HttpPost("staff")]
    public async Task<IActionResult> AddStaffMember(AddStaffMemberCommand command, CancellationToken cancellationToken)
    {
        command.RequestedByUserId = CurrentUserId;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }
}
