using GymAppApi.Application.Common.ContactVerification;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Common;
using GymAppApi.Application.Features.Auth.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Auth.Commands.RegisterComplete;

public class RegisterCompleteCommandHandler : IRequestHandler<RegisterCompleteCommand, AuthTokenResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;

    public RegisterCompleteCommandHandler(IUnitOfWork unitOfWork, IPasswordHasher passwordHasher, IJwtTokenService jwtTokenService)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
    }

    public async Task<AuthTokenResult> Handle(RegisterCompleteCommand request, CancellationToken cancellationToken)
    {
        var pendingReadRepo = _unitOfWork.GetReadRepository<PendingContactVerification>();
        var pendingWriteRepo = _unitOfWork.GetWriteRepository<PendingContactVerification>();
        var hasEmail = !string.IsNullOrWhiteSpace(request.Email);
        var normalizedEmail = hasEmail ? request.Email!.Trim().ToLowerInvariant() : null;

        var phonePending = await pendingReadRepo.GetAsync(
            p => p.Channel == ContactChannel.Phone && p.Target == request.Phone, cancellationToken: cancellationToken);
        var phoneValid = PendingVerificationCodeService.TryConsumeAttempt(phonePending, request.PhoneCode, pendingWriteRepo);

        PendingContactVerification? emailPending = null;
        var emailValid = true;
        if (hasEmail)
        {
            emailPending = await pendingReadRepo.GetAsync(
                p => p.Channel == ContactChannel.Email && p.Target == normalizedEmail, cancellationToken: cancellationToken);
            emailValid = PendingVerificationCodeService.TryConsumeAttempt(emailPending, request.EmailCode!, pendingWriteRepo);
        }

        if (!phoneValid || !emailValid)
        {
            // No ambient transaction here - this save commits immediately and
            // independently, so the AttemptCount increment(s) above survive
            // the throw right after. See this plan's "manual transaction"
            // grounding fact for why this command is not ITransactionalRequest.
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            throw new InvalidContactVerificationCodeException(phoneFailed: !phoneValid, emailFailed: hasEmail && !emailValid);
        }

        var userReadRepo = _unitOfWork.GetReadRepository<User>();
        if (await userReadRepo.AnyAsync(u => u.Phone == request.Phone, cancellationToken))
        {
            throw new PhoneAlreadyRegisteredException();
        }
        if (hasEmail && await userReadRepo.AnyAsync(u => u.Email == request.Email, cancellationToken))
        {
            throw new EmailAlreadyRegisteredException();
        }

        // ExecuteWithRetryAsync icinden aciliyor: DbContext'in EnableRetryOnFailure
        // execution strategy'si, kullanici tarafindan baslatilan bir transaction'i
        // ancak begin/commit/rollback'in TAMAMI kendi ExecuteAsync delegate'inin
        // icindeyse yeniden deneyebiliyor - aksi halde EF Core calisma zamaninda
        // "does not support user-initiated transactions" firlatiyor (bkz.
        // TransactionBehavior.cs'in ayni gerekcesi - bu handler ITransactionalRequest
        // KULLANMIYOR, cunku yukaridaki OTP-basarisizligi save'i transaction DISINDA
        // kalmali, bu yuzden ayni sarmalama burada elle tekrarlaniyor).
        return await _unitOfWork.ExecuteWithRetryAsync(async () =>
        {
            await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);
            try
            {
                var user = new User
                {
                    FullName = request.FullName,
                    Phone = request.Phone,
                    Email = request.Email,
                    PasswordHash = _passwordHasher.Hash(request.Password),
                    PhoneVerified = true,
                    EmailVerified = hasEmail,
                };
                await _unitOfWork.GetWriteRepository<User>().AddAsync(user, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken); // need user.Id before issuing tokens

                var access = _jwtTokenService.GenerateAccessToken(new AccessTokenClaims(user.Id, user.FullName, user.Email, user.Phone));
                var rawRefreshToken = _jwtTokenService.GenerateRefreshTokenValue();

                await _unitOfWork.GetWriteRepository<RefreshToken>().AddAsync(new RefreshToken
                {
                    UserId = user.Id,
                    TokenHash = _passwordHasher.Hash(rawRefreshToken),
                    ExpiresAt = DateTime.UtcNow.AddDays(30),
                }, cancellationToken);

                pendingWriteRepo.Remove(phonePending!);
                if (emailPending is not null)
                {
                    pendingWriteRepo.Remove(emailPending);
                }

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                return new AuthTokenResult
                {
                    AccessToken = access.Token,
                    ExpiresAtUtc = access.ExpiresAtUtc,
                    RefreshToken = rawRefreshToken,
                };
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                throw;
            }
        });
    }
}
