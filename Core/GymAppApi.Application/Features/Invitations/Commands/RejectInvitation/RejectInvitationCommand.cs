using MediatR;

namespace GymAppApi.Application.Features.Invitations.Commands.RejectInvitation;

public class RejectInvitationCommand : IRequest
{
    // "GymAdmin" | "Staff" | "Package" (büyük/küçük harf duyarsız).
    public string Type { get; set; } = null!;
    public int InvitationId { get; set; }
    // Controller tarafından JWT'den doldurulur.
    public int UserId { get; set; }
}
