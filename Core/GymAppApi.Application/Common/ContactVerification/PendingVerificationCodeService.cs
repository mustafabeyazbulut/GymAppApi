using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;

namespace GymAppApi.Application.Common.ContactVerification;

// Shared by every flow that proves control of a phone/email via a 6-digit
// code (register, account freeze, account reactivation): issuing a code
// (rate-limited) and consuming an attempt against it. Extracted once a third
// call site needed the exact same rate-limit/attempt-counting logic that
// RegisterRequestOtp/RegisterComplete already had.
public static class PendingVerificationCodeService
{
    public const int MaxAttempts = 5;
    private const int CodeExpiryMinutes = 10;
    private const int CooldownSeconds = 60;
    private const int MaxSendsPerWindow = 5;
    private static readonly TimeSpan SendWindow = TimeSpan.FromHours(1);

    // Does not call SaveChangesAsync - the caller controls when the pending
    // row is persisted (so it can batch it with other writes) and is
    // responsible for actually sending the code afterward.
    public static async Task<string> IssueAsync(
        IUnitOfWork unitOfWork, ContactChannel channel, string target, CancellationToken cancellationToken)
    {
        var pendingReadRepo = unitOfWork.GetReadRepository<PendingContactVerification>();
        var pendingWriteRepo = unitOfWork.GetWriteRepository<PendingContactVerification>();
        var now = DateTime.UtcNow;

        var existing = await pendingReadRepo.GetAsync(
            p => p.Channel == channel && p.Target == target, cancellationToken: cancellationToken);
        EnsureWithinSendLimits(existing, now);
        var code = GenerateCode();
        await UpsertPendingAsync(existing, channel, target, code, now, pendingWriteRepo, cancellationToken);
        return code;
    }

    // Does not call SaveChangesAsync - on a failed attempt the caller must
    // still persist the incremented AttemptCount before throwing.
    public static bool TryConsumeAttempt(
        PendingContactVerification? pending, string code, IWriteRepository<PendingContactVerification> writeRepo)
    {
        if (pending is null || pending.ExpiresAt <= DateTime.UtcNow || pending.AttemptCount >= MaxAttempts)
        {
            return false;
        }

        if (pending.Code != code)
        {
            pending.AttemptCount += 1;
            writeRepo.Update(pending);
            return false;
        }

        return true;
    }

    private static void EnsureWithinSendLimits(PendingContactVerification? pending, DateTime now)
    {
        if (pending is null)
        {
            return;
        }

        if (now - pending.LastSentAt < TimeSpan.FromSeconds(CooldownSeconds))
        {
            throw new TooManyVerificationRequestsException();
        }

        var windowExpired = now - pending.WindowStartAt >= SendWindow;
        if (!windowExpired && pending.SendCount >= MaxSendsPerWindow)
        {
            throw new TooManyVerificationRequestsException();
        }
    }

    private static async Task UpsertPendingAsync(
        PendingContactVerification? existing, ContactChannel channel, string target, string code, DateTime now,
        IWriteRepository<PendingContactVerification> writeRepo, CancellationToken cancellationToken)
    {
        if (existing is null)
        {
            await writeRepo.AddAsync(new PendingContactVerification
            {
                Channel = channel,
                Target = target,
                Code = code,
                ExpiresAt = now.AddMinutes(CodeExpiryMinutes),
                AttemptCount = 0,
                LastSentAt = now,
                SendCount = 1,
                WindowStartAt = now,
            }, cancellationToken);
            return;
        }

        var windowExpired = now - existing.WindowStartAt >= SendWindow;
        existing.Code = code;
        existing.ExpiresAt = now.AddMinutes(CodeExpiryMinutes);
        existing.AttemptCount = 0;
        existing.LastSentAt = now;
        existing.SendCount = windowExpired ? 1 : existing.SendCount + 1;
        existing.WindowStartAt = windowExpired ? now : existing.WindowStartAt;
        writeRepo.Update(existing);
    }

    private static string GenerateCode() => Random.Shared.Next(100000, 999999).ToString();
}
