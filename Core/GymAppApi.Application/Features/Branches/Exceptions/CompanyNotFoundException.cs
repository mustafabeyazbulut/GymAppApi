using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Branches.Exceptions;

public class CompanyNotFoundException : NotFoundException
{
    public CompanyNotFoundException(int companyId) : base("CompanyNotFound", companyId) { }
}
