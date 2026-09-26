using GymAppApi.Domain.Enums;

namespace GymAppApi.Application.Common.Interfaces;

public interface ITenantContext
{
    int? CompanyId { get; }
    int? BranchId { get; }
    bool IsSuperAdmin { get; }

    // İsteğin AKTİF ataması (bkz. ITenantResolutionService) - yetki
    // politikaları kullanıcının herhangi bir yerdeki rolüne değil bu role
    // bakar. Varsayılan gövde, sadece ilk üç özelliği bilen eski/test
    // uygulamalarının derlenmeye devam etmesi için.
    int? AssignmentId => null;
    AssignmentRole? Role => null;
}
