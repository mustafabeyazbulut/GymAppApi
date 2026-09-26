using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Invitations.Exceptions;

// Davet gönderildikten sonra şube/firma kapatılmış ya da paket pasife alınmış.
public class InvitationTargetInactiveException : ConflictException
{
    public InvitationTargetInactiveException() : base("InvitationTargetInactive") { }
}
