using System.IdentityModel.Tokens.Jwt;
using GymAppApi.Application.Features.Services.Commands.CreateService;
using GymAppApi.Application.Features.Services.Commands.SetServiceActive;
using GymAppApi.Application.Features.Services.Commands.UpdateService;
using GymAppApi.Application.Features.Services.Queries.GetBranchServices;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymAppApi.WebApi.Controllers;

// Şube hizmetleri (senaryo §5.3). Yazma: GymAdmin firma genelinde,
// BranchManager kendi şubesinde (ayrım handler'da, aktif bağlama göre).
[ApiController]
[Route("api")]
[Authorize]
public class ServicesController : ControllerBase
{
    private readonly IMediator _mediator;

    public ServicesController(IMediator mediator) => _mediator = mediator;

    private int CurrentUserId => int.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    [HttpGet("branches/{branchId}/services")]
    public async Task<IActionResult> GetForBranch(int branchId, [FromQuery] bool includeInactive, CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetBranchServicesQuery
        {
            BranchId = branchId,
            IncludeInactive = includeInactive,
            RequestedByUserId = CurrentUserId,
        }, cancellationToken));

    [Authorize(Policy = "StaffManagement")]
    [HttpPost("branches/{branchId}/services")]
    public async Task<IActionResult> Create(int branchId, CreateServiceCommand command, CancellationToken cancellationToken)
    {
        command.BranchId = branchId;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [Authorize(Policy = "StaffManagement")]
    [HttpPatch("services/{id}")]
    public async Task<IActionResult> Update(int id, UpdateServiceCommand command, CancellationToken cancellationToken)
    {
        command.ServiceId = id;
        return Ok(await _mediator.Send(command, cancellationToken));
    }

    [Authorize(Policy = "StaffManagement")]
    [HttpPatch("services/{id}/active")]
    public async Task<IActionResult> SetActive(int id, SetServiceActiveCommand command, CancellationToken cancellationToken)
    {
        command.ServiceId = id;
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }
}
