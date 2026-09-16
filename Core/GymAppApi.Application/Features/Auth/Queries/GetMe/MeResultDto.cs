namespace GymAppApi.Application.Features.Auth.Queries.GetMe;

public class MeResultDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = null!;
    public string Phone { get; set; } = null!;
    public string? Email { get; set; }
    public string PreferredLanguage { get; set; } = null!;
    public bool IsAccountFrozen { get; set; }
    public IReadOnlyList<MeAssignmentDto> Assignments { get; set; } = new List<MeAssignmentDto>();
}

public class MeAssignmentDto
{
    // Nullable: a SuperAdmin assignment is platform-wide (Assignment.CompanyId
    // is null by design for that role) — coercing null to 0 would make a
    // SuperAdmin's own profile indistinguishable from a data-integrity bug.
    public int? CompanyId { get; set; }
    public string? CompanyName { get; set; }
    public int? BranchId { get; set; } // no BranchName — mobile only needs to know a branch id exists, not its label, for the "do I have an active membership" check this DTO exists for
    public string Role { get; set; } = null!;
}
