using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.PackageAssignments;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.Services.Queries.GetBranchServices;

// Okuma: o şubenin personeli (GymAdmin firma geneli, BranchManager/Trainer
// şubenin) ve o şubede geçerli paketi olan üye. Üyenin ambient firması
// olmadığı için okumalar filtresiz + açık kapsamla (standing rule).
public class GetBranchServicesQueryHandler : IRequestHandler<GetBranchServicesQuery, IReadOnlyList<ServiceDto>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetBranchServicesQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<IReadOnlyList<ServiceDto>> Handle(GetBranchServicesQuery request, CancellationToken cancellationToken)
    {
        var branch = await _unitOfWork.GetReadRepository<Branch>().GetAsync(
            b => b.Id == request.BranchId,
            include: q => q.IgnoreQueryFilters().Include(b => b.Company),
            cancellationToken: cancellationToken);
        if (branch is null)
        {
            throw new NotFoundException("BranchNotFound", request.BranchId);
        }

        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive,
            include: q => q.IgnoreQueryFilters().Include(a => a.Company),
            cancellationToken: cancellationToken);
        var isManager = callerAssignments.Any(a =>
            (a.Role == AssignmentRole.GymAdmin && a.CompanyId == branch.CompanyId) ||
            (a.Role == AssignmentRole.BranchManager && a.BranchId == branch.Id));
        var isStaff = isManager || callerAssignments.Any(a => a.Role == AssignmentRole.Trainer && a.BranchId == branch.Id);

        if (!isStaff)
        {
            var validPackages = await _unitOfWork.GetReadRepository<PackageAssignment>().GetAllAsync(
                PackageAssignmentValidity.UsableOwnedBy(request.RequestedByUserId, DateTime.UtcNow),
                include: q => q.IgnoreQueryFilters().Include(pa => pa.Package),
                cancellationToken: cancellationToken);
            var hasPackageHere = validPackages.Any(pa =>
                pa.BranchId == branch.Id || (pa.BranchId == null && pa.CompanyId == branch.CompanyId));
            if (!hasPackageHere)
            {
                throw new ForbiddenException("ForbiddenViewServices");
            }
        }

        var includeInactive = request.IncludeInactive && isManager;
        var services = await _unitOfWork.GetReadRepository<Service>().GetAllAsync(
            s => s.BranchId == branch.Id && (includeInactive || s.IsActive),
            include: q => q.IgnoreQueryFilters().Include(s => s.Branch),
            orderBy: q => q.OrderBy(s => s.Name),
            cancellationToken: cancellationToken);
        return services.Select(ServiceDto.From).ToList();
    }
}
