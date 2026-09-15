using System.IdentityModel.Tokens.Jwt;
using GymAppApi.Application.Features.Auth.Commands.DeleteMe;
using GymAppApi.Application.Features.Auth.Commands.ForgotPassword;
using GymAppApi.Application.Features.Auth.Commands.Login;
using GymAppApi.Application.Features.Auth.Commands.Refresh;
using GymAppApi.Application.Features.Auth.Commands.Register;
using GymAppApi.Application.Features.Auth.Commands.ResetPassword;
using GymAppApi.Application.Features.Auth.Queries.GetMe;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GymAppApi.WebApi.Controllers;

// [EnableRateLimiting("auth")] was added to this controller by Task 7's own
// code-quality review (a global 10-req/min policy registered in Program.cs)
// AFTER this task's code was originally drafted — preserve it across this
// full-file replacement, do not drop it.
[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;

    public AuthController(IMediator mediator) => _mediator = mediator;

    private int CurrentUserId => int.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginCommand command, CancellationToken cancellationToken)
        => Ok(await _mediator.Send(command, cancellationToken));

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshCommand command, CancellationToken cancellationToken)
        => Ok(await _mediator.Send(command, cancellationToken));

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordCommand command, CancellationToken cancellationToken)
        => Ok(await _mediator.Send(command, cancellationToken));

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetMe(CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetMeQuery { UserId = CurrentUserId }, cancellationToken));

    [Authorize]
    [HttpDelete("me")]
    public async Task<IActionResult> DeleteMe(CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteMeCommand { UserId = CurrentUserId }, cancellationToken);
        return NoContent();
    }
}
