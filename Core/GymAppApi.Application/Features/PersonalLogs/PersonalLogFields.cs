using System.Globalization;
using FluentValidation;
using GymAppApi.Application.Common.Localization;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;

namespace GymAppApi.Application.Features.PersonalLogs;

// Create ve Update komutlarının ortak gövdesi.
public abstract class PersonalLogFields
{
    public DateOnly Date { get; set; }
    public PersonalLogKind Kind { get; set; }
    public string? Title { get; set; }
    public int? DurationMinutes { get; set; }
    public string? Notes { get; set; }
    public decimal? WeightKg { get; set; }
    public decimal? BodyFatPercent { get; set; }
    public decimal? WaistCm { get; set; }

    // Dünyada şu an yaşanan en ileri tarih (UTC+14).
    public static DateOnly LatestLocalToday() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(14));

    // Sadece türe ait alanlar saklanır: Workout'ta ölçüm alanları, Measurement'ta
    // başlık/süre yok sayılır (null yazılır) - kayıt türüyle çelişen veri tutulmaz.
    public void ApplyTo(PersonalLog log)
    {
        var isWorkout = Kind == PersonalLogKind.Workout;
        log.Date = Date;
        log.Kind = Kind;
        log.Title = isWorkout ? Title?.Trim() : null;
        log.DurationMinutes = isWorkout ? DurationMinutes : null;
        log.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();
        log.WeightKg = isWorkout ? null : WeightKg;
        log.BodyFatPercent = isWorkout ? null : BodyFatPercent;
        log.WaistCm = isWorkout ? null : WaistCm;
    }
}

public abstract class PersonalLogFieldsValidator<T> : AbstractValidator<T> where T : PersonalLogFields
{
    protected PersonalLogFieldsValidator()
    {
        RuleFor(x => x.Kind).IsInEnum().WithMessage(_ => Localized("PersonalLogInvalidKind"));

        // "Gelecek" en ileri saat dilimine (UTC+14) göre: kullanıcının yerel
        // "bugün"ü UTC'de henüz yarın olabilir ve reddedilmemeli.
        RuleFor(x => x.Date)
            .Must(date => date <= PersonalLogFields.LatestLocalToday())
            .WithMessage(_ => Localized("PersonalLogDateInFuture"));

        RuleFor(x => x.Notes).MaximumLength(1000).WithMessage(_ => Localized("PersonalLogNotesTooLong"));

        When(x => x.Kind == PersonalLogKind.Workout, () =>
        {
            RuleFor(x => x.Title).Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage(_ => Localized("PersonalLogTitleRequired"));
            RuleFor(x => x.Title).MaximumLength(100).WithMessage(_ => Localized("PersonalLogTitleTooLong"));
            RuleFor(x => x.DurationMinutes).InclusiveBetween(1, 600)
                .When(x => x.DurationMinutes.HasValue)
                .WithMessage(_ => Localized("PersonalLogDurationOutOfRange"));
        });

        When(x => x.Kind == PersonalLogKind.Measurement, () =>
        {
            RuleFor(x => x)
                .Must(x => x.WeightKg.HasValue || x.BodyFatPercent.HasValue || x.WaistCm.HasValue)
                .WithName("Measurement")
                .WithMessage(_ => Localized("PersonalLogMeasurementRequired"));
            RuleFor(x => x.WeightKg).InclusiveBetween(20m, 400m)
                .When(x => x.WeightKg.HasValue)
                .WithMessage(_ => Localized("PersonalLogWeightOutOfRange"));
            RuleFor(x => x.BodyFatPercent).InclusiveBetween(1m, 75m)
                .When(x => x.BodyFatPercent.HasValue)
                .WithMessage(_ => Localized("PersonalLogBodyFatOutOfRange"));
            RuleFor(x => x.WaistCm).InclusiveBetween(30m, 250m)
                .When(x => x.WaistCm.HasValue)
                .WithMessage(_ => Localized("PersonalLogWaistOutOfRange"));
        });
    }

    protected static string Localized(string code) =>
        AppMessages.Resolve(code, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
}
