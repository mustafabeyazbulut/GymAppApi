using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Invitations;
using GymAppApi.Application.Common.Notifications;
using GymAppApi.Application.Features.Packages.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Packages.Commands.CreatePackageAssignment;

public class CreatePackageAssignmentCommandHandler : IRequestHandler<CreatePackageAssignmentCommand, CreatePackageAssignmentCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPhoneNumberNormalizer _phoneNumberNormalizer;
    private readonly ISmsSender _smsSender;
    private readonly IPushNotificationSender _pushNotificationSender;

    public CreatePackageAssignmentCommandHandler(IUnitOfWork unitOfWork, ISmsSender smsSender, IPushNotificationSender pushNotificationSender, IPhoneNumberNormalizer phoneNumberNormalizer)
    {
        _unitOfWork = unitOfWork;
        _phoneNumberNormalizer = phoneNumberNormalizer;
        _smsSender = smsSender;
        _pushNotificationSender = pushNotificationSender;
    }

    public async Task<CreatePackageAssignmentCommandResult> Handle(CreatePackageAssignmentCommand request, CancellationToken cancellationToken)
    {
        // Telefon her zaman kanonik E.164 olarak aranır/saklanır/SMS'e verilir -
        // validator ValidPhoneNumber ile geçerliliği zaten garanti ediyor.
        var phone = _phoneNumberNormalizer.NormalizeIfPhone(request.MemberPhone);

        var package = await _unitOfWork.GetReadRepository<Package>()
            .GetAsync(p => p.Id == request.PackageId, cancellationToken: cancellationToken);
        if (package is null)
        {
            throw new NotFoundException("PackageNotFound", request.PackageId);
        }

        // A company-wide package (BranchId == null) may only be assigned by a
        // GymAdmin, same limit as creating one - a BranchManager may
        // only assign a package scoped to their own exact branch.
        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);
        var callerIsAuthorized = package.BranchId is null
            ? callerAssignments.Any(a =>
                (a.Role == AssignmentRole.GymAdmin && a.CompanyId == package.CompanyId))
            : callerAssignments.Any(a =>
                (a.Role == AssignmentRole.GymAdmin && a.CompanyId == package.CompanyId) ||
                (a.Role == AssignmentRole.BranchManager && a.BranchId == package.BranchId));
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("ForbiddenAssignPackage");
        }

        var member = await _unitOfWork.GetReadRepository<User>()
            .GetAsync(u => u.Phone == phone, cancellationToken: cancellationToken);
        if (member is null)
        {
            throw new NotFoundException("PhoneNotRegistered", phone);
        }

        var alreadyHasThisPackage = await _unitOfWork.GetReadRepository<PackageAssignment>().AnyAsync(
            pa => pa.MemberUserId == member.Id && pa.PackageId == package.Id && pa.Status != PackageAssignmentStatus.Cancelled,
            cancellationToken);
        if (alreadyHasThisPackage)
        {
            throw new MemberAlreadyHasThisPackageException();
        }

        var code = await PackageAssignmentInvitationService.IssueAsync(
            _unitOfWork, member.Id, package.Id, package.CompanyId, package.BranchId, request.RequestedByUserId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _smsSender.SendAsync(
            phone,
            $"GymApp'te '{package.Name}' paketi size tanımlanmak üzere. Onay kodu: {code} (10 dakika geçerli).",
            cancellationToken);
        await NotificationDispatcher.NotifyUserAsync(
            _unitOfWork, _pushNotificationSender, member.Id,
            "Yeni paket daveti",
            "Bir paket size tanımlanmak üzere. Telefonunuza gelen kodla onaylayabilirsiniz.",
            cancellationToken);

        return new CreatePackageAssignmentCommandResult { UserId = member.Id, PackageId = package.Id };
    }
}
