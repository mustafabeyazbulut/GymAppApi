using GymAppApi.Application.Common.Behaviors;
using MediatR;

namespace GymAppApi.Application.Features.ClassScheduling.Commands.CancelClassEnrollment;

// ITransactionalRequest: ders, kayıt ve paket ataması FOR UPDATE ile kilitlenir
// (hak iadesi eşzamanlı check-in/katılımla kaybolmasın, aynı kayıt iki kez
// iade edilmesin); kilitler transaction içinde anlamlı.
public class CancelClassEnrollmentCommand : IRequest, ITransactionalRequest
{
    // Set by the controller from the route segment.
    public int ClassEnrollmentId { get; set; }
    // Set by the controller from the caller's own JWT sub claim.
    public int RequestedByUserId { get; set; }
}
