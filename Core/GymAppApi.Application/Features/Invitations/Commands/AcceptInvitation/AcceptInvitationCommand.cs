using MediatR;

namespace GymAppApi.Application.Features.Invitations.Commands.AcceptInvitation;

// Uygulama içinden (SMS kodu olmadan) davet kabulü. Kimlik JWT ile
// doğrulanmış ve telefonu doğrulanmış kullanıcı için - kod gerektirmez.
public class AcceptInvitationCommand : IRequest
{
    // "GymAdmin" | "Staff" | "Package" (büyük/küçük harf duyarsız).
    public string Type { get; set; } = null!;
    public int InvitationId { get; set; }
    // Controller tarafından JWT'den doldurulur.
    public int UserId { get; set; }
}
