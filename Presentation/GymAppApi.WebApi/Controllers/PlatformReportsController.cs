using GymAppApi.Application.Features.PlatformReports.Queries.GetPlatformCompanyBranches;
using GymAppApi.Application.Features.PlatformReports.Queries.GetPlatformSummary;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymAppApi.WebApi.Controllers;

// Sistem Sahibi platform raporları. SuperAdminOnly aktif role bakar: personel
// görevini seçmiş (X-Active-Assignment-Id header'lı) bir SuperAdmin 403 alır.
[ApiController]
[Route("api/platform-reports")]
[Authorize(Policy = "SuperAdminOnly")]
public class PlatformReportsController : ControllerBase
{
    private const int DefaultDays = 30;

    private readonly IMediator _mediator;

    public PlatformReportsController(IMediator mediator) => _mediator = mediator;

    // days: 7 | 30 | 90 | 365 (verilmezse 30); başka değer 400 InvalidReportPeriod.
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary([FromQuery] int? days, CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetPlatformSummaryQuery { Days = days ?? DefaultDays }, cancellationToken));

    [HttpGet("companies/{id}/branches")]
    public async Task<IActionResult> GetCompanyBranches(int id, [FromQuery] int? days, CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetPlatformCompanyBranchesQuery { CompanyId = id, Days = days ?? DefaultDays }, cancellationToken));
}
