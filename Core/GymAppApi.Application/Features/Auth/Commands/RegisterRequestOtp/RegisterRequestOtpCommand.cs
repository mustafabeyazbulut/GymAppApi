using MediatR;

namespace GymAppApi.Application.Features.Auth.Commands.RegisterRequestOtp;

public class RegisterRequestOtpCommand : IRequest
{
    public string Phone { get; set; } = null!;
    public string? Email { get; set; }
}
