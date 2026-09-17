using GymAppApi.Application.Common.ContactVerification;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Auth.Commands.RegisterRequestOtp;

public class RegisterRequestOtpCommandHandler : IRequestHandler<RegisterRequestOtpCommand>
{
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

        var phoneCode = await PendingVerificationCodeService.IssueAsync(_unitOfWork, ContactChannel.Phone, request.Phone, cancellationToken);

        string? emailCode = null;
        if (hasEmail)
        {
            emailCode = await PendingVerificationCodeService.IssueAsync(_unitOfWork, ContactChannel.Email, normalizedEmail!, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _smsSender.SendAsync(request.Phone, $"GymApp doğrulama kodunuz: {phoneCode}. Kod 10 dakika geçerlidir.", cancellationToken);
        if (hasEmail)
        {
            await _emailSender.SendAsync(normalizedEmail!, "GymApp E-posta Doğrulama", $"GymApp doğrulama kodunuz: {emailCode}. Kod 10 dakika geçerlidir.", cancellationToken);
        }
    }
}
