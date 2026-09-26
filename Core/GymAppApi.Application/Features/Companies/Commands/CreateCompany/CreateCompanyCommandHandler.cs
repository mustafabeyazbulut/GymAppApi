using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Invitations;
using GymAppApi.Application.Common.Notifications;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Companies.Commands.CreateCompany;

public class CreateCompanyCommandHandler : IRequestHandler<CreateCompanyCommand, CreateCompanyCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPhoneNumberNormalizer _phoneNumberNormalizer;
    private readonly ISmsSender _smsSender;
    private readonly IPushNotificationSender _pushNotificationSender;

    public CreateCompanyCommandHandler(IUnitOfWork unitOfWork, ISmsSender smsSender, IPushNotificationSender pushNotificationSender, IPhoneNumberNormalizer phoneNumberNormalizer)
    {
        _unitOfWork = unitOfWork;
        _phoneNumberNormalizer = phoneNumberNormalizer;
        _smsSender = smsSender;
        _pushNotificationSender = pushNotificationSender;
    }

    public async Task<CreateCompanyCommandResult> Handle(CreateCompanyCommand request, CancellationToken cancellationToken)
    {
        // Telefon her zaman kanonik E.164 olarak aranır/saklanır/SMS'e verilir -
        // validator ValidPhoneNumber ile geçerliliği zaten garanti ediyor.
        var phone = _phoneNumberNormalizer.NormalizeIfPhone(request.GymAdminPhone);

        // No [Authorize(Policy = "SuperAdminOnly")]-level re-check needed here
        // unlike CreateAssignmentCommandHandler's GymAdmin case - a SuperAdmin
        // has no per-company scope to violate, so the policy's own fresh
        // per-request Assignment re-query (AssignmentRoleAuthorizationHandler)
        // is already the complete check.
        //
        // Never creates a new User — the Gym Admin must already be a
        // registered user, picked up by phone. See
        // .claude/memory/feedback-never-remove-registration-pointer.md.
        var gymAdminUser = await _unitOfWork.GetReadRepository<User>()
            .GetAsync(u => u.Phone == phone, cancellationToken: cancellationToken);
        if (gymAdminUser is null)
        {
            throw new NotFoundException("PhoneNotRegistered", phone);
        }

        // ExecuteWithRetryAsync icinden aciliyor - RegisterCompleteCommandHandler'daki
        // ayni fix ve gerekce (bkz. o dosyadaki yorum): DbContext'in
        // EnableRetryOnFailure execution strategy'si, kullanici tarafindan
        // baslatilan bir transaction'i ancak begin/commit/rollback'in TAMAMI
        // kendi ExecuteAsync delegate'inin icindeyse yeniden deneyebiliyor.
        // SMS/push bildirimleri kasitli olarak DISARIDA - commit'ten sonra
        // bir retry bu yan etkileri tekrar tetiklemesin diye.
        var (company, code) = await _unitOfWork.ExecuteWithRetryAsync(async () =>
        {
            await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);
            try
            {
                var company = new Company { Name = request.CompanyName, IsActive = true };
                await _unitOfWork.GetWriteRepository<Company>().AddAsync(company, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken); // need company.Id for the invitation

                // Security requirement: SuperAdmin knowing this phone number is
                // never enough by itself to make someone a GymAdmin - the
                // Assignment only comes into existence once the invitee confirms
                // this code themselves (ConfirmAssignmentInvitationCommand).
                // No Branch is created here either - that's the new GymAdmin's
                // own call once they've confirmed (POST /api/branches), not
                // something SuperAdmin decides on their behalf.
                var code = await AssignmentInvitationService.IssueAsync(
                    _unitOfWork, gymAdminUser.Id, company.Id, null, AssignmentRole.GymAdmin, request.RequestedByUserId, cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                return (company, code);
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                throw;
            }
        });

        await _smsSender.SendAsync(
            phone,
            $"GymApp'te '{request.CompanyName}' firmasının Gym Admin'i olmak üzeresiniz. Onay kodu: {code} (10 dakika geçerli).",
            cancellationToken);
        await NotificationDispatcher.NotifyUserAsync(
            _unitOfWork, _pushNotificationSender, gymAdminUser.Id,
            "Yeni firma daveti",
            "Bir firmanın Gym Admin'i olmanız için davet gönderildi. Telefonunuza gelen kodla onaylayabilirsiniz.",
            cancellationToken);

        return new CreateCompanyCommandResult
        {
            CompanyId = company.Id,
            GymAdminUserId = gymAdminUser.Id,
            GymAdminPhone = gymAdminUser.Phone,
        };
    }
}
