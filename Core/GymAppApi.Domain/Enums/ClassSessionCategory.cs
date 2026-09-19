namespace GymAppApi.Domain.Enums;

// PT (özel antrenör) dersleri hariç - o zaten mevcut Reservation/CheckIn
// modelinde kalıyor (bkz. docs/superpowers/specs/2026-09-20-group-class-scheduling-design.md).
public enum ClassSessionCategory
{
    GroupClass,
    MartialArts,
}
