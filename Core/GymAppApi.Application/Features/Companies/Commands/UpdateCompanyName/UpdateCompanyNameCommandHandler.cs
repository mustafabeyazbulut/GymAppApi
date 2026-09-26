using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.Companies.Commands.UpdateCompanyName;

public class UpdateCompanyNameCommandHandler : IRequestHandler<UpdateCompanyNameCommand>
{
    private readonly IUnitOfWork _unitOfWork;

    public UpdateCompanyNameCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Handle(UpdateCompanyNameCommand request, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters: SuperAdminOnly uç nokta - Sistem Sahibi'nin tenant
        // bağlamı yok (senaryo §10.6, platform bypass'ı kaldırıldı); firma yönetimi
        // tüm firmaları açıkça görür.
        var company = await _unitOfWork.GetReadRepository<Company>()
            .GetAsync(c => c.Id == request.CompanyId, include: q => q.IgnoreQueryFilters().Include(c => c.Branches), cancellationToken: cancellationToken);
        if (company is null)
        {
            throw new NotFoundException("CompanyNotFound", request.CompanyId);
        }

        company.Name = request.Name;
        _unitOfWork.GetWriteRepository<Company>().Update(company);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
