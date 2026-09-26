using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Security;
using GymAppApi.Application.Features.Auth.Common;
using GymAppApi.Application.Features.Auth.Exceptions;
using GymAppApi.Domain.Entities;
using MediatR;

namespace GymAppApi.Application.Features.Auth.Commands.Login;

public class LoginCommandHandler : IRequestHandler<LoginCommand, AuthTokenResult>
{
    // Lazily computed once via the injected hasher (never a hand-typed
    // string — must be a real, correctly-formatted hash) and reused for
    // every "user not found" case, so that path takes comparable time to a
    // real Verify call. Without this, a null user short-circuits instantly
    // while a found-user-wrong-password path pays real PBKDF2 cost, letting
    // an attacker distinguish "no such account" from "wrong password" by
    // timing alone — exactly the enumeration leak login must not have.
    private static string? _dummyHash;
    private static readonly object DummyHashLock = new();

    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IPhoneNumberNormalizer _phoneNumberNormalizer;
    private readonly ILoginAttemptStore _loginAttemptStore;
    private readonly IClientIpHashProvider _clientIpHashProvider;

    public LoginCommandHandler(
        IUnitOfWork unitOfWork, IPasswordHasher passwordHasher, IJwtTokenService jwtTokenService,
        IPhoneNumberNormalizer phoneNumberNormalizer, ILoginAttemptStore loginAttemptStore, IClientIpHashProvider clientIpHashProvider)
    {
        _loginAttemptStore = loginAttemptStore;
        _clientIpHashProvider = clientIpHashProvider;
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _phoneNumberNormalizer = phoneNumberNormalizer;
    }

    public async Task<AuthTokenResult> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        // Lets a user type their phone in any common local format
        // ("05551234567", "5551234567", "+905551234567") regardless of how
        // it was originally stored - normalizing the lookup value, not the
        // stored one, means this works uniformly for every account.
        var identifier = _phoneNumberNormalizer.NormalizeIfPhone(request.Identifier);
        var user = await _unitOfWork.GetReadRepository<User>()
            .GetAsync(u => u.Phone == identifier || u.Email == identifier, cancellationToken: cancellationToken);

        // Doğrulama HER durumda yapılır (kullanıcı yok, kilitli, yanlış şifre) -
        // yollar yanıt süresiyle ayırt edilemesin.
        var hashToVerify = user?.PasswordHash ?? GetDummyHash();
        var passwordMatches = _passwordHasher.Verify(hashToVerify, request.Password);

        if (user is null)
        {
            throw new InvalidCredentialsException();
        }

        // Hesap+IP bazlı kilit (LoginLockoutPolicy): kilitliyken doğru şifre de
        // reddedilir ve yanıt "kullanıcı yok / yanlış şifre" ile birebir aynıdır
        // (InvalidCredentials) - hesabın varlığı veya kilit durumu sızdırılmaz.
        // Login User satırına hiç yazmaz: sayaç ayrı tabloda atomik tutulur, böylece
        // eşzamanlı ResetPassword'ün yeni hash'i bayat bir kopyayla ezilemez.
        var now = DateTime.UtcNow;
        var ipHash = _clientIpHashProvider.GetHashedClientIp();
        if (await _loginAttemptStore.IsLockedAsync(user.Id, ipHash, now, cancellationToken))
        {
            throw new InvalidCredentialsException();
        }

        if (!passwordMatches)
        {
            await _loginAttemptStore.RecordFailureAsync(user.Id, ipHash, now, cancellationToken);
            throw new InvalidCredentialsException();
        }

        await _loginAttemptStore.ResetAsync(user.Id, ipHash, cancellationToken);

        var access = _jwtTokenService.GenerateAccessToken(new AccessTokenClaims(user.Id, user.FullName, user.Email, user.Phone));
        var rawRefreshToken = _jwtTokenService.GenerateRefreshTokenValue();

        await _unitOfWork.GetWriteRepository<RefreshToken>().AddAsync(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = _passwordHasher.Hash(rawRefreshToken),
            ExpiresAt = DateTime.UtcNow.AddDays(30),
        }, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AuthTokenResult
        {
            AccessToken = access.Token,
            ExpiresAtUtc = access.ExpiresAtUtc,
            RefreshToken = rawRefreshToken,
        };
    }

    private string GetDummyHash()
    {
        if (_dummyHash is null)
        {
            lock (DummyHashLock)
            {
                _dummyHash ??= _passwordHasher.Hash("dummy-password-for-constant-time-verification");
            }
        }
        return _dummyHash;
    }
}
