using System.IdentityModel.Tokens.Jwt;
using GymAppApi.Application.Features.PersonalLogs.Commands.CreatePersonalLog;
using GymAppApi.Application.Features.PersonalLogs.Commands.DeletePersonalLog;
using GymAppApi.Application.Features.PersonalLogs.Commands.UpdatePersonalLog;
using GymAppApi.Application.Features.PersonalLogs.Queries.GetPersonalLogs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymAppApi.WebApi.Controllers;

// Kişisel takip - her kullanıcı sadece kendi kayıtlarına erişir (rol/tenant yok).
[ApiController]
[Route("api/personal-logs")]
[Authorize]
public class PersonalLogsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PersonalLogsController(IMediator mediator) => _mediator = mediator;

    private int CurrentUserId => int.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    [HttpGet]
    public async Task<IActionResult> GetMine([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetPersonalLogsQuery { From = from, To = to, UserId = CurrentUserId }, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(CreatePersonalLogCommand command, CancellationToken cancellationToken)
    {
        command.UserId = CurrentUserId;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, UpdatePersonalLogCommand command, CancellationToken cancellationToken)
    {
        command.Id = id;
        command.UserId = CurrentUserId;
        return Ok(await _mediator.Send(command, cancellationToken));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeletePersonalLogCommand { Id = id, UserId = CurrentUserId }, cancellationToken);
        return NoContent();
    }
}
