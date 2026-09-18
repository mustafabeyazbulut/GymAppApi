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
    private readonly ISmsSender _smsSender;
    private readonly IPushNotificationSender _pushNotificationSender;

    public CreatePackageAssignmentCommandHandler(IUnitOfWork unitOfWork, ISmsSender smsSender, IPushNotificationSender pushNotificationSender)
    {
        _unitOfWork = unitOfWork;
        _smsSender = smsSender;
        _pushNotificationSender = pushNotificationSender;
    }

    public async Task<CreatePackageAssignmentCommandResult> Handle(CreatePackageAssignmentCommand request, CancellationToken cancellationToken)
    {
        var package = await _unitOfWork.GetReadRepository<Package>()
            .GetAsync(p => p.Id == request.PackageId, cancellationToken: cancellationToken);
        if (package is null)
        {
            throw new NotFoundException($"Paket {request.PackageId} bulunamadı.");
        }

        // A company-wide package (BranchId == null) may only be assigned by a
        // GymAdmin/SuperAdmin, same limit as creating one - a BranchManager may
        // only assign a package scoped to their own exact branch.
        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);
        var callerIsAuthorized = package.BranchId is null
            ? callerAssignments.Any(a =>
                a.Role == AssignmentRole.SuperAdmin ||
                (a.Role == AssignmentRole.GymAdmin && a.CompanyId == package.CompanyId))
            : callerAssignments.Any(a =>
                a.Role == AssignmentRole.SuperAdmin ||
                (a.Role == AssignmentRole.GymAdmin && a.CompanyId == package.CompanyId) ||
                (a.Role == AssignmentRole.BranchManager && a.BranchId == package.BranchId));
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("Bu paketi atama yetkiniz yok.");
        }

        var member = await _unitOfWork.GetReadRepository<User>()
            .GetAsync(u => u.Phone == request.MemberPhone, cancellationToken: cancellationToken);
        if (member is null)
        {
            throw new NotFoundException($"'{request.MemberPhone}' numaralı kayıtlı bir kullanıcı bulunamadı.");
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
            request.MemberPhone,
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
