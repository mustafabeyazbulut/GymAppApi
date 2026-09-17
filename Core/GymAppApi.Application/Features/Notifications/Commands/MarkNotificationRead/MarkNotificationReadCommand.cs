using MediatR;

namespace GymAppApi.Application.Features.Notifications.Commands.MarkNotificationRead;

public class MarkNotificationReadCommand : IRequest
{
    // Set by the controller from the route segment.
    public int NotificationId { get; set; }

    // Set by the controller from the caller's own JWT sub claim - a
    // notification id alone must never let one user touch (or even
    // discover the existence of) another user's notification.
    public int UserId { get; set; }
}
