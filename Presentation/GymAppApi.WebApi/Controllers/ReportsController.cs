using GymAppApi.Application.Features.Reports.Queries.GetExpiringMemberships;
using GymAppApi.Application.Features.Reports.Queries.GetOutstandingBalancesReport;
using GymAppApi.Application.Features.Reports.Queries.GetRevenueReport;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymAppApi.WebApi.Controllers;

// StaffManagement policy zaten BranchManager/GymAdmin/SuperAdmin'i kapsıyor.
// Kapsam daraltması query param DEĞİL, ambient ITenantContext üzerinden -
// Analytics modülüyle aynı desen.
[ApiController]
[Route("api/reports")]
[Authorize(Policy = "StaffManagement")]
public class ReportsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ReportsController(IMediator mediator) => _mediator = mediator;

    [HttpGet("revenue")]
    public async Task<IActionResult> GetRevenue(
        [FromQuery] DateOnly? fromDate, [FromQuery] DateOnly? toDate, CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetRevenueReportQuery { FromDate = fromDate, ToDate = toDate }, cancellationToken));

    [HttpGet("outstanding-balances")]
    public async Task<IActionResult> GetOutstandingBalances(CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetOutstandingBalancesReportQuery(), cancellationToken));

    [HttpGet("expiring-memberships")]
    public async Task<IActionResult> GetExpiringMemberships([FromQuery] int daysAhead, CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetExpiringMembershipsReportQuery { DaysAhead = daysAhead <= 0 ? 30 : daysAhead }, cancellationToken));
}
