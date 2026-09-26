namespace GymAppApi.Application.Common.Time;

// Tasarım dokümanı: tüm gym'ler aynı saat diliminde (Türkiye, UTC+3, DST yok)
// varsayılır. Gün bazlı iş kuralları (dondurma gün sayısı gibi) UTC tarihine
// değil kullanıcının yaşadığı yerel güne göre hesaplanır.
public static class TurkeyCalendar
{
    public const int UtcOffsetHours = 3;

    public static DateOnly LocalDate(DateTime utc) => DateOnly.FromDateTime(utc.AddHours(UtcOffsetHours));
}
