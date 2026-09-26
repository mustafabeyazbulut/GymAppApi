using System.IdentityModel.Tokens.Jwt;
using GymAppApi.Application.Features.ClassScheduling.Commands.CreateClassSession;
using GymAppApi.Application.Features.ClassScheduling.Commands.EnrollInClassSession;
using GymAppApi.Application.Features.ClassScheduling.Queries.GetClassSessions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymAppApi.WebApi.Controllers;

[ApiController]
[Route("api/class-sessions")]
[Authorize]
public class ClassSessionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ClassSessionsController(IMediator mediator) => _mediator = mediator;

    private int CurrentUserId => int.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    [Authorize(Policy = "StaffManagement")]
    [HttpPost]
    public async Task<IActionResult> Create(CreateClassSessionCommand command, CancellationToken cancellationToken)
    {
        command.RequestedByUserId = CurrentUserId;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    // Herkes çağırabilir - görünürlük kapsamı (personelin firması/şubesi +
    // üyenin geçerli paketleri) handler'da uygulanıyor.
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? branchId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetClassSessionsQuery { BranchId = branchId, From = from, To = to, RequestedByUserId = CurrentUserId }, cancellationToken));

    [HttpPost("{id}/enroll")]
    public async Task<IActionResult> Enroll(int id, EnrollInClassSessionCommand command, CancellationToken cancellationToken)
    {
        command.ClassSessionId = id;
        command.RequestedByUserId = CurrentUserId;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }
}
