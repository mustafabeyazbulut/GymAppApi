using System.IdentityModel.Tokens.Jwt;
using GymAppApi.Application.Features.DeviceTokens.Commands.RegisterDeviceToken;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymAppApi.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DeviceTokensController : ControllerBase
{
    private readonly IMediator _mediator;

    public DeviceTokensController(IMediator mediator) => _mediator = mediator;

    private int CurrentUserId => int.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    [HttpPost]
    public async Task<IActionResult> Register(RegisterDeviceTokenCommand command, CancellationToken cancellationToken)
    {
        command.UserId = CurrentUserId;
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }
}
