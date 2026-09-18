using MediatR;

namespace GymAppApi.Application.Features.Assignments.Queries.GetStaffMembers;

// Personelin (GymAdmin/BranchManager) kendi şirketindeki personeli
// (GymAdmin/BranchManager/Trainer) görüp kaldırabilmesi için - şimdiye
// kadar bir kişiyi eklemenin (AddStaffMember/InviteGymAdmin) bir yolu
// vardı ama listeleyip kaldırmanın (DELETE /api/assignments/{id} zaten
// vardı) hiçbir yolu yoktu.
public class GetStaffMembersQuery : IRequest<IReadOnlyList<StaffMemberDto>>
{
}
