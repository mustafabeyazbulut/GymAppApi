using GymAppApi.Application.Features.Analytics.Queries.GetAnalyticsSummary;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymAppApi.WebApi.Controllers;

// StaffManagement policy zaten BranchManager/GymAdmin/SuperAdmin'i kapsıyor
// (bkz. Program.cs) - ayrı bir "SuperAdmin de erişebilsin" ekine gerek yok.
// Kapsam daraltması query param DEĞİL, tamamen ambient ITenantContext
// üzerinden (bkz. GetAnalyticsSummaryQueryHandler).
[ApiController]
[Route("api/analytics")]
[Authorize(Policy = "StaffManagement")]
public class AnalyticsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AnalyticsController(IMediator mediator) => _mediator = mediator;

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetAnalyticsSummaryQuery(), cancellationToken));
}
