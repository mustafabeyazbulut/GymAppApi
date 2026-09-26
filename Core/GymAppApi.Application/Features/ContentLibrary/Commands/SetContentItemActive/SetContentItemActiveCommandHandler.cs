using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.ContentLibrary.Commands.SetContentItemActive;

public class SetContentItemActiveCommandHandler : IRequestHandler<SetContentItemActiveCommand>
{
    private readonly IUnitOfWork _unitOfWork;

    public SetContentItemActiveCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Handle(SetContentItemActiveCommand request, CancellationToken cancellationToken)
    {
        var contentItem = await _unitOfWork.GetReadRepository<ContentItem>()
            .GetAsync(c => c.Id == request.ContentItemId, cancellationToken: cancellationToken);
        if (contentItem is null)
        {
            throw new NotFoundException("ContentItemNotFound", request.ContentItemId);
        }

        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);
        var callerIsAuthorized = callerAssignments.Any(a =>
            (a.Role == AssignmentRole.GymAdmin && a.CompanyId == contentItem.CompanyId) ||
            (a.Role == AssignmentRole.BranchManager && a.BranchId == contentItem.BranchId));
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("ForbiddenSetContentItemActive");
        }

        contentItem.IsActive = request.IsActive;
        _unitOfWork.GetWriteRepository<ContentItem>().Update(contentItem);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
