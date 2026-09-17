using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
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

        return companies.Select(c => new CompanyListItemDto
        {
            Id = c.Id,
            Name = c.Name,
            IsActive = c.IsActive,
            BranchCount = c.Branches.Count,
        }).ToList();
    }
}
