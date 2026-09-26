using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.Packages.Queries.GetPackageAssignments;

public class GetPackageAssignmentsQueryHandler : IRequestHandler<GetPackageAssignmentsQuery, IReadOnlyList<PackageAssignmentDto>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;
    private readonly IPhoneNumberNormalizer _phoneNumberNormalizer;

    public GetPackageAssignmentsQueryHandler(IUnitOfWork unitOfWork, ITenantContext tenantContext, IPhoneNumberNormalizer phoneNumberNormalizer)
    {
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
        _phoneNumberNormalizer = phoneNumberNormalizer;
    }

    // Tam ve geçerli bir numara ("0555 123 45 67", "+49 151 23456789")
    // kanonik E.164'e çevrilir - DB'deki değerle birebir eşleşsin. Kısmi
    // arama terimi ("1234567") geçerli bir numara olmadığından sadece
    // biçimlendirme karakterleri temizlenip "içerir" araması olarak kalır.
    private string? NormalizePhoneSearchTerm(string? term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return null;
        }

        return _phoneNumberNormalizer.TryNormalize(term, out var e164)
            ? e164
            : System.Text.RegularExpressions.Regex.Replace(term, @"[\s\-().\/]", string.Empty);
    }

    public async Task<IReadOnlyList<PackageAssignmentDto>> Handle(GetPackageAssignmentsQuery request, CancellationToken cancellationToken)
    {
        // PackageAssignment ICompanyScoped olduğu için global filtre çağıranın
        // ambient CompanyId'sine göre zaten daraltıyor. Şube kapsamlı personel
        // (BranchManager, ambient BranchId set) ek olarak sadece kendi
        // şubesinin atamalarını görür (senaryo §10.8) - firma geneli pakete
        // bağlı (BranchId null) atamalar da dahil değil, payments/check-ins
        // gibi id bazlı uç noktaların BranchManager kuralıyla
        // (a.BranchId == assignment.BranchId) tutarlı.
        var branchId = _tenantContext.BranchId;
        var memberPhone = NormalizePhoneSearchTerm(request.MemberPhone);
        var filterByPhone = !string.IsNullOrWhiteSpace(memberPhone);
        var assignments = await _unitOfWork.GetReadRepository<PackageAssignment>().GetAllAsync(
            predicate: a => (branchId == null || a.BranchId == branchId) &&
                            (!filterByPhone || a.MemberUser!.Phone.Contains(memberPhone!)),
            include: q => q.Include(a => a.Package!).Include(a => a.MemberUser!),
            orderBy: q => q.OrderByDescending(a => a.CreatedAt),
            cancellationToken: cancellationToken);

        if (assignments.Count == 0)
        {
            return Array.Empty<PackageAssignmentDto>();
        }

        var assignmentIds = assignments.Select(a => a.Id).ToList();
        var payments = await _unitOfWork.GetReadRepository<PackageAssignmentPayment>().GetAllAsync(
            p => assignmentIds.Contains(p.PackageAssignmentId), cancellationToken: cancellationToken);
        var paidByAssignmentId = payments
            .GroupBy(p => p.PackageAssignmentId)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));

        return assignments.Select(a =>
        {
            var totalPaid = paidByAssignmentId.GetValueOrDefault(a.Id, 0m);
            var price = a.Package?.Price ?? 0m;
            return new PackageAssignmentDto
            {
                Id = a.Id,
                PackageId = a.PackageId,
                PackageName = a.Package?.Name ?? string.Empty,
                Price = price,
                MemberUserId = a.MemberUserId,
                MemberFullName = a.MemberUser?.FullName ?? string.Empty,
                MemberPhone = a.MemberUser?.Phone ?? string.Empty,
                CompanyId = a.CompanyId,
                BranchId = a.BranchId,
                StartDate = a.StartDate,
                EndDate = a.EndDate,
                RemainingSessions = a.RemainingSessions,
                Status = a.Status.ToString(),
                TotalPaid = totalPaid,
                RemainingBalance = price - totalPaid,
                MaxFreezeDays = a.Package?.MaxFreezeDays,
                TotalFrozenDays = a.TotalFrozenDays,
            };
        }).ToList();
    }
}
