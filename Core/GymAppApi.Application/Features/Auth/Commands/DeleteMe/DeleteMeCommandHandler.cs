using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
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
            throw new NotFoundException($"Kullanıcı {request.UserId} bulunamadı.");
        }

        // Cascade delete removes Assignments/RefreshTokens/OtpVerifications/
        // DeviceTokens (all configured OnDelete(DeleteBehavior.Cascade) on
        // their User FK) — a single Remove is sufficient.
        _unitOfWork.GetWriteRepository<User>().Remove(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
