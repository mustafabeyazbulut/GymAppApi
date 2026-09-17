using System.IdentityModel.Tokens.Jwt;
using GymAppApi.Application.Features.Branches.Commands.CreateBranch;
using GymAppApi.Application.Features.Branches.Commands.SetBranchActive;
using GymAppApi.Application.Features.Branches.Queries.GetBranches;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymAppApi.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BranchesController : ControllerBase
{
    private readonly IMediator _mediator;

    public BranchesController(IMediator mediator) => _mediator = mediator;

    private int CurrentUserId => int.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetBranchesQuery(), cancellationToken));

    [Authorize(Policy = "GymAdminOrSuperAdmin")]
    [HttpPost]
    public async Task<IActionResult> Create(CreateBranchCommand command, CancellationToken cancellationToken)
    {
        command.RequestedByUserId = CurrentUserId;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [Authorize(Policy = "GymAdminOrSuperAdmin")]
    [HttpPatch("{id}/active")]
    public async Task<IActionResult> SetActive(int id, SetBranchActiveCommand command, CancellationToken cancellationToken)
    {
        command.BranchId = id;
        command.RequestedByUserId = CurrentUserId;
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }
}
