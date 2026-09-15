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

        var code = Random.Shared.Next(100000, 999999).ToString();

        await _unitOfWork.GetWriteRepository<OtpVerification>().AddAsync(new OtpVerification
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
