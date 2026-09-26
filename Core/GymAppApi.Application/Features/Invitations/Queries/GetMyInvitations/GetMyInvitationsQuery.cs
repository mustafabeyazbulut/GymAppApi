using MediatR;

namespace GymAppApi.Application.Features.Invitations.Queries.GetMyInvitations;

public class GetMyInvitationsQuery : IRequest<IReadOnlyList<InvitationDto>>
{
    // Controller tarafından JWT'den doldurulur.
    public int UserId { get; set; }
}

public class InvitationDto
{
    public int Id { get; set; }
    // "GymAdmin" | "Staff" | "Package" - kabul/red URL'sindeki {type}.
    public string Type { get; set; } = null!;
    public string? CompanyName { get; set; }
    public string? BranchName { get; set; }
    // Personel davetlerinde rol (GymAdmin/BranchManager/Trainer); paket davetinde null.
    public string? Role { get; set; }
    // Paket davetinde paket adı; personel davetinde null.
    public string? PackageName { get; set; }
    public string? InvitedByName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
