using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Branches.Rules;
using GymAppApi.Domain.Entities;
using MediatR;

namespace GymAppApi.Application.Features.Branches.Commands.CreateBranch;

public class CreateBranchCommandHandler : IRequestHandler<CreateBranchCommand, CreateBranchCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly BranchRules _branchRules;

    public CreateBranchCommandHandler(IUnitOfWork unitOfWork, BranchRules branchRules)
    {
        _unitOfWork = unitOfWork;
        _branchRules = branchRules;
    }

    public async Task<CreateBranchCommandResult> Handle(CreateBranchCommand request, CancellationToken cancellationToken)
    {
        await _branchRules.CompanyMustExistAsync(request.CompanyId, cancellationToken);

        var branch = new Branch
        {
            CompanyId = request.CompanyId,
            Name = request.Name,
            Address = request.Address
        };

        await _unitOfWork.GetWriteRepository<Branch>().AddAsync(branch, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreateBranchCommandResult { Id = branch.Id, Name = branch.Name };
    }
}
