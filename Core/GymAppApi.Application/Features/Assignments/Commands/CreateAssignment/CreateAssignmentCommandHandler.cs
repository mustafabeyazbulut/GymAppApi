using GymAppApi.Application.Common.Exceptions;
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
        // The [Authorize] policy only confirms the caller holds SOME
        // GymAdmin/SuperAdmin assignment — re-check it's scoped to THIS
        // company (SuperAdmin's own CompanyId is null/platform-wide, so it
        // bypasses the company match) before allowing the assignment.
        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive,
            cancellationToken: cancellationToken);
        var callerIsAuthorizedForThisCompany = callerAssignments.Any(a =>
            a.Role == AssignmentRole.SuperAdmin ||
            (a.Role == AssignmentRole.GymAdmin && a.CompanyId == request.CompanyId));
        if (!callerIsAuthorizedForThisCompany)
        {
            throw new ForbiddenException("Bu firma için atama yapma yetkiniz yok.");
        }

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
