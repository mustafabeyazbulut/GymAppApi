using System.IdentityModel.Tokens.Jwt;
using GymAppApi.Application.Features.Media.Queries.GetMediaFile;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymAppApi.WebApi.Controllers;

// Dosyalar doğrudan static file serving DEĞİL, bu kimlik doğrulamalı action
// üzerinden akıtılır - bkz. docs/superpowers/specs/2026-09-20-content-library-design.md.
[ApiController]
[Route("api/media")]
[Authorize]
public class MediaController : ControllerBase
{
    private readonly IMediator _mediator;

    public MediaController(IMediator mediator) => _mediator = mediator;

    private int CurrentUserId => int.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetMediaFileQuery { MediaFileId = id, RequestedByUserId = CurrentUserId }, cancellationToken);
        return File(result.Content, result.ContentType);
    }
}
