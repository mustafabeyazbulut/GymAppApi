using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Features.PlatformReports;

namespace GymAppApi.UnitTests.Features.PlatformReports;

public class ReportPeriodTests
{
    [Theory]
    [InlineData(7)]
    [InlineData(30)]
    [InlineData(90)]
    [InlineData(365)]
    public void SupportedDays_CoverTodayAndThePreviousDays_InTurkeyLocalTime(int days)
    {
        // 2026-09-26 22:30 UTC = 2026-09-27 01:30 TR - yerel "bugün" 27'si.
        var nowUtc = new DateTime(2026, 9, 26, 22, 30, 0, DateTimeKind.Utc);

        var period = ReportPeriod.LastDays(days, nowUtc);

        Assert.Equal(new DateOnly(2026, 9, 27), period.ToDate);
        Assert.Equal(days - 1, period.ToDate.DayNumber - period.FromDate.DayNumber);
        // Dönem başı yerel gece yarısı = önceki gün 21:00 UTC.
        Assert.Equal(new DateTime(period.FromDate.Year, period.FromDate.Month, period.FromDate.Day, 0, 0, 0, DateTimeKind.Utc).AddHours(-3), period.FromUtc);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(366)]
    public void UnsupportedDays_AreABadRequest(int days)
    {
        var ex = Assert.Throws<BadRequestException>(() => ReportPeriod.LastDays(days, DateTime.UtcNow));
        Assert.Equal("InvalidReportPeriod", ex.Code);
    }
}
