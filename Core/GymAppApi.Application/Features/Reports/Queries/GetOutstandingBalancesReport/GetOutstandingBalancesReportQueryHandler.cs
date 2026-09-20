using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.Reports.Queries.GetOutstandingBalancesReport;

// "Kimden para alacağız" listesi - GymAdmin/BranchManager'ın tahsilat takibi
// için. İptal edilmiş (Cancelled) atamalar kasıtlı olarak hariç - artık
// tahsil edilmesi beklenen bir borç değil. Dondurulmuş (Frozen) atamalar
// DAHİL - dondurma sadece kullanım hakkını durdurur, borcu silmez.
public class GetOutstandingBalancesReportQueryHandler : IRequestHandler<GetOutstandingBalancesReportQuery, IReadOnlyList<OutstandingBalanceDto>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;

    public GetOutstandingBalancesReportQueryHandler(IUnitOfWork unitOfWork, ITenantContext tenantContext)
    {
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
    }

    public async Task<IReadOnlyList<OutstandingBalanceDto>> Handle(GetOutstandingBalancesReportQuery request, CancellationToken cancellationToken)
    {
        var branchId = _tenantContext.BranchId;

        var assignments = await _unitOfWork.GetReadRepository<PackageAssignment>().GetAllAsync(
            pa => pa.Status != PackageAssignmentStatus.Cancelled && (branchId == null || pa.BranchId == branchId),
            include: q => q.Include(pa => pa.Package).Include(pa => pa.MemberUser).Include(pa => pa.Branch),
            cancellationToken: cancellationToken);

        if (assignments.Count == 0)
        {
            return Array.Empty<OutstandingBalanceDto>();
        }

        var assignmentIds = assignments.Select(a => a.Id).ToList();
        var payments = await _unitOfWork.GetReadRepository<PackageAssignmentPayment>().GetAllAsync(
            p => assignmentIds.Contains(p.PackageAssignmentId), cancellationToken: cancellationToken);
        var paidByAssignmentId = payments.GroupBy(p => p.PackageAssignmentId).ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));

        return assignments
            .Select(a =>
            {
                var totalPaid = paidByAssignmentId.GetValueOrDefault(a.Id);
                return new OutstandingBalanceDto
                {
                    PackageAssignmentId = a.Id,
                    MemberFullName = a.MemberUser!.FullName,
                    MemberPhone = a.MemberUser.Phone,
                    PackageName = a.Package!.Name,
                    BranchName = a.Branch?.Name,
                    Price = a.Package.Price,
                    TotalPaid = totalPaid,
                    RemainingBalance = a.Package.Price - totalPaid,
                };
            })
            .Where(dto => dto.RemainingBalance > 0)
            .OrderByDescending(dto => dto.RemainingBalance)
            .ToList();
    }
}
