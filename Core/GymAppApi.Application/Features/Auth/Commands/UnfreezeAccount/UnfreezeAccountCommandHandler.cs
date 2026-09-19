using GymAppApi.Application.Common.ContactVerification;
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Auth.Commands.UnfreezeAccount;

public class UnfreezeAccountCommandHandler : IRequestHandler<UnfreezeAccountCommand>
{
    private readonly IUnitOfWork _unitOfWork;

    public UnfreezeAccountCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Handle(UnfreezeAccountCommand request, CancellationToken cancellationToken)
    {
        var user = await _unitOfWork.GetReadRepository<User>()
            .GetAsync(u => u.Id == request.UserId, cancellationToken: cancellationToken);
        if (user is null)
        {
            throw new NotFoundException("UserNotFound", request.UserId);
        }

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

        user.IsAccountFrozen = false;
        _unitOfWork.GetWriteRepository<User>().Update(user);
        pendingWriteRepo.Remove(pending!);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
