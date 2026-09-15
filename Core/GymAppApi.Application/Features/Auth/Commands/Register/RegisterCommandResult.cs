namespace GymAppApi.Application.Features.Auth.Commands.Register;

public class RegisterCommandResult
{
    public string AccessToken { get; set; } = null!;
    public DateTime ExpiresAtUtc { get; set; }
    public string RefreshToken { get; set; } = null!;
}
