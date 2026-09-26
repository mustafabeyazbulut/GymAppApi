using GymAppApi.Application.Common.Behaviors;
using MediatR;

namespace GymAppApi.Application.Features.Packages.Commands.RecordGeneralCheckIn;

// ITransactionalRequest: seans hakkı düşümü için paket ataması FOR UPDATE ile
// kilitlenir; kilit transaction içinde anlamlı.
public class RecordGeneralCheckInCommand : IRequest, ITransactionalRequest
{
    // Set by the controller from the route segment.
    public int PackageAssignmentId { get; set; }
    // Set by the controller from the caller's own JWT sub claim.
    public int RequestedByUserId { get; set; }
}
