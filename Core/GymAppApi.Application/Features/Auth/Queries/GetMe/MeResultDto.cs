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
    public IReadOnlyList<MePackageAssignmentDto> PackageAssignments { get; set; } = new List<MePackageAssignmentDto>();
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

public class MePackageAssignmentDto
{
    // The mobile client needs the assignment's own id (not just the
    // Package's) to call /api/package-assignments/{id}/... (payments,
    // reservations, check-ins) for the right row when a Member holds more
    // than one PackageAssignment.
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string? CompanyName { get; set; }
    public int? BranchId { get; set; }
    public int PackageId { get; set; }
    public string? PackageName { get; set; }
    public decimal Price { get; set; }
    public string Status { get; set; } = null!;
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int? SessionCount { get; set; }
    public int? RemainingSessions { get; set; }
}
