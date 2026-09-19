using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.ClassScheduling.Queries.GetClassSessions;

// Herkes çağırabilir ([Authorize]) - bir Member'ın ambient CompanyId'si yok,
// bu yüzden IgnoreQueryFilters + kendi BranchId/tarih filtremizle görünürlüğü
// kendimiz sağlıyoruz (CreateReservationCommandHandler'daki standing rule).
public class GetClassSessionsQueryHandler : IRequestHandler<GetClassSessionsQuery, IReadOnlyList<ClassSessionDto>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetClassSessionsQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<IReadOnlyList<ClassSessionDto>> Handle(GetClassSessionsQuery request, CancellationToken cancellationToken)
    {
        var sessions = await _unitOfWork.GetReadRepository<ClassSession>().GetAllAsync(
            s => (request.BranchId == null || s.BranchId == request.BranchId) &&
                 (request.From == null || s.Date >= request.From) &&
                 (request.To == null || s.Date <= request.To),
            include: q => q.IgnoreQueryFilters().Include(s => s.Branch),
            orderBy: q => q.OrderBy(s => s.Date).ThenBy(s => s.StartTime),
            cancellationToken: cancellationToken);

        if (sessions.Count == 0)
        {
            return Array.Empty<ClassSessionDto>();
        }

        // Doluluk sayısını her ders için ayrı bir sorguyla değil, TÜM
        // oturumlar için TEK bir sorguyla toplu çekip bellek içinde
        // grupluyoruz - N+1 yok (master spec'in performans notu).
        var sessionIds = sessions.Select(s => s.Id).ToList();
        var activeEnrollments = await _unitOfWork.GetReadRepository<ClassEnrollment>().GetAllAsync(
            e => sessionIds.Contains(e.ClassSessionId) &&
                 (e.Status == ClassEnrollmentStatus.Reserved || e.Status == ClassEnrollmentStatus.Attended),
            include: q => q.IgnoreQueryFilters().Include(e => e.ClassSession),
            cancellationToken: cancellationToken);

        var enrolledCountsBySessionId = activeEnrollments
            .GroupBy(e => e.ClassSessionId)
            .ToDictionary(g => g.Key, g => g.Count());

        return sessions.Select(s => new ClassSessionDto
        {
            Id = s.Id,
            BranchId = s.BranchId,
            TrainerUserId = s.TrainerUserId,
            Category = s.Category.ToString(),
            Name = s.Name,
            Date = s.Date,
            StartTime = s.StartTime,
            EndTime = s.EndTime,
            Capacity = s.Capacity,
            EnrolledCount = enrolledCountsBySessionId.GetValueOrDefault(s.Id),
            CancellationCutoffHours = s.CancellationCutoffHours,
        }).ToList();
    }
}
