using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.PackageAssignments;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.Companies.Queries.GetCompanies;

public class GetCompaniesQueryHandler : IRequestHandler<GetCompaniesQuery, IReadOnlyList<CompanyListItemDto>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetCompaniesQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<IReadOnlyList<CompanyListItemDto>> Handle(GetCompaniesQuery request, CancellationToken cancellationToken)
    {
        var companies = await _unitOfWork.GetReadRepository<Company>().GetAllAsync(
            include: q => q.Include(c => c.Branches),
            cancellationToken: cancellationToken);

        // Bu uc noktaya sadece SuperAdmin erisebiliyor (CompaniesController'daki
        // SuperAdminOnly policy'si), bu yuzden Assignment'in tenant-scoping
        // global filtresi (_tenantContext.IsSuperAdmin) burada devre disi kaliyor
        // ve tum sirketlerin atamalari tek sorguda guvenle cekilebiliyor.
        var assignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            predicate: a => a.CompanyId != null && a.IsActive && a.Role != AssignmentRole.SuperAdmin,
            cancellationToken: cancellationToken);
        var assignmentsByCompany = assignments.ToLookup(a => a.CompanyId!.Value);

        // Senaryo §3.2: firmanın üyesi = o firmada GEÇERLİ paketi olan kullanıcı.
        // Aynı kişinin birden fazla geçerli paketi tek üye sayılır.
        var validPackageAssignments = await _unitOfWork.GetReadRepository<PackageAssignment>().GetAllAsync(
            PackageAssignmentValidity.Usable(DateTime.UtcNow),
            include: q => q.IgnoreQueryFilters().Include(pa => pa.Package),
            cancellationToken: cancellationToken);
        var memberCountByCompany = validPackageAssignments
            .GroupBy(pa => pa.CompanyId)
            .ToDictionary(g => g.Key, g => g.Select(pa => pa.MemberUserId).Distinct().Count());

        return companies.Select(c =>
        {
            var companyAssignments = assignmentsByCompany[c.Id];
            return new CompanyListItemDto
            {
                Id = c.Id,
                Name = c.Name,
                IsActive = c.IsActive,
                BranchCount = c.Branches.Count,
                GymAdminCount = companyAssignments.Count(a => a.Role == AssignmentRole.GymAdmin),
                BranchManagerCount = companyAssignments.Count(a => a.Role == AssignmentRole.BranchManager),
                TrainerCount = companyAssignments.Count(a => a.Role == AssignmentRole.Trainer),
                MemberCount = memberCountByCompany.GetValueOrDefault(c.Id),
            };
        }).ToList();
    }
}
