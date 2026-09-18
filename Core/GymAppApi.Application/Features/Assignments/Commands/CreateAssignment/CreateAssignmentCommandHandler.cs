using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Invitations;
using GymAppApi.Application.Common.Notifications;
using GymAppApi.Application.Features.Assignments.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Assignments.Commands.CreateAssignment;

public class CreateAssignmentCommandHandler : IRequestHandler<CreateAssignmentCommand, CreateAssignmentCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISmsSender _smsSender;
    private readonly IPushNotificationSender _pushNotificationSender;

    public CreateAssignmentCommandHandler(IUnitOfWork unitOfWork, ISmsSender smsSender, IPushNotificationSender pushNotificationSender)
    {
        _unitOfWork = unitOfWork;
        _smsSender = smsSender;
        _pushNotificationSender = pushNotificationSender;
    }

    public async Task<CreateAssignmentCommandResult> Handle(CreateAssignmentCommand request, CancellationToken cancellationToken)
    {
        // [Authorize] policy'si sadece çağıranın BİR YERDE GymAdmin/SuperAdmin
        // olduğunu doğruluyor — bu isteğin BU şirketine kapsanmış olduğunu
        // tekrar kontrol et (SuperAdmin'in kendi CompanyId'si null/platform
        // geneli olduğundan şirket eşleşmesini atlar).
        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive,
            cancellationToken: cancellationToken);
        var callerIsAuthorizedForThisCompany = callerAssignments.Any(a =>
            a.Role == AssignmentRole.SuperAdmin ||
            (a.Role == AssignmentRole.GymAdmin && a.CompanyId == request.CompanyId));
        if (!callerIsAuthorizedForThisCompany)
        {
            throw new ForbiddenException("Bu firma için atama yapma yetkiniz yok.");
        }

        var user = await _unitOfWork.GetReadRepository<User>().GetAsync(u => u.Id == request.UserId, cancellationToken: cancellationToken);
        if (user is null)
        {
            throw new AssignmentUserNotFoundException(request.UserId);
        }

        var alreadyAssigned = await _unitOfWork.GetReadRepository<Assignment>()
            .AnyAsync(a => a.UserId == request.UserId && a.CompanyId == request.CompanyId && a.IsActive, cancellationToken);
        if (alreadyAssigned)
        {
            throw new UserAlreadyAssignedException();
        }

        // Güvenlik gereksinimi: çağıranın bu UserId'yi bilmesi tek başına
        // asla yeterli olmamalı - Assignment ancak davet edilen kişi kendi
        // onay kodunu (ConfirmAssignmentInvitationCommand) girdiğinde var
        // olur. CreateCompanyCommandHandler/AddStaffMemberCommandHandler'ın
        // aynı davet-onay akışı - bu eski/legacy endpoint artık aynı
        // korumayı sağlıyor, daha önce doğrudan ve rızasız Assignment
        // oluşturuyordu.
        var code = await AssignmentInvitationService.IssueAsync(
            _unitOfWork, user.Id, request.CompanyId, request.BranchId, AssignmentRole.Member, request.RequestedByUserId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _smsSender.SendAsync(
            user.Phone,
            $"GymApp'te bir firmaya üye olarak eklenmek üzeresiniz. Onay kodu: {code} (10 dakika geçerli).",
            cancellationToken);
        await NotificationDispatcher.NotifyUserAsync(
            _unitOfWork, _pushNotificationSender, user.Id,
            "Yeni firma daveti",
            "Bir firmaya eklenmeniz için davet gönderildi. Telefonunuza gelen kodla onaylayabilirsiniz.",
            cancellationToken);

        return new CreateAssignmentCommandResult
        {
            UserId = user.Id,
            CompanyId = request.CompanyId,
            BranchId = request.BranchId,
            Role = AssignmentRole.Member.ToString(),
        };
    }
}
