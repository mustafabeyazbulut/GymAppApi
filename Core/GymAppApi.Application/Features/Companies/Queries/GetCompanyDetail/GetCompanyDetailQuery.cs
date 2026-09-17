using MediatR;

namespace GymAppApi.Application.Features.Companies.Queries.GetCompanyDetail;

public class GetCompanyDetailQuery : IRequest<CompanyDetailDto>
{
    public GetCompanyDetailQuery(int companyId) => CompanyId = companyId;

    public int CompanyId { get; }
}
