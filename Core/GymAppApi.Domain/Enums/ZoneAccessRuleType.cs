namespace GymAppApi.Domain.Enums;

// Hiçbir kod bu kuralları şu an DEĞERLENDİRMİYOR - gerçek donanım seçilip
// bir AccessLog akışı eklenene kadar sadece CRUD ile tanımlanıp saklanırlar
// (bkz. docs/superpowers/specs/2026-09-20-door-access-skeleton-design.md).
public enum ZoneAccessRuleType
{
    AllActiveMembers,
    Gender,
    Role,
    PackageCategory,
}
