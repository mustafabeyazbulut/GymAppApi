using GymAppApi.Application.Common.Assignments;
using GymAppApi.Application.Common.ContactVerification;
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
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
            throw new NotFoundException("UserNotFound", request.UserId);
        }

        // Kod tüketilmeden/gönderilmeden önce: bir firmanın son Gym Admin'i hesabını donduramaz.
        await LastGymAdminGuard.EnsureNotLastGymAdminAnywhereAsync(_unitOfWork, user.Id, cancellationToken);

        var pendingReadRepo = _unitOfWork.GetReadRepository<PendingContactVerification>();
        var pendingWriteRepo = _unitOfWork.GetWriteRepository<PendingContactVerification>();
        var pending = await pendingReadRepo.GetAsync(
            p => p.Channel == ContactChannel.Phone && p.Target == user.Phone, cancellationToken: cancellationToken);
        var codeValid = PendingVerificationCodeService.TryConsumeAttempt(pending, request.Code, pendingWriteRepo);
        if (!codeValid)
        {
            // No ambient transaction here - this save commits immediately so
            // the AttemptCount increment above survives the throw right after.
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            throw new InvalidContactVerificationCodeException(phoneFailed: true, emailFailed: false);
        }

        // Son Gym Admin kontrolü dondurmayla aynı transaction'da, firma satırı
        // kilitliyken tekrarlanır (bkz. LastGymAdminGuard.RunSerializedAsync).
        await LastGymAdminGuard.RunSerializedAsync(_unitOfWork, user.Id, async () =>
        {
            user.IsAccountFrozen = true;
            _unitOfWork.GetWriteRepository<User>().Update(user);
            pendingWriteRepo.Remove(pending!);

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
        }, cancellationToken);
    }
}
