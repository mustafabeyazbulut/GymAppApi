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
    private readonly ISmsSender _smsSender;
    private readonly IPushNotificationSender _pushNotificationSender;

    public CreateCompanyCommandHandler(IUnitOfWork unitOfWork, ISmsSender smsSender, IPushNotificationSender pushNotificationSender)
    {
        _unitOfWork = unitOfWork;
        _smsSender = smsSender;
        _pushNotificationSender = pushNotificationSender;
    }

    public async Task<CreateCompanyCommandResult> Handle(CreateCompanyCommand request, CancellationToken cancellationToken)
    {
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
            .GetAsync(u => u.Phone == request.GymAdminPhone, cancellationToken: cancellationToken);
        if (gymAdminUser is null)
        {
            throw new NotFoundException($"'{request.GymAdminPhone}' numaralı kayıtlı bir kullanıcı bulunamadı.");
        }

        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var company = new Company { Name = request.CompanyName, IsActive = true };
            await _unitOfWork.GetWriteRepository<Company>().AddAsync(company, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken); // need company.Id for the branch

            var branch = new Branch
            {
                CompanyId = company.Id,
                Name = request.BranchName,
                Address = request.BranchAddress,
                IsActive = true,
            };
            await _unitOfWork.GetWriteRepository<Branch>().AddAsync(branch, cancellationToken);

            // Security requirement: SuperAdmin knowing this phone number is
            // never enough by itself to make someone a GymAdmin - the
            // Assignment only comes into existence once the invitee confirms
            // this code themselves (ConfirmAssignmentInvitationCommand).
            // GymAdmin's BranchId is null by design (Assignment.cs's own
            // comment: a GymAdmin assignment has CompanyId set, BranchId
            // null, meaning "all branches of this company").
            var code = await AssignmentInvitationService.IssueAsync(
                _unitOfWork, gymAdminUser.Id, company.Id, null, AssignmentRole.GymAdmin, request.RequestedByUserId, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            await _smsSender.SendAsync(
                request.GymAdminPhone,
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
                BranchId = branch.Id,
                GymAdminUserId = gymAdminUser.Id,
                GymAdminPhone = gymAdminUser.Phone,
            };
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }
}
