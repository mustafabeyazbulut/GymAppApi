using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Exceptions;
using GymAppApi.Domain.Entities;

namespace GymAppApi.Application.Common.Invitations;

// Mirrors AssignmentInvitationService exactly, for Package instead of role
// assignments - see CreatePackageAssignmentCommandHandler/ConfirmPackageAssignmentCommand.
public static class PackageAssignmentInvitationService
{
    public const int MaxAttempts = 5;
    private const int CodeExpiryMinutes = 10;
    private const int CooldownSeconds = 60;

    public static async Task<string> IssueAsync(
        IUnitOfWork unitOfWork, int targetUserId, int packageId, int companyId, int? branchId,
        int requestedByUserId, CancellationToken cancellationToken)
    {
        var readRepo = unitOfWork.GetReadRepository<PendingPackageAssignmentInvitation>();
        var writeRepo = unitOfWork.GetWriteRepository<PendingPackageAssignmentInvitation>();
        var now = DateTime.UtcNow;

        var priorLive = await readRepo.GetAllAsync(
            p => p.TargetUserId == targetUserId && p.CompanyId == companyId && !p.IsUsed && p.ExpiresAt > now,
            cancellationToken: cancellationToken);

        var mostRecentPrior = priorLive.OrderByDescending(p => p.CreatedAt).FirstOrDefault();
        if (mostRecentPrior is not null && now - mostRecentPrior.CreatedAt < TimeSpan.FromSeconds(CooldownSeconds))
        {
            throw new TooManyVerificationRequestsException();
        }

        foreach (var prior in priorLive)
        {
            prior.IsUsed = true;
            writeRepo.Update(prior);
        }

        var code = Random.Shared.Next(100000, 999999).ToString();
        await writeRepo.AddAsync(new PendingPackageAssignmentInvitation
        {
            TargetUserId = targetUserId,
            PackageId = packageId,
            CompanyId = companyId,
            BranchId = branchId,
            RequestedByUserId = requestedByUserId,
            Code = code,
            ExpiresAt = now.AddMinutes(CodeExpiryMinutes),
            AttemptCount = 0,
            IsUsed = false,
        }, cancellationToken);

        return code;
    }
}
