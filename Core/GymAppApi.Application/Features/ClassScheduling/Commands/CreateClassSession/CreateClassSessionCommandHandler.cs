using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Security;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.ClassScheduling.Commands.CreateClassSession;

public class CreateClassSessionCommandHandler : IRequestHandler<CreateClassSessionCommand, CreateClassSessionCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;

    public CreateClassSessionCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<CreateClassSessionCommandResult> Handle(CreateClassSessionCommand request, CancellationToken cancellationToken)
    {
        var branch = await _unitOfWork.GetReadRepository<Branch>()
            .GetAsync(b => b.Id == request.BranchId, cancellationToken: cancellationToken);
        if (branch is null)
        {
            throw new NotFoundException("BranchNotFound", request.BranchId);
        }

        // CreatePackageCommandHandler'ın branch-özel (BranchId != null) dalıyla
        // aynı yetki deseni: GymAdmin (firma geneli) veya o şubenin BranchManager'ı.
        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);
        var callerIsAuthorized = callerAssignments.Any(a =>
            (a.Role == AssignmentRole.GymAdmin && a.CompanyId == branch.CompanyId) ||
            (a.Role == AssignmentRole.BranchManager && a.BranchId == request.BranchId));
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("ForbiddenCreateClassSession");
        }

        await CompanyStatusGuard.EnsureActiveAsync(_unitOfWork, branch.CompanyId, cancellationToken);

        var classSession = new ClassSession
        {
            CompanyId = branch.CompanyId,
            BranchId = request.BranchId,
            TrainerUserId = request.TrainerUserId,
            Category = request.Category,
            Name = request.Name,
            Date = request.Date,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            Capacity = request.Capacity,
            CancellationCutoffHours = request.CancellationCutoffHours,
            CreatedByUserId = request.RequestedByUserId,
        };

        await _unitOfWork.GetWriteRepository<ClassSession>().AddAsync(classSession, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreateClassSessionCommandResult { Id = classSession.Id, Name = classSession.Name };
    }
}
