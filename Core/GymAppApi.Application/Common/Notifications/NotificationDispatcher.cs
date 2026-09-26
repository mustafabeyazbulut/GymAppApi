using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace GymAppApi.Application.Common.Notifications;

// Shared by every flow that notifies a user about something that happened to
// their account (attached to a company/branch, made a Gym Admin, etc.):
// writes an in-app Notification row and pushes to every device the user has
// registered. See the product design doc's "Push Notification Altyapısı".
public static class NotificationDispatcher
{
    public static async Task NotifyUserAsync(
        IUnitOfWork unitOfWork,
        IPushNotificationSender pushNotificationSender,
        int userId,
        string title,
        string body,
        CancellationToken cancellationToken = default)
    {
        await unitOfWork.GetWriteRepository<Notification>().AddAsync(new Notification
        {
            UserId = userId,
            Title = title,
            Body = body,
            IsRead = false,
        }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var deviceTokens = await unitOfWork.GetReadRepository<DeviceToken>()
            .GetAllAsync(d => d.UserId == userId, cancellationToken: cancellationToken);
        foreach (var deviceToken in deviceTokens)
        {
            await pushNotificationSender.SendAsync(deviceToken.Token, title, body, cancellationToken);
        }
    }

    // Toplu gönderimler (hatırlatma job'ları) için: bir alıcının hatası (ör. push
    // sağlayıcısı yanıt vermedi) loglanır ve false döner; tarama diğer
    // alıcılarla devam eder. Uygulama içi bildirim satırı push'tan ÖNCE commit
    // edildiği için hatalı alıcının uygulama içi bildirimi yine de kalır.
    //
    // Hata SaveChanges'ta olduysa (FK/unique ihlali, geçici DB hatası) change
    // tracker'da kalan Added/Modified varlıklar temizlenir - tarama tek bir
    // DbContext ile yürüdüğü için aksi hâlde sonraki TÜM alıcıların
    // SaveChanges'ı aynı bozuk varlıklar yüzünden patlardı. Çağıran, alıcı
    // başına yazacağı varlıkları temizlemeden SONRA yeniden okumalı
    // (izlenmeyen bir varlığa yazmak sessizce kaybolur).
    public static async Task<bool> TryNotifyUserAsync(
        IUnitOfWork unitOfWork,
        IPushNotificationSender pushNotificationSender,
        ILogger logger,
        int userId,
        string title,
        string body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await NotifyUserAsync(unitOfWork, pushNotificationSender, userId, title, body, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Bildirim gönderilemedi (kullanıcı {UserId}); toplu gönderim devam ediyor.", userId);
            unitOfWork.ClearChangeTracker();
            return false;
        }
    }
}
