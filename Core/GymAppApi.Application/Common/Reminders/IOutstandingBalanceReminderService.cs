namespace GymAppApi.Application.Common.Reminders;

public interface IOutstandingBalanceReminderService
{
    // Kalan bakiyesi (Package.Price - toplam ödeme) sıfırdan büyük ve son
    // hatırlatmanın üzerinden ReminderCooldownDays gün geçmiş (ya da hiç
    // gönderilmemiş) her üyelik atamasına bir ödeme hatırlatması yollar.
    // Döndürdüğü değer, gönderilen hatırlatma sayısıdır (loglama/test için).
    Task<int> SendDueRemindersAsync(CancellationToken cancellationToken = default);
}
