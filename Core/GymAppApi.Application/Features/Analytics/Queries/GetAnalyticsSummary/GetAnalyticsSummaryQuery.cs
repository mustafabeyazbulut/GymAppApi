using MediatR;

namespace GymAppApi.Application.Features.Analytics.Queries.GetAnalyticsSummary;

// Parametresiz - kapsam tamamen ambient ITenantContext'ten gelir (Super
// Admin/Gym Admin/Şube Yöneticisi ayrımı ayrı bir query param gerektirmez,
// bkz. docs/superpowers/specs/2026-09-20-analytics-design.md "API" bölümü).
public class GetAnalyticsSummaryQuery : IRequest<AnalyticsSummaryDto>
{
}
