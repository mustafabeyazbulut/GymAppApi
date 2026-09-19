using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Branches.Queries.GetBranches;
using GymAppApi.Domain.Entities;
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
        };
    }
}
