namespace GymAppApi.Domain.Enums;

public enum AssignmentRole
{
    SuperAdmin,
    GymAdmin,
    BranchManager,
    Trainer,
    // Member bilerek YOK (senaryo §10.7): gym üyeliği bir atama değil, bir
    // pakettir (PackageAssignment). Enum DB'de string olarak saklandığı için
    // (AssignmentConfiguration) değerin kaldırılması diğer rolleri kaydırmaz;
    // eski "Member" satırları RemoveLegacyMemberAssignments migration'ıyla silindi.
}
