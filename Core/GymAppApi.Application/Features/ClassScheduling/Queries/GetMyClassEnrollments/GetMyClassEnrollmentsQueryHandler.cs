using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.ClassScheduling.Queries.GetMyClassEnrollments;

// Bir Member'ın ambient CompanyId'si yoktur - MemberUserId == çağıran
// filtresi kendiliğinden görünürlüğü sağlıyor, GetMyReservationsQueryHandler
// ile aynı desen.
public class GetMyClassEnrollmentsQueryHandler : IRequestHandler<GetMyClassEnrollmentsQuery, IReadOnlyList<MyClassEnrollmentDto>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetMyClassEnrollmentsQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<IReadOnlyList<MyClassEnrollmentDto>> Handle(GetMyClassEnrollmentsQuery request, CancellationToken cancellationToken)
    {
        var enrollments = await _unitOfWork.GetReadRepository<ClassEnrollment>().GetAllAsync(
            e => e.MemberUserId == request.MemberUserId,
            include: q => q.IgnoreQueryFilters().Include(e => e.ClassSession),
            cancellationToken: cancellationToken);

        return enrollments
            .OrderByDescending(e => e.ClassSession!.Date)
            .ThenByDescending(e => e.ClassSession!.StartTime)
            .Select(e => new MyClassEnrollmentDto
            {
                Id = e.Id,
                ClassSessionId = e.ClassSessionId,
                ClassName = e.ClassSession?.Name ?? string.Empty,
                Category = e.ClassSession?.Category.ToString() ?? string.Empty,
                Date = e.ClassSession?.Date ?? default,
                StartTime = e.ClassSession?.StartTime ?? default,
                EndTime = e.ClassSession?.EndTime ?? default,
                Status = e.Status.ToString(),
            })
            .ToList();
    }
}
