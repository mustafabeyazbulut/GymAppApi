using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using MediatR;

namespace GymAppApi.Application.Features.PersonalLogs.Commands.CreatePersonalLog;

public class CreatePersonalLogCommandHandler : IRequestHandler<CreatePersonalLogCommand, PersonalLogDto>
{
    private readonly IUnitOfWork _unitOfWork;

    public CreatePersonalLogCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<PersonalLogDto> Handle(CreatePersonalLogCommand request, CancellationToken cancellationToken)
    {
        var log = new PersonalLog { UserId = request.UserId };
        request.ApplyTo(log);
        await _unitOfWork.GetWriteRepository<PersonalLog>().AddAsync(log, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return PersonalLogDto.From(log);
    }
}
