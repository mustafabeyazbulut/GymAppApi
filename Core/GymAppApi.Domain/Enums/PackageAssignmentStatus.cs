namespace GymAppApi.Domain.Enums;

// Expired is deliberately NOT a stored status - a PackageAssignment whose
// EndDate has passed is still "Active" in the database; callers compute
// "is this currently usable" as Status == Active && (EndDate == null || EndDate > now).
public enum PackageAssignmentStatus
{
    Active,
    Frozen,
    Cancelled,
}
