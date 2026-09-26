using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using MediatR;

namespace GymAppApi.Application.Features.PersonalLogs.Commands.UpdatePersonalLog;

public class UpdatePersonalLogCommandHandler : IRequestHandler<UpdatePersonalLogCommand, PersonalLogDto>
{
    private readonly IUnitOfWork _unitOfWork;

    public UpdatePersonalLogCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<PersonalLogDto> Handle(UpdatePersonalLogCommand request, CancellationToken cancellationToken)
    {
        // Başkasının kaydı "yok" gibi davranır (404) - varlığı sızmaz.
        var log = await _unitOfWork.GetReadRepository<PersonalLog>().GetAsync(
            l => l.Id == request.Id && l.UserId == request.UserId, enableTracking: true, cancellationToken: cancellationToken);
        if (log is null)
        {
            throw new NotFoundException("PersonalLogNotFound", request.Id);
        }

        request.ApplyTo(log);
        _unitOfWork.GetWriteRepository<PersonalLog>().Update(log);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return PersonalLogDto.From(log);
    }
}
