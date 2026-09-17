using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using MediatR;

namespace GymAppApi.Application.Features.Notifications.Queries.GetMyNotifications;

public class GetMyNotificationsQueryHandler : IRequestHandler<GetMyNotificationsQuery, IReadOnlyList<NotificationDto>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetMyNotificationsQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<IReadOnlyList<NotificationDto>> Handle(GetMyNotificationsQuery request, CancellationToken cancellationToken)
    {
        var notifications = await _unitOfWork.GetReadRepository<Notification>().GetAllAsync(
            n => n.UserId == request.UserId,
            orderBy: q => q.OrderByDescending(n => n.CreatedAt),
            cancellationToken: cancellationToken);

        return notifications.Select(n => new NotificationDto
        {
            Id = n.Id,
            Title = n.Title,
            Body = n.Body,
            IsRead = n.IsRead,
            CreatedAt = n.CreatedAt,
        }).ToList();
    }
}
