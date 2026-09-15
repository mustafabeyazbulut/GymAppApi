using GymAppApi.Application.Common.Interfaces;
using Microsoft.AspNetCore.Identity;

namespace GymAppApi.Infrastructure.Security;

// PasswordHasher<TUser>'s "user" parameter is unused by the default
// implementation (PBKDF2 over the raw string) — we pass a throwaway object
// so this can hash any string, not just User passwords (also used for
// RefreshToken.TokenHash).
public class PasswordHasherAdapter : IPasswordHasher
{
    private readonly PasswordHasher<object> _hasher = new();
    private static readonly object DummyUser = new();

    public string Hash(string input) => _hasher.HashPassword(DummyUser, input);

    public bool Verify(string hash, string input)
        => _hasher.VerifyHashedPassword(DummyUser, hash, input) != PasswordVerificationResult.Failed;
}
