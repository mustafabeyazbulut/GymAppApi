using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.PackageAssignments;
using GymAppApi.Application.Common.Invitations;
using GymAppApi.Application.Features.Packages.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.Packages.Commands.ConfirmPackageAssignment;

public class ConfirmPackageAssignmentCommandHandler : IRequestHandler<ConfirmPackageAssignmentCommand, ConfirmPackageAssignmentCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;

    public ConfirmPackageAssignmentCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<ConfirmPackageAssignmentCommandResult> Handle(ConfirmPackageAssignmentCommand request, CancellationToken cancellationToken)
    {
        var invitationWriteRepo = _unitOfWork.GetWriteRepository<PendingPackageAssignmentInvitation>();
        var now = DateTime.UtcNow;

        var liveInvitations = await _unitOfWork.GetReadRepository<PendingPackageAssignmentInvitation>().GetAllAsync(
            p => p.TargetUserId == request.UserId && !p.IsUsed && p.ExpiresAt > now, cancellationToken: cancellationToken);

        var matching = liveInvitations.FirstOrDefault(p => p.Code == request.Code && p.AttemptCount < PackageAssignmentInvitationService.MaxAttempts);
        if (matching is null)
        {
            foreach (var invitation in liveInvitations.Where(p => p.AttemptCount < PackageAssignmentInvitationService.MaxAttempts))
            {
                invitation.AttemptCount += 1;
                invitationWriteRepo.Update(invitation);
            }
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            throw new InvalidPackageAssignmentInvitationCodeException();
        }

        matching.IsUsed = true;
        invitationWriteRepo.Update(matching);

        // Defense in depth, same rationale as ConfirmAssignmentInvitationCommandHandler:
        // something else could have assigned this package to the member in the
        // meantime. The invitation is consumed either way.
        // IgnoreQueryFilters (here and on the Package fetch below): the
        // confirming caller is the MEMBER, who typically has no Assignment
        // row at all, so their ambient CompanyId is always null (see the
        // standing rule in .claude/memory/project-member-package-linkage-design.md)
        // - without this, PackageAssignment/Package are invisible to them via
        // the ICompanyScoped filter, silently defeating the duplicate check
        // above and leaving RemainingSessions/EndDate null below (a package
        // fetch that "succeeds" with null). Safe because AnyAsync/the
        // downstream write are scoped by matching.PackageId/TargetUserId, not
        // by tenant.
        // Engel sadece bu pakette şu an GEÇERLİ bir atama (CreatePackageAssignment
        // ile aynı kural) - süresi dolmuş/hakkı bitmiş/iptal eski atama yenilemeyi
        // engellemez.
        var membersValidAssignments = await _unitOfWork.GetReadRepository<PackageAssignment>().GetAllAsync(
            PackageAssignmentValidity.UsableOwnedBy(matching.TargetUserId, now),
            include: q => q.IgnoreQueryFilters().Include(pa => pa.Package),
            cancellationToken: cancellationToken);
        if (membersValidAssignments.Any(pa => pa.PackageId == matching.PackageId))
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            throw new MemberAlreadyHasThisPackageException();
        }

        var package = await _unitOfWork.GetReadRepository<Package>().GetAsync(
            p => p.Id == matching.PackageId,
            include: q => q.IgnoreQueryFilters().Include(p => p.Company),
            cancellationToken: cancellationToken);

        var assignment = new PackageAssignment
        {
            PackageId = matching.PackageId,
            MemberUserId = matching.TargetUserId,
            CompanyId = matching.CompanyId,
            BranchId = matching.BranchId,
            AssignedByUserId = matching.RequestedByUserId,
            StartDate = now,
            EndDate = package?.DurationDays is int days ? now.AddDays(days) : null,
            RemainingSessions = package?.SessionCount,
            Status = PackageAssignmentStatus.Active,
        };
        await _unitOfWork.GetWriteRepository<PackageAssignment>().AddAsync(assignment, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ConfirmPackageAssignmentCommandResult
        {
            PackageAssignmentId = assignment.Id,
            PackageId = assignment.PackageId,
            EndDate = assignment.EndDate,
        };
    }
}
