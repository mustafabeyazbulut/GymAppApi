using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using MediatR;

namespace GymAppApi.Application.Features.PersonalLogs.Commands.DeletePersonalLog;

// Kişisel kayıt kullanıcının kendi verisi - soft delete yerine gerçek silme.
public class DeletePersonalLogCommandHandler : IRequestHandler<DeletePersonalLogCommand>
{
    private readonly IUnitOfWork _unitOfWork;

    public DeletePersonalLogCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Handle(DeletePersonalLogCommand request, CancellationToken cancellationToken)
    {
        var log = await _unitOfWork.GetReadRepository<PersonalLog>().GetAsync(
            l => l.Id == request.Id && l.UserId == request.UserId, enableTracking: true, cancellationToken: cancellationToken);
        if (log is null)
        {
            throw new NotFoundException("PersonalLogNotFound", request.Id);
        }

        _unitOfWork.GetWriteRepository<PersonalLog>().Remove(log);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
