using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using MediatR;

namespace GymAppApi.Application.Features.Auth.Commands.FreezeAccount;

public class FreezeAccountCommandHandler : IRequestHandler<FreezeAccountCommand>
{
    private readonly IUnitOfWork _unitOfWork;

    public FreezeAccountCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Handle(FreezeAccountCommand request, CancellationToken cancellationToken)
    {
        var user = await _unitOfWork.GetReadRepository<User>()
            .GetAsync(u => u.Id == request.UserId, cancellationToken: cancellationToken);
        if (user is null)
        {
            throw new NotFoundException($"Kullanıcı {request.UserId} bulunamadı.");
        }

        user.IsAccountFrozen = true;
        _unitOfWork.GetWriteRepository<User>().Update(user);

        // Freezing takes effect everywhere immediately, not just for future
        // requests - revoke every currently-active session, matching
        // ResetPasswordCommandHandler's precedent for security-relevant
        // account changes.
        var refreshReadRepo = _unitOfWork.GetReadRepository<RefreshToken>();
        var refreshWriteRepo = _unitOfWork.GetWriteRepository<RefreshToken>();
        var activeTokens = await refreshReadRepo.GetAllAsync(
            t => t.UserId == user.Id && t.RevokedAt == null, cancellationToken: cancellationToken);
        foreach (var token in activeTokens)
        {
            token.RevokedAt = DateTime.UtcNow;
            refreshWriteRepo.Update(token);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
