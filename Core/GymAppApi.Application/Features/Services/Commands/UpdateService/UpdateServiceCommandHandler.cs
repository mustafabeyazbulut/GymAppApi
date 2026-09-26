using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using MediatR;

namespace GymAppApi.Application.Features.Services.Commands.UpdateService;

public class UpdateServiceCommandHandler : IRequestHandler<UpdateServiceCommand, ServiceDto>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;

    public UpdateServiceCommandHandler(IUnitOfWork unitOfWork, ITenantContext tenantContext)
    {
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
    }

    public async Task<ServiceDto> Handle(UpdateServiceCommand request, CancellationToken cancellationToken)
    {
        var service = await ServiceRules.LoadManageableAsync(_unitOfWork, _tenantContext, request.ServiceId, cancellationToken);

        var name = request.Name.Trim();
        await ServiceRules.EnsureNameAvailableAsync(_unitOfWork, service.BranchId, name, exceptServiceId: service.Id, cancellationToken);

        service.Name = name;
        _unitOfWork.GetWriteRepository<Service>().Update(service);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return ServiceDto.From(service);
    }
}
