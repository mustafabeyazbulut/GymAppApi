namespace GymAppApi.Application.Common.Interfaces;

// Wraps Microsoft.AspNetCore.Identity.PasswordHasher<T> without depending on
// ASP.NET Core Identity's user-store machinery — reused for both real user
// passwords and RefreshToken raw-value hashing (see User.cs's own comment:
// "only Identity's data model is skipped, not its hashing algorithm").
public interface IPasswordHasher
{
    string Hash(string input);
    bool Verify(string hash, string input);
}
