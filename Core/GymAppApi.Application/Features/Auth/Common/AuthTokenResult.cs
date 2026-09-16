namespace GymAppApi.Application.Features.Auth.Common;

// Shared response shape for every auth flow that ends in a fresh token pair:
// login, refresh, and (from Task 5 onward) register/complete. Extracted from
// the old Register-only RegisterCommandResult, which LoginCommandHandler and
// RefreshCommandHandler were already reusing before this rename.
public class AuthTokenResult
{
    public string AccessToken { get; set; } = null!;
    public DateTime ExpiresAtUtc { get; set; }
    public string RefreshToken { get; set; } = null!;
}
