using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Assignments.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Assignments.Commands.CreateAssignment;

public class CreateAssignmentCommandHandler : IRequestHandler<CreateAssignmentCommand, CreateAssignmentCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;

    public CreateAssignmentCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<CreateAssignmentCommandResult> Handle(CreateAssignmentCommand request, CancellationToken cancellationToken)
    {
        var userExists = await _unitOfWork.GetReadRepository<User>().AnyAsync(u => u.Id == request.UserId, cancellationToken);
        if (!userExists)
        {
            throw new AssignmentUserNotFoundException(request.UserId);
        }

        var alreadyAssigned = await _unitOfWork.GetReadRepository<Assignment>()
            .AnyAsync(a => a.UserId == request.UserId && a.CompanyId == request.CompanyId && a.IsActive, cancellationToken);
        if (alreadyAssigned)
        {
            throw new UserAlreadyAssignedException();
        }

        var assignment = new Assignment
        {
            UserId = request.UserId,
            CompanyId = request.CompanyId,
            BranchId = request.BranchId,
            Role = AssignmentRole.Member,
            IsActive = true,
        };

        await _unitOfWork.GetWriteRepository<Assignment>().AddAsync(assignment, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreateAssignmentCommandResult
        {
            Id = assignment.Id,
            UserId = assignment.UserId,
            CompanyId = assignment.CompanyId!.Value,
            BranchId = assignment.BranchId,
            Role = assignment.Role.ToString(),
        };
    }
}
