using GymAppApi.Domain.Common;
using GymAppApi.Domain.Enums;

namespace GymAppApi.Domain.Entities;

// Deliberately NOT built on ASP.NET Core Identity: Identity's role model is
// one-set-of-roles-per-user, but a User here can hold a different Role per
// Company/Branch via Assignment. Password hashing still reuses
// Microsoft.AspNetCore.Identity.PasswordHasher<T> (Infrastructure layer) —
// only the Identity *data model* is skipped, not its hashing algorithm.
public class User : EntityBase
{
    public string Phone { get; set; } = null!;
    public string? Email { get; set; }
    public string PasswordHash { get; set; } = null!;
    public string FullName { get; set; } = null!;
    public Gender Gender { get; set; } = Gender.Unspecified;
    public string PreferredLanguage { get; set; } = "tr";
    public bool PhoneVerified { get; set; }

    public ICollection<Assignment> Assignments { get; set; } = new List<Assignment>();
}
