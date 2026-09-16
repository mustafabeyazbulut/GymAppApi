using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Auth.Commands.ResetPassword;

public class ResetPasswordCommandHandler : IRequestHandler<ResetPasswordCommand>
{
    private const int MaxAttempts = 5;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IPhoneNumberNormalizer _phoneNumberNormalizer;

    public ResetPasswordCommandHandler(
        IUnitOfWork unitOfWork, IPasswordHasher passwordHasher, IPhoneNumberNormalizer phoneNumberNormalizer)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _phoneNumberNormalizer = phoneNumberNormalizer;
    }

    public async Task Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var identifier = _phoneNumberNormalizer.NormalizeIfPhone(request.Identifier);
        var user = await _unitOfWork.GetReadRepository<User>()
            .GetAsync(u => u.Phone == identifier || u.Email == identifier, cancellationToken: cancellationToken);
        if (user is null)
        {
            throw new InvalidResetCodeException();
        }

        var otpReadRepo = _unitOfWork.GetReadRepository<OtpVerification>();
        var otpWriteRepo = _unitOfWork.GetWriteRepository<OtpVerification>();

        var otp = await otpReadRepo.GetAsync(
            o => o.UserId == user.Id && o.Purpose == OtpPurpose.PasswordReset && !o.IsUsed && o.ExpiresAt > DateTime.UtcNow,
            cancellationToken: cancellationToken);

        if (otp is null || otp.AttemptCount >= MaxAttempts)
        {
            throw new InvalidResetCodeException();
        }

        if (otp.Code != request.Code)
        {
            otp.AttemptCount += 1;
            otpWriteRepo.Update(otp);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            throw new InvalidResetCodeException();
        }

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        _unitOfWork.GetWriteRepository<User>().Update(user);

        otp.IsUsed = true;
        otpWriteRepo.Update(otp);

        var refreshReadRepo = _unitOfWork.GetReadRepository<RefreshToken>();
        var refreshWriteRepo = _unitOfWork.GetWriteRepository<RefreshToken>();
        var activeTokens = await refreshReadRepo.GetAllAsync(t => t.UserId == user.Id && t.RevokedAt == null, cancellationToken: cancellationToken);
        foreach (var token in activeTokens)
        {
            token.RevokedAt = DateTime.UtcNow;
            refreshWriteRepo.Update(token);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
