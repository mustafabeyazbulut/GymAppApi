using System.IdentityModel.Tokens.Jwt;
using GymAppApi.Application.Features.Packages.Commands.CreatePackage;
using GymAppApi.Application.Features.Packages.Commands.SetPackageActive;
using GymAppApi.Application.Features.Packages.Queries.GetPackageDetail;
using GymAppApi.Application.Features.Packages.Queries.GetPackages;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymAppApi.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PackagesController : ControllerBase
{
    private readonly IMediator _mediator;

    public PackagesController(IMediator mediator) => _mediator = mediator;

    private int CurrentUserId => int.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetPackagesQuery(), cancellationToken));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetPackageDetailQuery(id), cancellationToken));

    [Authorize(Policy = "StaffManagement")]
    [HttpPost]
    public async Task<IActionResult> Create(CreatePackageCommand command, CancellationToken cancellationToken)
    {
        command.RequestedByUserId = CurrentUserId;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [Authorize(Policy = "StaffManagement")]
    [HttpPatch("{id}/active")]
    public async Task<IActionResult> SetActive(int id, SetPackageActiveCommand command, CancellationToken cancellationToken)
    {
        command.PackageId = id;
        command.RequestedByUserId = CurrentUserId;
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }
}
