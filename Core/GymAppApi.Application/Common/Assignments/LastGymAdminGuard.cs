using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Assignments.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Common.Assignments;

// Bir firma her zaman en az bir kullanılabilir Gym Admin'e sahip olmalı
// (senaryo §4.5). Hesabını silen/donduran kullanıcı, aktif bir firmada
// başka kullanılabilir (aktif atamalı, hesabı dondurulmamış) Gym Admin
// yoksa bunu yapamaz - önce başka bir Gym Admin atanmalı.
public static class LastGymAdminGuard
{
    public static async Task EnsureNotLastGymAdminAnywhereAsync(IUnitOfWork unitOfWork, int userId, CancellationToken cancellationToken)
    {
        var assignmentReadRepo = unitOfWork.GetReadRepository<Assignment>();

        // IgnoreQueryFilters: hesap işlemleri tenant bağlamından bağımsız -
        // kullanıcının TÜM firmalarındaki Gym Admin atamaları görülmeli.
        var ownGymAdminAssignments = await assignmentReadRepo.GetAllAsync(
            a => a.UserId == userId && a.Role == AssignmentRole.GymAdmin && a.IsActive &&
                 a.CompanyId != null && a.Company!.IsActive,
            include: q => q.IgnoreQueryFilters().Include(a => a.Company),
            cancellationToken: cancellationToken);
        if (ownGymAdminAssignments.Count == 0)
        {
            return;
        }

        var companyIds = ownGymAdminAssignments.Select(a => a.CompanyId!.Value).Distinct().ToList();
        var otherUsableGymAdmins = await assignmentReadRepo.GetAllAsync(
            a => a.CompanyId != null && companyIds.Contains(a.CompanyId.Value) &&
                 a.Role == AssignmentRole.GymAdmin && a.IsActive &&
                 a.UserId != userId && !a.User!.IsAccountFrozen,
            include: q => q.IgnoreQueryFilters().Include(a => a.User),
            cancellationToken: cancellationToken);

        var companiesCovered = otherUsableGymAdmins.Select(a => a.CompanyId!.Value).ToHashSet();
        var uncoveredCompany = ownGymAdminAssignments.FirstOrDefault(a => !companiesCovered.Contains(a.CompanyId!.Value));
        if (uncoveredCompany is not null)
        {
            throw new LastGymAdminException();
        }
    }
}
