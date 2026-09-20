using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.Reports.Queries.GetExpiringMemberships;

// "Kimi aramalıyız" listesi - yenileme/satış takibi için. Sadece süre bazlı
// (EndDate != null) paketler kapsanır - seans bazlı paketlerde "bitiş
// tarihi" kavramı yok (bkz. PackageAssignmentStatus.cs'in "Expired hiç
// saklanmaz" notu - burada da aynı hesaplama tekrarlanıyor).
public class GetExpiringMembershipsReportQueryHandler : IRequestHandler<GetExpiringMembershipsReportQuery, IReadOnlyList<ExpiringMembershipDto>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;

    public GetExpiringMembershipsReportQueryHandler(IUnitOfWork unitOfWork, ITenantContext tenantContext)
    {
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
    }

    public async Task<IReadOnlyList<ExpiringMembershipDto>> Handle(GetExpiringMembershipsReportQuery request, CancellationToken cancellationToken)
    {
        var branchId = _tenantContext.BranchId;
        var now = DateTime.UtcNow;
        var cutoff = now.AddDays(request.DaysAhead);

        var assignments = await _unitOfWork.GetReadRepository<PackageAssignment>().GetAllAsync(
            pa => pa.Status == PackageAssignmentStatus.Active &&
                  pa.EndDate != null &&
                  pa.EndDate <= cutoff &&
                  (branchId == null || pa.BranchId == branchId),
            include: q => q.Include(pa => pa.Package).Include(pa => pa.MemberUser).Include(pa => pa.Branch),
            cancellationToken: cancellationToken);

        return assignments
            .Select(a => new ExpiringMembershipDto
            {
                PackageAssignmentId = a.Id,
                MemberFullName = a.MemberUser!.FullName,
                MemberPhone = a.MemberUser.Phone,
                PackageName = a.Package!.Name,
                BranchName = a.Branch?.Name,
                EndDate = a.EndDate!.Value,
                DaysRemaining = (int)Math.Floor((a.EndDate.Value - now).TotalDays),
            })
            .OrderBy(dto => dto.DaysRemaining)
            .ToList();
    }
}
