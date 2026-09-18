using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.Assignments.Queries.GetStaffMembers;

public class GetStaffMembersQueryHandler : IRequestHandler<GetStaffMembersQuery, IReadOnlyList<StaffMemberDto>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetStaffMembersQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<IReadOnlyList<StaffMemberDto>> Handle(GetStaffMembersQuery request, CancellationToken cancellationToken)
    {
        // Assignment ITenantScoped (nullable CompanyId/BranchId) olduğu için
        // otomatik filtre zaten çağıranın ambient şirketine/şubesine göre
        // daraltıyor (X-Active-Company-Id dahil, bkz. TenantResolutionService) -
        // burada sadece SuperAdmin'in kendi platform-geneli (CompanyId null)
        // atamalarının sızmasını engellemek için ek bir predicate gerekiyor
        // (SetNullableTenantFilter, null CompanyId'yi "herkese görünür" sayar).
        var assignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            predicate: a => a.CompanyId != null && a.IsActive && a.Role != AssignmentRole.SuperAdmin,
            include: q => q.Include(a => a.User!).Include(a => a.Branch!),
            orderBy: q => q.OrderBy(a => a.Role).ThenBy(a => a.Id),
            cancellationToken: cancellationToken);

        return assignments.Select(a => new StaffMemberDto
        {
            AssignmentId = a.Id,
            UserId = a.UserId,
            FullName = a.User?.FullName ?? string.Empty,
            Phone = a.User?.Phone ?? string.Empty,
            Role = a.Role.ToString(),
            CompanyId = a.CompanyId,
            BranchId = a.BranchId,
            BranchName = a.Branch?.Name,
        }).ToList();
    }
}
