using GymAppApi.Domain.Common;
using GymAppApi.Domain.Enums;

namespace GymAppApi.Domain.Entities;

// Kişisel takip: kullanıcının kendi tuttuğu antrenman/ölçüm kaydı. Hiçbir
// firmaya/şubeye bağlı değil (tenant'sız) - GymAppApiDbContext.
// IntentionallyUnscopedEntityTypes'ta listelenir; erişim her zaman
// UserId == çağıran kuralıyla handler'larda kısıtlanır.
public class PersonalLog : EntityBase
{
    public int UserId { get; set; }
    public User? User { get; set; }

    public DateOnly Date { get; set; }
    public PersonalLogKind Kind { get; set; }

    // Workout alanları.
    public string? Title { get; set; }
    public int? DurationMinutes { get; set; }

    public string? Notes { get; set; }

    // Measurement alanları.
    public decimal? WeightKg { get; set; }
    public decimal? BodyFatPercent { get; set; }
    public decimal? WaistCm { get; set; }
}
