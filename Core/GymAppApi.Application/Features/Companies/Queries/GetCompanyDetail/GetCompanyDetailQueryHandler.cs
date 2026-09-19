using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
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
            cancellationToken: cancellationToken);

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
            }).ToList(),
            GymAdminCount = assignments.Count(a => a.Role == AssignmentRole.GymAdmin),
            BranchManagerCount = assignments.Count(a => a.Role == AssignmentRole.BranchManager),
            TrainerCount = assignments.Count(a => a.Role == AssignmentRole.Trainer),
            MemberCount = assignments.Count(a => a.Role == AssignmentRole.Member),
        };
    }
}
