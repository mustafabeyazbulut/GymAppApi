using MediatR;

namespace GymAppApi.Application.Features.Companies.Queries.GetCompanies;

public class GetCompaniesQuery : IRequest<IReadOnlyList<CompanyListItemDto>>
{
}
