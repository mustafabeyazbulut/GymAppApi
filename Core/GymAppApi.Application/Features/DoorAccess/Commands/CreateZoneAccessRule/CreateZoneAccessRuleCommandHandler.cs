using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.DoorAccess.Commands.CreateZoneAccessRule;

public class CreateZoneAccessRuleCommandHandler : IRequestHandler<CreateZoneAccessRuleCommand, CreateZoneAccessRuleCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;

    public CreateZoneAccessRuleCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<CreateZoneAccessRuleCommandResult> Handle(CreateZoneAccessRuleCommand request, CancellationToken cancellationToken)
    {
        var zone = await _unitOfWork.GetReadRepository<Zone>()
            .GetAsync(z => z.Id == request.ZoneId, cancellationToken: cancellationToken);
        if (zone is null)
        {
            throw new NotFoundException("ZoneNotFound", request.ZoneId);
        }

        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);
        var callerIsAuthorized = callerAssignments.Any(a =>
            a.Role == AssignmentRole.SuperAdmin ||
            (a.Role == AssignmentRole.GymAdmin && a.CompanyId == zone.CompanyId) ||
            (a.Role == AssignmentRole.BranchManager && a.BranchId == zone.BranchId));
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("ForbiddenManageDoorAccess");
        }

        var rule = new ZoneAccessRule
        {
            ZoneId = zone.Id,
            CompanyId = zone.CompanyId,
            RuleType = request.RuleType,
            RuleValue = request.RuleValue,
        };
        await _unitOfWork.GetWriteRepository<ZoneAccessRule>().AddAsync(rule, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreateZoneAccessRuleCommandResult { Id = rule.Id };
    }
}
