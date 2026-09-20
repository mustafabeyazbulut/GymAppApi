using System.IdentityModel.Tokens.Jwt;
using GymAppApi.Application.Features.DoorAccess.Commands.CreateDoor;
using GymAppApi.Application.Features.DoorAccess.Commands.CreateZone;
using GymAppApi.Application.Features.DoorAccess.Commands.CreateZoneAccessRule;
using GymAppApi.Application.Features.DoorAccess.Commands.DeleteDoor;
using GymAppApi.Application.Features.DoorAccess.Commands.DeleteZone;
using GymAppApi.Application.Features.DoorAccess.Commands.DeleteZoneAccessRule;
using GymAppApi.Application.Features.DoorAccess.Queries.GetDoors;
using GymAppApi.Application.Features.DoorAccess.Queries.GetZoneAccessRules;
using GymAppApi.Application.Features.DoorAccess.Queries.GetZones;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymAppApi.WebApi.Controllers;

// Kapı Erişim Sistemi'nin yazılım iskeleti - tümü StaffManagement
// (GymAdmin kendi firması, BranchManager kendi şubesi). Gerçek donanım
// olmadan bir AccessLog oluşturan hiçbir endpoint YOK (bkz.
// docs/superpowers/specs/2026-09-20-door-access-skeleton-design.md).
[ApiController]
[Route("api/zones")]
[Authorize(Policy = "StaffManagement")]
public class ZonesController : ControllerBase
{
    private readonly IMediator _mediator;

    public ZonesController(IMediator mediator) => _mediator = mediator;

    private int CurrentUserId => int.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    [HttpPost]
    public async Task<IActionResult> CreateZone(CreateZoneCommand command, CancellationToken cancellationToken)
    {
        command.RequestedByUserId = CurrentUserId;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpGet]
    public async Task<IActionResult> GetZones([FromQuery] int branchId, CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetZonesQuery { BranchId = branchId, RequestedByUserId = CurrentUserId }, cancellationToken));

    [HttpDelete("{zoneId}")]
    public async Task<IActionResult> DeleteZone(int zoneId, CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteZoneCommand { ZoneId = zoneId, RequestedByUserId = CurrentUserId }, cancellationToken);
        return NoContent();
    }

    [HttpPost("{zoneId}/doors")]
    public async Task<IActionResult> CreateDoor(int zoneId, CreateDoorCommand command, CancellationToken cancellationToken)
    {
        command.ZoneId = zoneId;
        command.RequestedByUserId = CurrentUserId;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpGet("{zoneId}/doors")]
    public async Task<IActionResult> GetDoors(int zoneId, CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetDoorsQuery { ZoneId = zoneId, RequestedByUserId = CurrentUserId }, cancellationToken));

    [HttpDelete("doors/{doorId}")]
    public async Task<IActionResult> DeleteDoor(int doorId, CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteDoorCommand { DoorId = doorId, RequestedByUserId = CurrentUserId }, cancellationToken);
        return NoContent();
    }

    [HttpPost("{zoneId}/access-rules")]
    public async Task<IActionResult> CreateZoneAccessRule(int zoneId, CreateZoneAccessRuleCommand command, CancellationToken cancellationToken)
    {
        command.ZoneId = zoneId;
        command.RequestedByUserId = CurrentUserId;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpGet("{zoneId}/access-rules")]
    public async Task<IActionResult> GetZoneAccessRules(int zoneId, CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetZoneAccessRulesQuery { ZoneId = zoneId, RequestedByUserId = CurrentUserId }, cancellationToken));

    [HttpDelete("access-rules/{ruleId}")]
    public async Task<IActionResult> DeleteZoneAccessRule(int ruleId, CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteZoneAccessRuleCommand { ZoneAccessRuleId = ruleId, RequestedByUserId = CurrentUserId }, cancellationToken);
        return NoContent();
    }
}
