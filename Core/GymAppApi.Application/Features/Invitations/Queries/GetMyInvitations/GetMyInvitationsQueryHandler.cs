using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Invitations.Common;
using GymAppApi.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.Invitations.Queries.GetMyInvitations;

// Çağıranın kendi, bekleyen (kullanılmamış) ve süresi dolmamış tüm davetleri:
// personel davetleri (AddStaffMember, InviteGymAdmin, CreateCompany'nin
// GymAdmin daveti) ve paket davetleri (CreatePackageAssignment).
public class GetMyInvitationsQueryHandler : IRequestHandler<GetMyInvitationsQuery, IReadOnlyList<InvitationDto>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetMyInvitationsQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<IReadOnlyList<InvitationDto>> Handle(GetMyInvitationsQuery request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        // Davet tabloları tenant kapsamlı değil (davetli henüz o firmaya bağlı
        // değil); sorgular çağıranın kendi satırlarına sabit.
        var staffInvitations = await _unitOfWork.GetReadRepository<PendingAssignmentInvitation>().GetAllAsync(
            p => p.TargetUserId == request.UserId && !p.IsUsed && p.ExpiresAt > now, cancellationToken: cancellationToken);
        var packageInvitations = await _unitOfWork.GetReadRepository<PendingPackageAssignmentInvitation>().GetAllAsync(
            p => p.TargetUserId == request.UserId && !p.IsUsed && p.ExpiresAt > now, cancellationToken: cancellationToken);

        if (staffInvitations.Count == 0 && packageInvitations.Count == 0)
        {
            return Array.Empty<InvitationDto>();
        }

        // Görüntüleme için ad bilgileri. IgnoreQueryFilters: davetlinin bu
        // firmada bağlamı yok - filtreli okuma firma/şube/paket adlarını gizlerdi.
        // Sadece davetlerde geçen Id'ler okunuyor.
        var companyIds = staffInvitations.Select(p => p.CompanyId).Concat(packageInvitations.Select(p => p.CompanyId)).Distinct().ToList();
        var branchIds = staffInvitations.Select(p => p.BranchId).Concat(packageInvitations.Select(p => p.BranchId))
            .Where(id => id != null).Select(id => id!.Value).Distinct().ToList();
        var packageIds = packageInvitations.Select(p => p.PackageId).Distinct().ToList();
        var inviterIds = staffInvitations.Select(p => p.RequestedByUserId).Concat(packageInvitations.Select(p => p.RequestedByUserId)).Distinct().ToList();

        var companies = await _unitOfWork.GetReadRepository<Company>().GetAllAsync(
            c => companyIds.Contains(c.Id), include: q => q.IgnoreQueryFilters().Include(c => c.Branches), cancellationToken: cancellationToken);
        var branches = branchIds.Count == 0
            ? new List<Branch>()
            : (await _unitOfWork.GetReadRepository<Branch>().GetAllAsync(
                b => branchIds.Contains(b.Id), include: q => q.IgnoreQueryFilters().Include(b => b.Company), cancellationToken: cancellationToken)).ToList();
        var packages = packageIds.Count == 0
            ? new List<Package>()
            : (await _unitOfWork.GetReadRepository<Package>().GetAllAsync(
                p => packageIds.Contains(p.Id), include: q => q.IgnoreQueryFilters().Include(p => p.Company), cancellationToken: cancellationToken)).ToList();
        var inviters = await _unitOfWork.GetReadRepository<User>().GetAllAsync(
            u => inviterIds.Contains(u.Id), cancellationToken: cancellationToken);

        var companyName = companies.ToDictionary(c => c.Id, c => c.Name);
        var branchName = branches.ToDictionary(b => b.Id, b => b.Name);
        var packageName = packages.ToDictionary(p => p.Id, p => p.Name);
        var inviterName = inviters.ToDictionary(u => u.Id, u => u.FullName);

        var result = staffInvitations.Select(p => new InvitationDto
            {
                Id = p.Id,
                Type = InvitationTypes.ForRole(p.Role),
                CompanyName = companyName.GetValueOrDefault(p.CompanyId),
                BranchName = p.BranchId is int b ? branchName.GetValueOrDefault(b) : null,
                Role = p.Role.ToString(),
                PackageName = null,
                InvitedByName = inviterName.GetValueOrDefault(p.RequestedByUserId),
                CreatedAt = p.CreatedAt,
                ExpiresAt = p.ExpiresAt,
            })
            .Concat(packageInvitations.Select(p => new InvitationDto
            {
                Id = p.Id,
                Type = InvitationTypes.Package,
                CompanyName = companyName.GetValueOrDefault(p.CompanyId),
                BranchName = p.BranchId is int b ? branchName.GetValueOrDefault(b) : null,
                Role = null,
                PackageName = packageName.GetValueOrDefault(p.PackageId),
                InvitedByName = inviterName.GetValueOrDefault(p.RequestedByUserId),
                CreatedAt = p.CreatedAt,
                ExpiresAt = p.ExpiresAt,
            }))
            .OrderByDescending(i => i.CreatedAt)
            .ToList();

        return result;
    }
}
