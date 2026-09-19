using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using MediatR;

namespace GymAppApi.Application.Features.Notifications.Commands.MarkNotificationRead;

public class MarkNotificationReadCommandHandler : IRequestHandler<MarkNotificationReadCommand>
{
    private readonly IUnitOfWork _unitOfWork;

    public MarkNotificationReadCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Handle(MarkNotificationReadCommand request, CancellationToken cancellationToken)
    {
        var notification = await _unitOfWork.GetReadRepository<Notification>().GetAsync(
            n => n.Id == request.NotificationId && n.UserId == request.UserId, cancellationToken: cancellationToken);
        if (notification is null)
        {
            throw new NotFoundException("NotificationNotFound", request.NotificationId);
        }

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            _unitOfWork.GetWriteRepository<Notification>().Update(notification);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
