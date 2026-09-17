using System.IdentityModel.Tokens.Jwt;
using GymAppApi.Application.Features.Auth.Commands.DeleteMe;
using GymAppApi.Application.Features.Auth.Commands.DeleteMeRequestOtp;
using GymAppApi.Application.Features.Auth.Commands.ForgotPassword;
using GymAppApi.Application.Features.Auth.Commands.FreezeAccount;
using GymAppApi.Application.Features.Auth.Commands.FreezeAccountRequestOtp;
using GymAppApi.Application.Features.Auth.Commands.Login;
using GymAppApi.Application.Features.Auth.Commands.Refresh;
using GymAppApi.Application.Features.Auth.Commands.RegisterComplete;
using GymAppApi.Application.Features.Auth.Commands.RegisterRequestOtp;
using GymAppApi.Application.Features.Auth.Commands.ResetPassword;
using GymAppApi.Application.Features.Auth.Commands.UnfreezeAccount;
using GymAppApi.Application.Features.Auth.Commands.UnfreezeAccountRequestOtp;
using GymAppApi.Application.Features.Auth.Commands.UpdatePreferredLanguage;
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

    [HttpPost("register/request-otp")]
    public async Task<IActionResult> RequestRegistrationOtp(RegisterRequestOtpCommand command, CancellationToken cancellationToken)
    {
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }

    [HttpPost("register/complete")]
    public async Task<IActionResult> CompleteRegistration(RegisterCompleteCommand command, CancellationToken cancellationToken)
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
    [HttpPost("me/delete/request-otp")]
    public async Task<IActionResult> RequestDeleteMeOtp(CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteMeRequestOtpCommand { UserId = CurrentUserId }, cancellationToken);
        return NoContent();
    }

    [Authorize]
    [HttpDelete("me")]
    public async Task<IActionResult> DeleteMe(DeleteMeCommand command, CancellationToken cancellationToken)
    {
        command.UserId = CurrentUserId;
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }

    [Authorize]
    [HttpPatch("me/language")]
    public async Task<IActionResult> UpdatePreferredLanguage(UpdatePreferredLanguageCommand command, CancellationToken cancellationToken)
    {
        command.UserId = CurrentUserId;
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }

    [Authorize]
    [HttpPost("me/freeze/request-otp")]
    public async Task<IActionResult> RequestFreezeOtp(CancellationToken cancellationToken)
    {
        await _mediator.Send(new FreezeAccountRequestOtpCommand { UserId = CurrentUserId }, cancellationToken);
        return NoContent();
    }

    [Authorize]
    [HttpPost("me/freeze")]
    public async Task<IActionResult> FreezeAccount(FreezeAccountCommand command, CancellationToken cancellationToken)
    {
        command.UserId = CurrentUserId;
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }

    [Authorize]
    [HttpPost("me/unfreeze/request-otp")]
    public async Task<IActionResult> RequestUnfreezeOtp(CancellationToken cancellationToken)
    {
        await _mediator.Send(new UnfreezeAccountRequestOtpCommand { UserId = CurrentUserId }, cancellationToken);
        return NoContent();
    }

    [Authorize]
    [HttpPost("me/unfreeze")]
    public async Task<IActionResult> UnfreezeAccount(UnfreezeAccountCommand command, CancellationToken cancellationToken)
    {
        command.UserId = CurrentUserId;
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }
}
