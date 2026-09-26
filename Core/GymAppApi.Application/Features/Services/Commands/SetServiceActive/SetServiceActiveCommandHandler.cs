using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using MediatR;

namespace GymAppApi.Application.Features.Services.Commands.SetServiceActive;

public class SetServiceActiveCommandHandler : IRequestHandler<SetServiceActiveCommand>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;

    public SetServiceActiveCommandHandler(IUnitOfWork unitOfWork, ITenantContext tenantContext)
    {
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
    }

    public async Task Handle(SetServiceActiveCommand request, CancellationToken cancellationToken)
    {
        var service = await ServiceRules.LoadManageableAsync(_unitOfWork, _tenantContext, request.ServiceId, cancellationToken);

        service.IsActive = request.IsActive;
        _unitOfWork.GetWriteRepository<Service>().Update(service);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
