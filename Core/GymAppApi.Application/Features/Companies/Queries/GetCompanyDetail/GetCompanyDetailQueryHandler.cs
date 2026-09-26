using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.PackageAssignments;
using GymAppApi.Application.Features.Branches.Queries.GetBranches;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.Companies.Queries.GetCompanyDetail;

public class GetCompanyDetailQueryHandler : IRequestHandler<GetCompanyDetailQuery, CompanyDetailDto>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetCompanyDetailQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<CompanyDetailDto> Handle(GetCompanyDetailQuery request, CancellationToken cancellationToken)
    {
        var company = await _unitOfWork.GetReadRepository<Company>().GetAsync(
            c => c.Id == request.CompanyId,
            include: q => q.Include(c => c.Branches),
            cancellationToken: cancellationToken);

        if (company is null)
        {
            throw new NotFoundException("CompanyNotFound", request.CompanyId);
        }

        // Bu uc noktaya sadece SuperAdmin erisebiliyor (CompaniesController'daki
        // SuperAdminOnly policy'si), bu yuzden Assignment'in tenant-scoping
        // global filtresi (_tenantContext.IsSuperAdmin) burada devre disi kaliyor
        // ve bu sirkete ait tum atamalar guvenle cekilebiliyor.
        var assignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            predicate: a => a.CompanyId == request.CompanyId && a.IsActive,
            include: q => q.Include(a => a.User!),
            cancellationToken: cancellationToken);

        // Senaryo §3.2: firmanın üyesi = o firmada GEÇERLİ paketi olan tekil kullanıcı.
        var validPackageAssignments = await _unitOfWork.GetReadRepository<PackageAssignment>().GetAllAsync(
            PackageAssignmentValidity.Usable(DateTime.UtcNow),
            include: q => q.IgnoreQueryFilters().Include(pa => pa.Package),
            cancellationToken: cancellationToken);
        var memberCount = validPackageAssignments
            .Where(pa => pa.CompanyId == request.CompanyId)
            .Select(pa => pa.MemberUserId)
            .Distinct()
            .Count();

        // Bir subenin BranchManager'i - ayni subeye birden fazla BranchManager
        // atanmasi teoride mumkun olsa da (AddStaffMember bunu engellemiyor),
        // pratikte her zaman tek kisi olduğundan ilkini gostermek yeterli.
        var branchManagerNameByBranchId = assignments
            .Where(a => a.Role == AssignmentRole.BranchManager && a.BranchId != null)
            .GroupBy(a => a.BranchId!.Value)
            .ToDictionary(g => g.Key, g => g.First().User?.FullName);

        return new CompanyDetailDto
        {
            Id = company.Id,
            Name = company.Name,
            IsActive = company.IsActive,
            Branches = company.Branches.Select(b => new BranchListItemDto
            {
                Id = b.Id,
                CompanyId = b.CompanyId,
                Name = b.Name,
                Address = b.Address,
                IsActive = b.IsActive,
                ManagerName = branchManagerNameByBranchId.GetValueOrDefault(b.Id),
            }).ToList(),
            GymAdmins = assignments
                .Where(a => a.Role == AssignmentRole.GymAdmin)
                .Select(a => new CompanyGymAdminDto
                {
                    AssignmentId = a.Id,
                    UserId = a.UserId,
                    FullName = a.User?.FullName ?? string.Empty,
                    Phone = a.User?.Phone ?? string.Empty,
                }).ToList(),
            GymAdminCount = assignments.Count(a => a.Role == AssignmentRole.GymAdmin),
            BranchManagerCount = assignments.Count(a => a.Role == AssignmentRole.BranchManager),
            TrainerCount = assignments.Count(a => a.Role == AssignmentRole.Trainer),
            MemberCount = memberCount,
        };
    }
}
