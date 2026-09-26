using System.IdentityModel.Tokens.Jwt;
using GymAppApi.Application.Features.Invitations.Commands.AcceptInvitation;
using GymAppApi.Application.Features.Invitations.Commands.RejectInvitation;
using GymAppApi.Application.Features.Invitations.Queries.GetMyInvitations;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymAppApi.WebApi.Controllers;

// "Davetlerim": kullanıcının kendi bekleyen personel/paket davetleri ve
// SMS kodu olmadan uygulama içi kabul/red. SMS kodlu /confirm uçları
// (assignments/confirm, package-assignments/confirm) aynen duruyor.
[ApiController]
[Route("api/invitations")]
[Authorize]
public class InvitationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public InvitationsController(IMediator mediator) => _mediator = mediator;

    private int CurrentUserId => int.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    [HttpGet("mine")]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetMyInvitationsQuery { UserId = CurrentUserId }, cancellationToken));

    [HttpPost("{type}/{id:int}/accept")]
    public async Task<IActionResult> Accept(string type, int id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new AcceptInvitationCommand { Type = type, InvitationId = id, UserId = CurrentUserId }, cancellationToken);
        return NoContent();
    }

    [HttpPost("{type}/{id:int}/reject")]
    public async Task<IActionResult> Reject(string type, int id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new RejectInvitationCommand { Type = type, InvitationId = id, UserId = CurrentUserId }, cancellationToken);
        return NoContent();
    }
}
