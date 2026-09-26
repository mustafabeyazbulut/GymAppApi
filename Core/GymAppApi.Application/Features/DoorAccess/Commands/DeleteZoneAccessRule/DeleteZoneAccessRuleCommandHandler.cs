using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.DoorAccess.Commands.DeleteZoneAccessRule;

public class DeleteZoneAccessRuleCommandHandler : IRequestHandler<DeleteZoneAccessRuleCommand>
{
    private readonly IUnitOfWork _unitOfWork;

    public DeleteZoneAccessRuleCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Handle(DeleteZoneAccessRuleCommand request, CancellationToken cancellationToken)
    {
        var rule = await _unitOfWork.GetReadRepository<ZoneAccessRule>().GetAsync(
            r => r.Id == request.ZoneAccessRuleId,
            include: q => q.Include(r => r.Zone),
            cancellationToken: cancellationToken);
        if (rule is null)
        {
            throw new NotFoundException("ZoneAccessRuleNotFound", request.ZoneAccessRuleId);
        }

        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);
        var callerIsAuthorized = callerAssignments.Any(a =>
            (a.Role == AssignmentRole.GymAdmin && a.CompanyId == rule.CompanyId));
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("ForbiddenManageDoorAccess");
        }

        _unitOfWork.GetWriteRepository<ZoneAccessRule>().Remove(rule);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
