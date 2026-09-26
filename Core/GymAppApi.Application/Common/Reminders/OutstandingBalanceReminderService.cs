using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Notifications;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Application.Common.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GymAppApi.Application.Common.Reminders;

public class OutstandingBalanceReminderService : IOutstandingBalanceReminderService
{
    // Borç, süresi doluncaya kadar açık kalabilir - MembershipExpiryReminderService'in
    // "bir kez ve bitti" mantığının aksine burada periyodik bir tekrar
    // (cooldown) mantığı kullanılıyor, aksi halde ödeme yapılana kadar her
    // taramada aynı üyeye tekrar tekrar bildirim giderdi.
    private const int ReminderCooldownDays = 7;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IPushNotificationSender _pushNotificationSender;
    private readonly ILogger<OutstandingBalanceReminderService> _logger;

    public OutstandingBalanceReminderService(IUnitOfWork unitOfWork, IPushNotificationSender pushNotificationSender, ILogger<OutstandingBalanceReminderService> logger)
    {
        _unitOfWork = unitOfWork;
        _pushNotificationSender = pushNotificationSender;
        _logger = logger;
    }

    public async Task<int> SendDueRemindersAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var cooldownCutoff = now.AddDays(-ReminderCooldownDays);

        // GetOutstandingBalancesReportQueryHandler ile aynı desen: kalan
        // bakiye stored bir kolon değil, Price - toplam ödeme olarak
        // hesaplanıyor, bu yüzden iki ayrı sorgu + bellekte join gerekiyor.
        var candidateAssignments = await _unitOfWork.GetReadRepository<PackageAssignment>().GetAllAsync(
            pa => pa.Status != PackageAssignmentStatus.Cancelled &&
                  (pa.LastPaymentReminderSentAt == null || pa.LastPaymentReminderSentAt <= cooldownCutoff),
            include: q => q.Include(pa => pa.Package).Include(pa => pa.MemberUser),
            cancellationToken: cancellationToken);

        if (candidateAssignments.Count == 0)
        {
            return 0;
        }

        var assignmentIds = candidateAssignments.Select(a => a.Id).ToList();
        var payments = await _unitOfWork.GetReadRepository<PackageAssignmentPayment>().GetAllAsync(
            p => assignmentIds.Contains(p.PackageAssignmentId), cancellationToken: cancellationToken);
        var paidByAssignmentId = payments.GroupBy(p => p.PackageAssignmentId).ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));

        var sentCount = 0;
        foreach (var assignment in candidateAssignments)
        {
            var totalPaid = paidByAssignmentId.GetValueOrDefault(assignment.Id);
            var remainingBalance = assignment.Package!.Price - totalPaid;
            if (remainingBalance <= 0)
            {
                continue;
            }

            // Her alıcı izole: atama alıcı başına izlenerek yeniden okunur ve
            // işaret bildirimle aynı SaveChanges'ta yazılır. Bir alıcının hatası
            // change tracker'ı temizlediği için (NotificationDispatcher) önceden
            // izlenen bir kopyaya yazmak sonrakiler için sessizce kaybolurdu.
            var trackedAssignment = await _unitOfWork.GetReadRepository<PackageAssignment>()
                .GetAsync(pa => pa.Id == assignment.Id, enableTracking: true, cancellationToken: cancellationToken);
            if (trackedAssignment is null)
            {
                continue;
            }
            trackedAssignment.LastPaymentReminderSentAt = now;

            // Metin üyenin kendi dilinde (PreferredLanguage).
            var language = assignment.MemberUser?.PreferredLanguage ?? "en";
            await NotificationDispatcher.TryNotifyUserAsync(
                _unitOfWork,
                _pushNotificationSender,
                _logger,
                assignment.MemberUserId,
                AppMessages.Resolve("OutstandingBalanceTitle", language),
                AppMessages.Resolve("OutstandingBalanceBody", language, assignment.Package.Name, remainingBalance.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo(language == "tr" ? "tr-TR" : "en-US"))),
                cancellationToken);
            sentCount++;
        }

        return sentCount;
    }
}
