using GymAppApi.Domain.Entities;

namespace GymAppApi.Application.Features.PersonalLogs;

public class PersonalLogDto
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public string Kind { get; set; } = null!;
    public string? Title { get; set; }
    public int? DurationMinutes { get; set; }
    public string? Notes { get; set; }
    public decimal? WeightKg { get; set; }
    public decimal? BodyFatPercent { get; set; }
    public decimal? WaistCm { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public static PersonalLogDto From(PersonalLog log) => new()
    {
        Id = log.Id,
        Date = log.Date,
        Kind = log.Kind.ToString(),
        Title = log.Title,
        DurationMinutes = log.DurationMinutes,
        Notes = log.Notes,
        WeightKg = log.WeightKg,
        BodyFatPercent = log.BodyFatPercent,
        WaistCm = log.WaistCm,
        CreatedAt = log.CreatedAt,
        UpdatedAt = log.UpdatedAt,
    };
}
