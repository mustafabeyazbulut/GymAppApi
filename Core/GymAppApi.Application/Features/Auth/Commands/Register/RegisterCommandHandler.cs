using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Exceptions;
using GymAppApi.Domain.Entities;
using MediatR;

namespace GymAppApi.Application.Features.Auth.Commands.Register;

public class RegisterCommandHandler : IRequestHandler<RegisterCommand, RegisterCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;

    public RegisterCommandHandler(IUnitOfWork unitOfWork, IPasswordHasher passwordHasher, IJwtTokenService jwtTokenService)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
    }

    public async Task<RegisterCommandResult> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        var userReadRepo = _unitOfWork.GetReadRepository<User>();

        if (await userReadRepo.AnyAsync(u => u.Phone == request.Phone, cancellationToken))
        {
            throw new PhoneAlreadyRegisteredException();
        }

        if (!string.IsNullOrWhiteSpace(request.Email) &&
            await userReadRepo.AnyAsync(u => u.Email == request.Email, cancellationToken))
        {
            throw new EmailAlreadyRegisteredException();
        }

        var user = new User
        {
            FullName = request.FullName,
            Phone = request.Phone,
            Email = request.Email,
            PasswordHash = _passwordHasher.Hash(request.Password),
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
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new RegisterCommandResult
        {
            AccessToken = access.Token,
            ExpiresAtUtc = access.ExpiresAtUtc,
            RefreshToken = rawRefreshToken,
        };
    }
}
