using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Localization;
using GymAppApi.Application.Common.Notifications;
using GymAppApi.Application.Features.Invitations.Common;
using GymAppApi.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.Invitations.Commands.RejectInvitation;

// Davet reddedilince kullanılmış işaretlenir (bir daha kabul/red edilemez,
// listeden düşer) ve davet edene uygulama içi bildirim gider - metin davet
// edenin kendi dilinde (PreferredLanguage).
public class RejectInvitationCommandHandler : IRequestHandler<RejectInvitationCommand>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPushNotificationSender _pushNotificationSender;

    public RejectInvitationCommandHandler(IUnitOfWork unitOfWork, IPushNotificationSender pushNotificationSender)
    {
        _unitOfWork = unitOfWork;
        _pushNotificationSender = pushNotificationSender;
    }

    public async Task Handle(RejectInvitationCommand request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        int inviterUserId;
        int companyId;
        string? packageName = null;
        string? roleLabelCode = null;

        if (InvitationLookup.IsPackage(request.Type))
        {
            var invitation = await InvitationLookup.FindPackageInvitationAsync(_unitOfWork, request.InvitationId, request.UserId, now, cancellationToken);
            invitation.IsUsed = true;
            _unitOfWork.GetWriteRepository<PendingPackageAssignmentInvitation>().Update(invitation);
            inviterUserId = invitation.RequestedByUserId;
            companyId = invitation.CompanyId;
            var package = await _unitOfWork.GetReadRepository<Package>().GetAsync(
                p => p.Id == invitation.PackageId, include: q => q.IgnoreQueryFilters().Include(p => p.Company), cancellationToken: cancellationToken);
            packageName = package?.Name;
        }
        else if (InvitationLookup.IsAssignment(request.Type))
        {
            var invitation = await InvitationLookup.FindAssignmentInvitationAsync(_unitOfWork, request.Type, request.InvitationId, request.UserId, now, cancellationToken);
            invitation.IsUsed = true;
            _unitOfWork.GetWriteRepository<PendingAssignmentInvitation>().Update(invitation);
            inviterUserId = invitation.RequestedByUserId;
            companyId = invitation.CompanyId;
            roleLabelCode = $"RoleLabel{invitation.Role}";
        }
        else
        {
            throw new NotFoundException("InvitationNotFound", request.InvitationId);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var invitee = await _unitOfWork.GetReadRepository<User>().GetAsync(u => u.Id == request.UserId, cancellationToken: cancellationToken);
        var inviter = await _unitOfWork.GetReadRepository<User>().GetAsync(u => u.Id == inviterUserId, cancellationToken: cancellationToken);
        var company = await _unitOfWork.GetReadRepository<Company>().GetAsync(
            c => c.Id == companyId, include: q => q.IgnoreQueryFilters().Include(c => c.Branches), cancellationToken: cancellationToken);
        if (inviter is null)
        {
            return;
        }

        var language = inviter.PreferredLanguage;
        var subject = packageName ?? AppMessages.Resolve(roleLabelCode!, language);
        await NotificationDispatcher.NotifyUserAsync(
            _unitOfWork,
            _pushNotificationSender,
            inviter.Id,
            AppMessages.Resolve("InvitationRejectedTitle", language),
            AppMessages.Resolve("InvitationRejectedBody", language, invitee?.FullName ?? string.Empty, company?.Name ?? string.Empty, subject),
            cancellationToken);
    }
}
