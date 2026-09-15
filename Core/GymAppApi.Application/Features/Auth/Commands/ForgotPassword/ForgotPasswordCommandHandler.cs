using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Auth.Commands.ForgotPassword;

public class ForgotPasswordCommandHandler : IRequestHandler<ForgotPasswordCommand, ForgotPasswordCommandResult>
{
    private const string GenericMessage = "Hesabınız varsa, şifre sıfırlama kodu gönderildi.";

    private readonly IUnitOfWork _unitOfWork;
    private readonly ISmsSender _smsSender;
    private readonly IEmailSender _emailSender;

    public ForgotPasswordCommandHandler(IUnitOfWork unitOfWork, ISmsSender smsSender, IEmailSender emailSender)
    {
        _unitOfWork = unitOfWork;
        _smsSender = smsSender;
        _emailSender = emailSender;
    }

    public async Task<ForgotPasswordCommandResult> Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await _unitOfWork.GetReadRepository<User>()
            .GetAsync(u => u.Phone == request.Identifier || u.Email == request.Identifier, cancellationToken: cancellationToken);

        // Never reveal whether the identifier matched an account.
        if (user is null)
        {
            return new ForgotPasswordCommandResult { Message = GenericMessage };
        }

        var otpWriteRepo = _unitOfWork.GetWriteRepository<OtpVerification>();

        // Invalidate any still-live reset codes for this user first, so at
        // most one PasswordReset OtpVerification row is ever valid at once.
        // Without this, requesting a new code twice (e.g. "didn't get the
        // SMS, send again") leaves multiple valid rows and ResetPassword's
        // single-row lookup (no ORDER BY) could match the wrong one,
        // rejecting a genuinely correct code and burning an attempt against
        // a row the user can never satisfy.
        var priorLiveOtps = await _unitOfWork.GetReadRepository<OtpVerification>().GetAllAsync(
            o => o.UserId == user.Id && o.Purpose == OtpPurpose.PasswordReset && !o.IsUsed && o.ExpiresAt > DateTime.UtcNow,
            cancellationToken: cancellationToken);
        foreach (var prior in priorLiveOtps)
        {
            prior.IsUsed = true;
            otpWriteRepo.Update(prior);
        }

        var code = Random.Shared.Next(100000, 999999).ToString();

        await otpWriteRepo.AddAsync(new OtpVerification
        {
            UserId = user.Id,
            Code = code,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            Purpose = OtpPurpose.PasswordReset,
            IsUsed = false,
            AttemptCount = 0,
        }, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var message = $"GymApp şifre sıfırlama kodunuz: {code}. Kod 10 dakika geçerlidir.";
        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            await _emailSender.SendAsync(user.Email, "GymApp Şifre Sıfırlama", message, cancellationToken);
        }
        else
        {
            await _smsSender.SendAsync(user.Phone, message, cancellationToken);
        }

        return new ForgotPasswordCommandResult { Message = GenericMessage };
    }
}
