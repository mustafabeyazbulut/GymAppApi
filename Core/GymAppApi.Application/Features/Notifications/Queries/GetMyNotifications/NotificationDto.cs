namespace GymAppApi.Application.Features.Notifications.Queries.GetMyNotifications;

public class NotificationDto
{
    public int Id { get; set; }
    public string Title { get; set; } = null!;
    public string Body { get; set; } = null!;
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}
