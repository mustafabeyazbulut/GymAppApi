using GymAppApi.Application.Common.Interfaces;
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

    public LoginCommandHandler(IUnitOfWork unitOfWork, IPasswordHasher passwordHasher, IJwtTokenService jwtTokenService)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
    }

    public async Task<AuthTokenResult> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var user = await _unitOfWork.GetReadRepository<User>()
            .GetAsync(u => u.Phone == request.Identifier || u.Email == request.Identifier, cancellationToken: cancellationToken);

        var hashToVerify = user?.PasswordHash ?? GetDummyHash();
        var passwordMatches = _passwordHasher.Verify(hashToVerify, request.Password);

        if (user is null || !passwordMatches)
        {
            throw new InvalidCredentialsException();
        }

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
