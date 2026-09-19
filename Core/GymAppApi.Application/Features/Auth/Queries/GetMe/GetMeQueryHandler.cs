using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.Auth.Queries.GetMe;

public class GetMeQueryHandler : IRequestHandler<GetMeQuery, MeResultDto>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetMeQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<MeResultDto> Handle(GetMeQuery request, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters: this query is already pinned to one specific user
        // (u.Id == request.UserId), so bypassing the tenant filter here can't
        // leak another tenant's data - but it's necessary, because Assignment/
        // PackageAssignment/Company/Package are all company-scoped, and THIS
        // caller's own ambient tenant context is resolved FROM their own
        // Assignments (TenantResolutionService) - a plain Member has none, so
        // their ambient CompanyId is always null, which would otherwise hide
        // their own PackageAssignment rows from themselves. Same rationale as
        // TenantResolutionService's own IgnoreQueryFilters usage.
        var user = await _unitOfWork.GetReadRepository<User>().GetAsync(
            u => u.Id == request.UserId,
            include: q => q.IgnoreQueryFilters()
                .Include(u => u.Assignments).ThenInclude(a => a.Company)
                .Include(u => u.PackageAssignments).ThenInclude(pa => pa.Company)
                .Include(u => u.PackageAssignments).ThenInclude(pa => pa.Package),
            cancellationToken: cancellationToken);

        if (user is null)
        {
            throw new NotFoundException("UserNotFound", request.UserId);
        }

        return new MeResultDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Phone = user.Phone,
            Email = user.Email,
            PreferredLanguage = user.PreferredLanguage,
            IsAccountFrozen = user.IsAccountFrozen,
            Assignments = user.Assignments
                .Where(a => a.IsActive)
                .Select(a => new MeAssignmentDto
                {
                    CompanyId = a.CompanyId,
                    CompanyName = a.Company?.Name,
                    BranchId = a.BranchId,
                    Role = a.Role.ToString(),
                })
                .ToList(),
            PackageAssignments = user.PackageAssignments
                .Where(pa => pa.Status != PackageAssignmentStatus.Cancelled)
                .Select(pa => new MePackageAssignmentDto
                {
                    Id = pa.Id,
                    CompanyId = pa.CompanyId,
                    CompanyName = pa.Company?.Name,
                    BranchId = pa.BranchId,
                    PackageId = pa.PackageId,
                    PackageName = pa.Package?.Name,
                    Price = pa.Package?.Price ?? 0,
                    Status = pa.Status.ToString(),
                    StartDate = pa.StartDate,
                    EndDate = pa.EndDate,
                    SessionCount = pa.Package?.SessionCount,
                    RemainingSessions = pa.RemainingSessions,
                    MaxFreezeDays = pa.Package?.MaxFreezeDays,
                    TotalFrozenDays = pa.TotalFrozenDays,
                })
                .ToList(),
        };
    }
}
