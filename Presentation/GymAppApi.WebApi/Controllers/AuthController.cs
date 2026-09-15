using GymAppApi.Application.Features.Auth.Commands.Register;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GymAppApi.WebApi.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;

    public AuthController(IMediator mediator) => _mediator = mediator;

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(GymAppApi.Application.Features.Auth.Commands.Login.LoginCommand command, CancellationToken cancellationToken)
        => Ok(await _mediator.Send(command, cancellationToken));

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(GymAppApi.Application.Features.Auth.Commands.Refresh.RefreshCommand command, CancellationToken cancellationToken)
        => Ok(await _mediator.Send(command, cancellationToken));

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(GymAppApi.Application.Features.Auth.Commands.ForgotPassword.ForgotPasswordCommand command, CancellationToken cancellationToken)
        => Ok(await _mediator.Send(command, cancellationToken));
}
