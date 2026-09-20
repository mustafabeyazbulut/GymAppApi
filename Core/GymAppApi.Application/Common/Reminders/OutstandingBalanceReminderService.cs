using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Notifications;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Microsoft.EntityFrameworkCore;

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

    public OutstandingBalanceReminderService(IUnitOfWork unitOfWork, IPushNotificationSender pushNotificationSender)
    {
        _unitOfWork = unitOfWork;
        _pushNotificationSender = pushNotificationSender;
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
            include: q => q.Include(pa => pa.Package),
            enableTracking: true,
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

            assignment.LastPaymentReminderSentAt = now;
            _unitOfWork.GetWriteRepository<PackageAssignment>().Update(assignment);

            await NotificationDispatcher.NotifyUserAsync(
                _unitOfWork,
                _pushNotificationSender,
                assignment.MemberUserId,
                "Bekleyen Ödemeniz Var",
                $"{assignment.Package.Name} paketiniz için {remainingBalance:N0} ₺ bakiyeniz kalıyor. Ödemenizi salonunuzda tamamlayabilirsiniz.",
                cancellationToken);
            sentCount++;
        }

        return sentCount;
    }
}
