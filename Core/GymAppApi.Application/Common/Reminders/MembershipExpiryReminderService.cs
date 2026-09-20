using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Notifications;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Common.Reminders;

public class MembershipExpiryReminderService : IMembershipExpiryReminderService
{
    // Yenileme/satış açısından anlamlı bir uyarı penceresi - GetExpiringMembershipsReportQuery'nin
    // varsayılan 30 günlük "kimi aramalıyız" listesinden farklı olarak burada
    // tek, kesin bir bildirim eşiği kullanılıyor (30 gün önceden her gün
    // hatırlatma göndermek gürültü olurdu).
    private const int ReminderDaysBeforeExpiry = 3;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IPushNotificationSender _pushNotificationSender;

    public MembershipExpiryReminderService(IUnitOfWork unitOfWork, IPushNotificationSender pushNotificationSender)
    {
        _unitOfWork = unitOfWork;
        _pushNotificationSender = pushNotificationSender;
    }

    public async Task<int> SendDueRemindersAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var cutoff = now.AddDays(ReminderDaysBeforeExpiry);

        var dueAssignments = await _unitOfWork.GetReadRepository<PackageAssignment>().GetAllAsync(
            pa => pa.Status == PackageAssignmentStatus.Active &&
                  pa.EndDate != null &&
                  pa.EndDate <= cutoff &&
                  pa.EndDate > now &&
                  pa.ExpiryReminderSentAt == null,
            include: q => q.Include(pa => pa.Package),
            enableTracking: true,
            cancellationToken: cancellationToken);

        foreach (var assignment in dueAssignments)
        {
            var daysRemaining = (int)Math.Ceiling((assignment.EndDate!.Value - now).TotalDays);
            var packageName = assignment.Package?.Name ?? "Üyelik";

            assignment.ExpiryReminderSentAt = now;
            _unitOfWork.GetWriteRepository<PackageAssignment>().Update(assignment);

            await NotificationDispatcher.NotifyUserAsync(
                _unitOfWork,
                _pushNotificationSender,
                assignment.MemberUserId,
                "Üyeliğiniz Yakında Sona Eriyor",
                daysRemaining <= 0
                    ? $"{packageName} paketiniz bugün sona eriyor. Yenilemek için salonunuzla iletişime geçebilirsiniz."
                    : $"{packageName} paketiniz {daysRemaining} gün sonra sona eriyor. Yenilemek için salonunuzla iletişime geçebilirsiniz.",
                cancellationToken);
        }

        return dueAssignments.Count;
    }
}
