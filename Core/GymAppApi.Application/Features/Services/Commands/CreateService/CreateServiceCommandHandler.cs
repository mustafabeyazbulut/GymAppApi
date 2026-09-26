using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.Services.Commands.CreateService;

public class CreateServiceCommandHandler : IRequestHandler<CreateServiceCommand, ServiceDto>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;

    public CreateServiceCommandHandler(IUnitOfWork unitOfWork, ITenantContext tenantContext)
    {
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
    }

    public async Task<ServiceDto> Handle(CreateServiceCommand request, CancellationToken cancellationToken)
    {
        // Başka firmanın şubesi "yok" sayılır (404).
        var branch = await _unitOfWork.GetReadRepository<Branch>().GetAsync(
            b => b.Id == request.BranchId,
            include: q => q.IgnoreQueryFilters().Include(b => b.Company),
            cancellationToken: cancellationToken);
        if (branch is null || branch.CompanyId != _tenantContext.CompanyId)
        {
            throw new NotFoundException("BranchNotFound", request.BranchId);
        }

        if (!ServiceRules.CanManage(_tenantContext, branch.CompanyId, branch.Id))
        {
            throw new ForbiddenException("ForbiddenManageService");
        }

        var name = request.Name.Trim();
        await ServiceRules.EnsureNameAvailableAsync(_unitOfWork, branch.Id, name, exceptServiceId: null, cancellationToken);

        var service = new Service { CompanyId = branch.CompanyId, BranchId = branch.Id, Name = name };
        await _unitOfWork.GetWriteRepository<Service>().AddAsync(service, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return ServiceDto.From(service);
    }
}
