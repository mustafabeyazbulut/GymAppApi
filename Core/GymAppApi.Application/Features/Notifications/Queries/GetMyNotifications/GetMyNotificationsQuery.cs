using MediatR;

namespace GymAppApi.Application.Features.Notifications.Queries.GetMyNotifications;

public class GetMyNotificationsQuery : IRequest<IReadOnlyList<NotificationDto>>
{
    // Set by the controller from the caller's own JWT sub claim - a
    // notification feed only ever shows the caller's own notifications.
    public int UserId { get; set; }
}
