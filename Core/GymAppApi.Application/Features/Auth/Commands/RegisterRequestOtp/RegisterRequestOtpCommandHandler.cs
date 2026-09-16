using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Auth.Commands.RegisterRequestOtp;

public class RegisterRequestOtpCommandHandler : IRequestHandler<RegisterRequestOtpCommand>
{
    private const int CodeExpiryMinutes = 10;
    private const int CooldownSeconds = 60;
    private const int MaxSendsPerWindow = 5;
    private static readonly TimeSpan SendWindow = TimeSpan.FromHours(1);

    private readonly IUnitOfWork _unitOfWork;
    private readonly ISmsSender _smsSender;
    private readonly IEmailSender _emailSender;

    public RegisterRequestOtpCommandHandler(IUnitOfWork unitOfWork, ISmsSender smsSender, IEmailSender emailSender)
    {
        _unitOfWork = unitOfWork;
        _smsSender = smsSender;
        _emailSender = emailSender;
    }

    public async Task Handle(RegisterRequestOtpCommand request, CancellationToken cancellationToken)
    {
        var userReadRepo = _unitOfWork.GetReadRepository<User>();

        if (await userReadRepo.AnyAsync(u => u.Phone == request.Phone, cancellationToken))
        {
            throw new PhoneAlreadyRegisteredException();
        }

        var hasEmail = !string.IsNullOrWhiteSpace(request.Email);
        var normalizedEmail = hasEmail ? request.Email!.Trim().ToLowerInvariant() : null;

        if (hasEmail && await userReadRepo.AnyAsync(u => u.Email == request.Email, cancellationToken))
        {
            throw new EmailAlreadyRegisteredException();
        }

        var pendingReadRepo = _unitOfWork.GetReadRepository<PendingContactVerification>();
        var pendingWriteRepo = _unitOfWork.GetWriteRepository<PendingContactVerification>();
        var now = DateTime.UtcNow;

        var phonePending = await pendingReadRepo.GetAsync(
            p => p.Channel == ContactChannel.Phone && p.Target == request.Phone, cancellationToken: cancellationToken);
        EnsureWithinSendLimits(phonePending, now);
        var phoneCode = GenerateCode();
        await UpsertPendingAsync(phonePending, ContactChannel.Phone, request.Phone, phoneCode, now, pendingWriteRepo, cancellationToken);

        PendingContactVerification? emailPending = null;
        string? emailCode = null;
        if (hasEmail)
        {
            emailPending = await pendingReadRepo.GetAsync(
                p => p.Channel == ContactChannel.Email && p.Target == normalizedEmail, cancellationToken: cancellationToken);
            EnsureWithinSendLimits(emailPending, now);
            emailCode = GenerateCode();
            await UpsertPendingAsync(emailPending, ContactChannel.Email, normalizedEmail!, emailCode, now, pendingWriteRepo, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _smsSender.SendAsync(request.Phone, $"GymApp doğrulama kodunuz: {phoneCode}. Kod 10 dakika geçerlidir.", cancellationToken);
        if (hasEmail)
        {
            await _emailSender.SendAsync(normalizedEmail!, "GymApp E-posta Doğrulama", $"GymApp doğrulama kodunuz: {emailCode}. Kod 10 dakika geçerlidir.", cancellationToken);
        }
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
