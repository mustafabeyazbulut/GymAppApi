namespace GymAppApi.Application.Common.Reminders;

public interface IMembershipExpiryReminderService
{
    // Süresi ReminderDaysBeforeExpiry gün ya da daha az kalmış ve daha önce
    // hatırlatma gönderilmemiş her aktif üyeliğe bir bildirim yollar.
    // Zamanlamayı çağıran (bkz. MembershipExpiryReminderHostedService) belirler -
    // bu servis sadece "şu an gönderilmesi gereken" kaydı bulup işler.
    // Döndürdüğü değer, gönderilen hatırlatma sayısıdır (loglama/test için).
    Task<int> SendDueRemindersAsync(CancellationToken cancellationToken = default);
}
