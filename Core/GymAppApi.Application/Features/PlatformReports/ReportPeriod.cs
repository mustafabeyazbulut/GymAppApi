using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Time;

namespace GymAppApi.Application.Features.PlatformReports;

// Rapor dönemi: bugün dahil son N gün, Türkiye yerel günüyle. Sadece
// 7/30/90/365 desteklenir; diğer değerler 400 InvalidReportPeriod.
public sealed record ReportPeriod(DateOnly FromDate, DateOnly ToDate, DateTime FromUtc, DateTime NowUtc)
{
    public static readonly int[] SupportedDays = { 7, 30, 90, 365 };

    public static ReportPeriod LastDays(int days, DateTime nowUtc)
    {
        if (!SupportedDays.Contains(days))
        {
            throw new BadRequestException("InvalidReportPeriod", string.Join(", ", SupportedDays));
        }

        var today = TurkeyCalendar.LocalDate(nowUtc);
        var fromDate = today.AddDays(-(days - 1));
        // Yerel gün başı (00:00 TR) UTC'ye çevrilir.
        var fromUtc = DateTime.SpecifyKind(fromDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc)
            .AddHours(-TurkeyCalendar.UtcOffsetHours);
        return new ReportPeriod(fromDate, today, fromUtc, nowUtc);
    }
}
