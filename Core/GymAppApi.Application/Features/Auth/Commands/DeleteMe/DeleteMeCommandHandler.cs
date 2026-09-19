using GymAppApi.Application.Common.ContactVerification;
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Auth.Commands.DeleteMe;

public class DeleteMeCommandHandler : IRequestHandler<DeleteMeCommand>
{
    private readonly IUnitOfWork _unitOfWork;

    public DeleteMeCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Handle(DeleteMeCommand request, CancellationToken cancellationToken)
    {
        var user = await _unitOfWork.GetReadRepository<User>().GetAsync(u => u.Id == request.UserId, cancellationToken: cancellationToken);
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

        // Cascade delete removes Assignments/RefreshTokens/OtpVerifications/
        // DeviceTokens (all configured OnDelete(DeleteBehavior.Cascade) on
        // their User FK) — a single Remove is sufficient. The pending
        // verification row isn't FK-linked to User (it exists independently
        // of any account, see PendingContactVerification's own comment), so
        // it's removed explicitly here.
        _unitOfWork.GetWriteRepository<User>().Remove(user);
        pendingWriteRepo.Remove(pending!);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
