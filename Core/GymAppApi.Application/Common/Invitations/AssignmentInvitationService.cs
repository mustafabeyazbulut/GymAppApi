using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;

namespace GymAppApi.Application.Common.Invitations;

// Shared by CreateCompanyCommandHandler (GymAdmin) and
// AddStaffMemberCommandHandler (Member/Trainer): neither ever attaches an
// existing user to a company directly from the inviter's own request. Both
// issue a confirmation code here instead, which only the INVITEE can redeem
// (see ConfirmAssignmentInvitationCommand) — the assignment only exists once
// they prove control of their own phone. Security requirement: a
// GymAdmin/BranchManager/SuperAdmin knowing someone's phone number must never
// be enough, by itself, to attach that person to a company.
public static class AssignmentInvitationService
{
    public const int MaxAttempts = 5;
    // Davetin kendisi uygulama içi "Davetlerim" listesinden kabul/red edilebilsin
    // diye günlerce yaşar; SMS kodu ise sadece gönderildikten sonraki kısa
    // pencerede geçerlidir (confirm handler'ı CreatedAt ile kontrol eder).
    public const int InvitationValidityDays = 7;
    public const int CodeValidityMinutes = 10;
    private const int CooldownSeconds = 60;

    // Does not call SaveChangesAsync or send the SMS - the caller does both
    // (so it can batch with other writes and control exactly when the code
    // goes out).
    public static async Task<string> IssueAsync(
        IUnitOfWork unitOfWork, int targetUserId, int companyId, int? branchId, AssignmentRole role,
        int requestedByUserId, CancellationToken cancellationToken)
    {
        var readRepo = unitOfWork.GetReadRepository<PendingAssignmentInvitation>();
        var writeRepo = unitOfWork.GetWriteRepository<PendingAssignmentInvitation>();
        var now = DateTime.UtcNow;

        // Scoped to (targetUserId, companyId) specifically - a user can
        // legitimately have separate live invitations to DIFFERENT companies
        // at once (multi-company staff/members are a supported scenario, see
        // TenantResolutionService), so inviting them elsewhere must not
        // invalidate an unrelated pending invite.
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
        await writeRepo.AddAsync(new PendingAssignmentInvitation
        {
            TargetUserId = targetUserId,
            CompanyId = companyId,
            BranchId = branchId,
            Role = role,
            RequestedByUserId = requestedByUserId,
            Code = code,
            ExpiresAt = now.AddDays(InvitationValidityDays),
            AttemptCount = 0,
            IsUsed = false,
        }, cancellationToken);

        return code;
    }
}
