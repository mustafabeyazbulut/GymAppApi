using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Localization;
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

    // Personel özet bildiriminde adıyla listelenen en fazla üye sayısı -
    // gerisi "ve N kişi daha" olarak özetlenir (bildirim metni kısa kalsın).
    private const int MaxMembersListedInStaffSummary = 5;

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
            include: q => q.Include(pa => pa.Package).Include(pa => pa.MemberUser),
            enableTracking: true,
            cancellationToken: cancellationToken);

        if (dueAssignments.Count == 0)
        {
            return 0;
        }

        foreach (var assignment in dueAssignments)
        {
            var daysRemaining = DaysRemaining(assignment, now);
            var packageName = assignment.Package?.Name ?? string.Empty;
            var language = assignment.MemberUser?.PreferredLanguage ?? "en";

            assignment.ExpiryReminderSentAt = now;
            _unitOfWork.GetWriteRepository<PackageAssignment>().Update(assignment);

            await NotificationDispatcher.NotifyUserAsync(
                _unitOfWork,
                _pushNotificationSender,
                assignment.MemberUserId,
                AppMessages.Resolve("MembershipExpiringTitle", language),
                daysRemaining <= 0
                    ? AppMessages.Resolve("MembershipExpiringTodayBody", language, packageName)
                    : AppMessages.Resolve("MembershipExpiringInDaysBody", language, packageName, daysRemaining),
                cancellationToken);
        }

        await NotifyStaffAsync(dueAssignments, now, cancellationToken);

        return dueAssignments.Count;
    }

    // Senaryo Akış D.1: sistem bitişe yaklaşan üyeye VE gym'e hatırlatma
    // gönderir. Alıcılar: atamanın şubesinin aktif Şube Yöneticileri ve
    // firmanın aktif Gym Admin'leri. Her personel, sorumlu olduğu tüm üyeler
    // için TEK bir özet bildirim alır (üye başına ayrı bildirim gürültü olurdu).
    private async Task NotifyStaffAsync(IReadOnlyList<PackageAssignment> dueAssignments, DateTime now, CancellationToken cancellationToken)
    {
        var companyIds = dueAssignments.Select(pa => pa.CompanyId).Distinct().ToList();
        var staff = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.IsActive && a.CompanyId != null && companyIds.Contains(a.CompanyId.Value) &&
                 (a.Role == AssignmentRole.GymAdmin || a.Role == AssignmentRole.BranchManager),
            cancellationToken: cancellationToken);

        var assignmentsByRecipient = new Dictionary<int, List<PackageAssignment>>();
        foreach (var packageAssignment in dueAssignments)
        {
            var recipients = staff.Where(a =>
                a.CompanyId == packageAssignment.CompanyId &&
                (a.Role == AssignmentRole.GymAdmin ||
                 (packageAssignment.BranchId != null && a.BranchId == packageAssignment.BranchId)));
            foreach (var recipientUserId in recipients.Select(a => a.UserId).Distinct())
            {
                if (!assignmentsByRecipient.TryGetValue(recipientUserId, out var list))
                {
                    assignmentsByRecipient[recipientUserId] = list = new List<PackageAssignment>();
                }
                list.Add(packageAssignment);
            }
        }

        if (assignmentsByRecipient.Count == 0)
        {
            return;
        }

        var recipientIds = assignmentsByRecipient.Keys.ToList();
        var recipientUsers = await _unitOfWork.GetReadRepository<User>().GetAllAsync(
            u => recipientIds.Contains(u.Id), cancellationToken: cancellationToken);
        var languageByUserId = recipientUsers.ToDictionary(u => u.Id, u => u.PreferredLanguage);

        foreach (var (recipientUserId, assignments) in assignmentsByRecipient)
        {
            var language = languageByUserId.GetValueOrDefault(recipientUserId, "en");
            var listed = assignments
                .OrderBy(pa => pa.EndDate)
                .Take(MaxMembersListedInStaffSummary)
                .Select(pa => AppMessages.Resolve("StaffExpiringMembershipsItem", language,
                    pa.MemberUser?.FullName ?? string.Empty, pa.Package?.Name ?? string.Empty, Math.Max(0, DaysRemaining(pa, now))))
                .ToList();
            var remaining = assignments.Count - listed.Count;
            if (remaining > 0)
            {
                listed.Add(AppMessages.Resolve("StaffExpiringMembershipsMore", language, remaining));
            }

            await NotificationDispatcher.NotifyUserAsync(
                _unitOfWork,
                _pushNotificationSender,
                recipientUserId,
                AppMessages.Resolve("StaffExpiringMembershipsTitle", language),
                AppMessages.Resolve("StaffExpiringMembershipsBody", language, assignments.Count, string.Join(", ", listed)),
                cancellationToken);
        }
    }

    private static int DaysRemaining(PackageAssignment assignment, DateTime now) =>
        (int)Math.Ceiling((assignment.EndDate!.Value - now).TotalDays);
}
