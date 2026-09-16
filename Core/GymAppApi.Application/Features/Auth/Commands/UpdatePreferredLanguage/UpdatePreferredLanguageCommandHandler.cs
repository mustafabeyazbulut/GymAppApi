using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using MediatR;

namespace GymAppApi.Application.Features.Auth.Commands.UpdatePreferredLanguage;

public class UpdatePreferredLanguageCommandHandler : IRequestHandler<UpdatePreferredLanguageCommand>
{
    private readonly IUnitOfWork _unitOfWork;

    public UpdatePreferredLanguageCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Handle(UpdatePreferredLanguageCommand request, CancellationToken cancellationToken)
    {
        var user = await _unitOfWork.GetReadRepository<User>()
            .GetAsync(u => u.Id == request.UserId, cancellationToken: cancellationToken);
        if (user is null)
        {
            throw new NotFoundException($"Kullanıcı {request.UserId} bulunamadı.");
        }

        user.PreferredLanguage = request.Language;
        _unitOfWork.GetWriteRepository<User>().Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
