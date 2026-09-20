using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Exceptions;
using GymAppApi.Domain.Entities;
using MediatR;

namespace GymAppApi.Application.Features.Auth.Commands.UpdateProfile;

public class UpdateProfileCommandHandler : IRequestHandler<UpdateProfileCommand>
{
    private readonly IUnitOfWork _unitOfWork;

    public UpdateProfileCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Handle(UpdateProfileCommand request, CancellationToken cancellationToken)
    {
        var userReadRepo = _unitOfWork.GetReadRepository<User>();
        var user = await userReadRepo.GetAsync(u => u.Id == request.UserId, cancellationToken: cancellationToken);
        if (user is null)
        {
            throw new NotFoundException("UserNotFound", request.UserId);
        }

        var newEmail = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        var emailChanged = !string.Equals(user.Email, newEmail, StringComparison.OrdinalIgnoreCase);
        if (emailChanged && newEmail != null && await userReadRepo.AnyAsync(u => u.Id != user.Id && u.Email == newEmail, cancellationToken))
        {
            throw new EmailAlreadyRegisteredException();
        }

        user.FullName = request.FullName.Trim();
        if (emailChanged)
        {
            user.Email = newEmail;
            // Yeni e-posta henüz doğrulanmadı - kayıt sırasındaki
            // EmailVerified'ı burada koruyup yanlış bir güven vermemek için
            // sıfırlanıyor (bkz. User.cs'in EmailVerified alanı).
            user.EmailVerified = false;
        }

        _unitOfWork.GetWriteRepository<User>().Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
