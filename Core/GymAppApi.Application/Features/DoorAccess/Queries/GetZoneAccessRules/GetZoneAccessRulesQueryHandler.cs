using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.DoorAccess.Queries.GetZoneAccessRules;

public class GetZoneAccessRulesQueryHandler : IRequestHandler<GetZoneAccessRulesQuery, IReadOnlyList<ZoneAccessRuleDto>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetZoneAccessRulesQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<IReadOnlyList<ZoneAccessRuleDto>> Handle(GetZoneAccessRulesQuery request, CancellationToken cancellationToken)
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
            (a.Role == AssignmentRole.GymAdmin && a.CompanyId == zone.CompanyId) ||
            (a.Role == AssignmentRole.BranchManager && a.BranchId == zone.BranchId));
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("ForbiddenManageDoorAccess");
        }

        var rules = await _unitOfWork.GetReadRepository<ZoneAccessRule>().GetAllAsync(
            r => r.ZoneId == request.ZoneId, cancellationToken: cancellationToken);

        return rules.Select(r => new ZoneAccessRuleDto
        {
            Id = r.Id,
            ZoneId = r.ZoneId,
            RuleType = r.RuleType.ToString(),
            RuleValue = r.RuleValue,
        }).ToList();
    }
}
