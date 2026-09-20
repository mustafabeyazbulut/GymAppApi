using System.IdentityModel.Tokens.Jwt;
using GymAppApi.Application.Features.ContentLibrary.Commands.CreateContentItem;
using GymAppApi.Application.Features.ContentLibrary.Commands.SetContentItemActive;
using GymAppApi.Application.Features.ContentLibrary.Queries.GetContentItems;
using GymAppApi.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymAppApi.WebApi.Controllers;

[ApiController]
[Route("api/content-items")]
[Authorize]
public class ContentItemsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ContentItemsController(IMediator mediator) => _mediator = mediator;

    private int CurrentUserId => int.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    [Authorize(Policy = "StaffManagement")]
    [HttpPost]
    [RequestSizeLimit(200_000_000)]
    public async Task<IActionResult> Create(
        [FromForm] string title,
        [FromForm] string? description,
        [FromForm] PackageAccessTier requiredAccessTier,
        [FromForm] int? branchId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        var command = new CreateContentItemCommand
        {
            Title = title,
            Description = description,
            RequiredAccessTier = requiredAccessTier,
            BranchId = branchId,
            FileContent = stream,
            FileContentType = file.ContentType,
            RequestedByUserId = CurrentUserId,
        };
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    // Herkes çağırabilir - handler'ın kendisi role göre (staff/Member)
    // görünürlüğü filtreliyor (bkz. GetContentItemsQueryHandler).
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetContentItemsQuery { RequestedByUserId = CurrentUserId }, cancellationToken));

    [Authorize(Policy = "StaffManagement")]
    [HttpPatch("{id}/active")]
    public async Task<IActionResult> SetActive(int id, SetContentItemActiveCommand command, CancellationToken cancellationToken)
    {
        command.ContentItemId = id;
        command.RequestedByUserId = CurrentUserId;
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }
}
