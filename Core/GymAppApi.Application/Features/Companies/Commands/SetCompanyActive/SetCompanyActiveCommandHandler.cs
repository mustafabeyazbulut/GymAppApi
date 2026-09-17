using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using MediatR;

namespace GymAppApi.Application.Features.Companies.Commands.SetCompanyActive;

public class SetCompanyActiveCommandHandler : IRequestHandler<SetCompanyActiveCommand>
{
    private readonly IUnitOfWork _unitOfWork;

    public SetCompanyActiveCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Handle(SetCompanyActiveCommand request, CancellationToken cancellationToken)
    {
        var company = await _unitOfWork.GetReadRepository<Company>()
            .GetAsync(c => c.Id == request.CompanyId, cancellationToken: cancellationToken);
        if (company is null)
        {
            throw new NotFoundException($"Firma {request.CompanyId} bulunamadı.");
        }

        company.IsActive = request.IsActive;
        _unitOfWork.GetWriteRepository<Company>().Update(company);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
