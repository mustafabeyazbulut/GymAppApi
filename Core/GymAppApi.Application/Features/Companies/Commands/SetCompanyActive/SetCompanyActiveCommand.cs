using MediatR;

namespace GymAppApi.Application.Features.Companies.Commands.SetCompanyActive;

public class SetCompanyActiveCommand : IRequest
{
    // Set by the controller from the route segment, never trusted from the body.
    public int CompanyId { get; set; }

    public bool IsActive { get; set; }
}
