using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Branches.Exceptions;
using GymAppApi.Domain.Entities;

namespace GymAppApi.Application.Features.Branches.Rules;

public class BranchRules
{
    private readonly IUnitOfWork _unitOfWork;

    public BranchRules(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task CompanyMustExistAsync(int companyId, CancellationToken cancellationToken)
    {
        var exists = await _unitOfWork.GetReadRepository<Company>().AnyAsync(c => c.Id == companyId, cancellationToken);
        if (!exists)
        {
            throw new CompanyNotFoundException(companyId);
        }
    }
}
