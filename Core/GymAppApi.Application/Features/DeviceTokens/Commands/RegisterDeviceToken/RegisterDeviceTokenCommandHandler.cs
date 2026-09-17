using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using MediatR;

namespace GymAppApi.Application.Features.DeviceTokens.Commands.RegisterDeviceToken;

public class RegisterDeviceTokenCommandHandler : IRequestHandler<RegisterDeviceTokenCommand>
{
    private readonly IUnitOfWork _unitOfWork;

    public RegisterDeviceTokenCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Handle(RegisterDeviceTokenCommand request, CancellationToken cancellationToken)
    {
        var writeRepo = _unitOfWork.GetWriteRepository<DeviceToken>();
        var existing = await _unitOfWork.GetReadRepository<DeviceToken>()
            .GetAsync(d => d.Token == request.Token, cancellationToken: cancellationToken);

        if (existing is null)
        {
            await writeRepo.AddAsync(new DeviceToken
            {
                UserId = request.UserId,
                Token = request.Token,
                Platform = request.Platform,
            }, cancellationToken);
        }
        else
        {
            // Re-registering the same physical device under a different
            // account (logout, then a different user logs in) reassigns it
            // rather than colliding with DeviceTokenConfiguration's unique
            // index on Token.
            existing.UserId = request.UserId;
            existing.Platform = request.Platform;
            writeRepo.Update(existing);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
