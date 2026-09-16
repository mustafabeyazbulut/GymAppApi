using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
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
            throw new NotFoundException($"Kullanıcı {request.UserId} bulunamadı.");
        }

        user.IsAccountFrozen = false;
        _unitOfWork.GetWriteRepository<User>().Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
