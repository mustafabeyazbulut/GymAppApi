using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Commands.Register;
using GymAppApi.Application.Features.Auth.Exceptions;
using GymAppApi.Domain.Entities;
using MediatR;

namespace GymAppApi.Application.Features.Auth.Commands.Login;

public class LoginCommandHandler : IRequestHandler<LoginCommand, RegisterCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;

    public LoginCommandHandler(IUnitOfWork unitOfWork, IPasswordHasher passwordHasher, IJwtTokenService jwtTokenService)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
    }

    public async Task<RegisterCommandResult> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var user = await _unitOfWork.GetReadRepository<User>()
            .GetAsync(u => u.Phone == request.Identifier || u.Email == request.Identifier, cancellationToken: cancellationToken);

        if (user is null || !_passwordHasher.Verify(user.PasswordHash, request.Password))
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

        return new RegisterCommandResult
        {
            AccessToken = access.Token,
            ExpiresAtUtc = access.ExpiresAtUtc,
            RefreshToken = rawRefreshToken,
        };
    }
}
