using MediatR;

namespace GymAppApi.Application.Features.Services.Commands.SetServiceActive;

public class SetServiceActiveCommand : IRequest
{
    // Controller tarafından route'tan set edilir.
    public int ServiceId { get; set; }
    public bool IsActive { get; set; }
}
