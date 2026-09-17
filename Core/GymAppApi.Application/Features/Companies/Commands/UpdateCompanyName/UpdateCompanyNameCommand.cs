using MediatR;

namespace GymAppApi.Application.Features.Companies.Commands.UpdateCompanyName;

public class UpdateCompanyNameCommand : IRequest
{
    // Set by the controller from the route segment, never trusted from the body.
    public int CompanyId { get; set; }

    public string Name { get; set; } = null!;
}
