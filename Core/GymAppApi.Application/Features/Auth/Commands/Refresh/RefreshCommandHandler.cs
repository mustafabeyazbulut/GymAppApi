using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Commands.Register;
using GymAppApi.Application.Features.Auth.Exceptions;
using GymAppApi.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.Auth.Commands.Refresh;

public class RefreshCommandHandler : IRequestHandler<RefreshCommand, RegisterCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;

    public RefreshCommandHandler(IUnitOfWork unitOfWork, IPasswordHasher passwordHasher, IJwtTokenService jwtTokenService)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
    }

    public async Task<RegisterCommandResult> Handle(RefreshCommand request, CancellationToken cancellationToken)
    {
        var tokenReadRepo = _unitOfWork.GetReadRepository<RefreshToken>();
        var tokenWriteRepo = _unitOfWork.GetWriteRepository<RefreshToken>();

        // Only filters on ExpiresAt, NOT RevokedAt — a revoked-but-not-expired
        // token must still be found here so the reuse-detection branch below
        // can catch it. Excluding revoked tokens from this query would make
        // theft detection impossible: a stolen, already-rotated token would
        // simply look "not found" instead of "reused".
        var candidates = await tokenReadRepo.GetAllAsync(t => t.ExpiresAt > DateTime.UtcNow, cancellationToken: cancellationToken);
        var matched = candidates.FirstOrDefault(t => _passwordHasher.Verify(t.TokenHash, request.RefreshToken));

        if (matched is null)
        {
            throw new InvalidRefreshTokenException();
        }

        if (matched.RevokedAt is not null)
        {
            // Reuse of an already-rotated token = likely theft. Revoke every
            // token this user currently has (including ones already revoked
            // — re-stamping RevokedAt on those is harmless) so a stolen
            // chain is fully cut, with no gap for a sibling token to survive.
            var allUserTokens = await tokenReadRepo.GetAllAsync(t => t.UserId == matched.UserId, cancellationToken: cancellationToken);
            foreach (var t in allUserTokens)
            {
                t.RevokedAt = DateTime.UtcNow;
                tokenWriteRepo.Update(t);
            }
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            throw new InvalidRefreshTokenException();
        }

        var user = await _unitOfWork.GetReadRepository<User>().GetAsync(u => u.Id == matched.UserId, cancellationToken: cancellationToken);
        if (user is null)
        {
            throw new InvalidRefreshTokenException();
        }

        var access = _jwtTokenService.GenerateAccessToken(new AccessTokenClaims(user.Id, user.FullName, user.Email, user.Phone));
        var newRawRefreshToken = _jwtTokenService.GenerateRefreshTokenValue();
        var newTokenHash = _passwordHasher.Hash(newRawRefreshToken);

        matched.RevokedAt = DateTime.UtcNow;
        matched.ReplacedByTokenHash = newTokenHash;
        tokenWriteRepo.Update(matched);

        await tokenWriteRepo.AddAsync(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = newTokenHash,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
        }, cancellationToken);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another concurrent request rotated this exact token first
            // (xmin mismatch) — treat the loser the same as an invalid
            // token rather than silently letting both requests "succeed"
            // and issue two live children from one now-stale parent.
            throw new InvalidRefreshTokenException();
        }

        return new RegisterCommandResult
        {
            AccessToken = access.Token,
            ExpiresAtUtc = access.ExpiresAtUtc,
            RefreshToken = newRawRefreshToken,
        };
    }
}
