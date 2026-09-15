namespace GymAppApi.Application.Common.Interfaces;

public interface IEmailSender
{
    Task SendAsync(string emailAddress, string subject, string body, CancellationToken cancellationToken = default);
}
