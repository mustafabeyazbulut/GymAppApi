using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Branches.Rules;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
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

        // Same pattern as CreateAssignmentCommandHandler: the [Authorize]
        // policy only proves the caller holds SOME GymAdmin/SuperAdmin
        // assignment somewhere - re-check it's scoped to THIS company. A new
        // company's GymAdmin creates their own first branch this way, once
        // they've confirmed their invitation - SuperAdmin no longer creates
        // it on their behalf (see CreateCompanyCommand).
        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);
        var callerIsAuthorized = callerAssignments.Any(a =>
            a.Role == AssignmentRole.SuperAdmin ||
            (a.Role == AssignmentRole.GymAdmin && a.CompanyId == request.CompanyId));
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("ForbiddenCreateBranch");
        }

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
