using System.IdentityModel.Tokens.Jwt;
using GymAppApi.Application.Features.Notifications.Commands.MarkNotificationRead;
using GymAppApi.Application.Features.Notifications.Queries.GetMyNotifications;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymAppApi.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public NotificationsController(IMediator mediator) => _mediator = mediator;

    private int CurrentUserId => int.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    [HttpGet]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetMyNotificationsQuery { UserId = CurrentUserId }, cancellationToken));

    [HttpPatch("{id}/read")]
    public async Task<IActionResult> MarkRead(int id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new MarkNotificationReadCommand { NotificationId = id, UserId = CurrentUserId }, cancellationToken);
        return NoContent();
    }
}
