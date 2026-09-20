using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Exceptions;
using GymAppApi.Domain.Entities;
using MediatR;

namespace GymAppApi.Application.Features.Auth.Commands.ChangePassword;

public class ChangePasswordCommandHandler : IRequestHandler<ChangePasswordCommand>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;

    public ChangePasswordCommandHandler(IUnitOfWork unitOfWork, IPasswordHasher passwordHasher)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
    }

    public async Task Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await _unitOfWork.GetReadRepository<User>()
            .GetAsync(u => u.Id == request.UserId, cancellationToken: cancellationToken);
        if (user is null)
        {
            throw new NotFoundException("UserNotFound", request.UserId);
        }

        if (!_passwordHasher.Verify(user.PasswordHash, request.CurrentPassword))
        {
            throw new InvalidCredentialsException();
        }

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        _unitOfWork.GetWriteRepository<User>().Update(user);

        // ResetPasswordCommandHandler ile aynı güvenlik önlemi: şifre
        // değiştiğinde bu hesabın diğer tüm oturumlarındaki refresh
        // token'lar da iptal edilir - aksi halde eski şifreyle açılmış bir
        // oturum (ör. çalınan bir cihaz) yeni şifreden habersiz şekilde
        // erişime devam edebilirdi.
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
