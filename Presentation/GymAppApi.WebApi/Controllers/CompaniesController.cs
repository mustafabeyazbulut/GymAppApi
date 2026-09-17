using System.IdentityModel.Tokens.Jwt;
using GymAppApi.Application.Features.Companies.Commands.CreateCompany;
using GymAppApi.Application.Features.Companies.Commands.SetCompanyActive;
using GymAppApi.Application.Features.Companies.Commands.UpdateCompanyName;
using GymAppApi.Application.Features.Companies.Queries.GetCompanies;
using GymAppApi.Application.Features.Companies.Queries.GetCompanyDetail;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymAppApi.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "SuperAdminOnly")]
public class CompaniesController : ControllerBase
{
    private readonly IMediator _mediator;

    public CompaniesController(IMediator mediator) => _mediator = mediator;

    private int CurrentUserId => int.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetCompaniesQuery(), cancellationToken));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetCompanyDetailQuery(id), cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(CreateCompanyCommand command, CancellationToken cancellationToken)
    {
        command.RequestedByUserId = CurrentUserId;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> UpdateName(int id, UpdateCompanyNameCommand command, CancellationToken cancellationToken)
    {
        command.CompanyId = id;
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }

    [HttpPatch("{id}/active")]
    public async Task<IActionResult> SetActive(int id, SetCompanyActiveCommand command, CancellationToken cancellationToken)
    {
        command.CompanyId = id;
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }
}
