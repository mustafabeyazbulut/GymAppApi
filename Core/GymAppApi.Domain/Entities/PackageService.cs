namespace GymAppApi.Domain.Entities;

// Package <-> Service çoka-çok bağlantısı: paket hangi hizmetlere katılım
// hakkı veriyor. Kendi tenant alanı yok - kapsamı bağlı olduğu paketten
// gelir (GymAppApiDbContext.IntentionallyUnscopedEntityTypes).
public class PackageService
{
    public int PackageId { get; set; }
    public int ServiceId { get; set; }
}
