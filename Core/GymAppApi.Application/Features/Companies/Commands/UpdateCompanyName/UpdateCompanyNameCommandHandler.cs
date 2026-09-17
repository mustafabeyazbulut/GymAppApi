using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using MediatR;

namespace GymAppApi.Application.Features.Companies.Commands.UpdateCompanyName;

public class UpdateCompanyNameCommandHandler : IRequestHandler<UpdateCompanyNameCommand>
{
    private readonly IUnitOfWork _unitOfWork;

    public UpdateCompanyNameCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Handle(UpdateCompanyNameCommand request, CancellationToken cancellationToken)
    {
        var company = await _unitOfWork.GetReadRepository<Company>()
            .GetAsync(c => c.Id == request.CompanyId, cancellationToken: cancellationToken);
        if (company is null)
        {
            throw new NotFoundException($"Firma {request.CompanyId} bulunamadı.");
        }

        company.Name = request.Name;
        _unitOfWork.GetWriteRepository<Company>().Update(company);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
