using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.Packages.Queries.GetPackageAssignments;

public class GetPackageAssignmentsQueryHandler : IRequestHandler<GetPackageAssignmentsQuery, IReadOnlyList<PackageAssignmentDto>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetPackageAssignmentsQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<IReadOnlyList<PackageAssignmentDto>> Handle(GetPackageAssignmentsQuery request, CancellationToken cancellationToken)
    {
        // PackageAssignment ICompanyScoped olduğu için global filtre çağıranın
        // ambient CompanyId'sine göre zaten daraltıyor - GetPackagesQueryHandler
        // ile aynı yetkilendirme yaklaşımı (BranchManager de kendi şirketindeki
        // tüm atamaları görür, GetPackages'ın da yaptığı gibi).
        var assignments = await _unitOfWork.GetReadRepository<PackageAssignment>().GetAllAsync(
            predicate: string.IsNullOrWhiteSpace(request.MemberPhone)
                ? null
                : a => a.MemberUser!.Phone.Contains(request.MemberPhone),
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
            };
        }).ToList();
    }
}
