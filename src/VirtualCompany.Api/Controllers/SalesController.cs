using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Api.ProblemHandling;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.CustomerMemory;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/sales")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class SalesController : ControllerBase
{
    private readonly ICompanyContextAccessor _companyContextAccessor;
    private readonly ISalesOperationsService _salesOperations;
    private readonly ICustomerMemoryService _customerMemory;
    private readonly IRevenueForecastService _revenueForecastService;
    private readonly IConversionAnalyticsService _conversionAnalyticsService;
    private readonly IDealIntelligenceSignalRepository _dealIntelligenceSignals;
    private readonly ISalesLeadEmailEvidenceService _leadEmailEvidence;
    private readonly ISalesMeetingSchedulingService _meetingScheduling;

    public SalesController(
        ICompanyContextAccessor companyContextAccessor,
        ISalesOperationsService salesOperations,
        ICustomerMemoryService customerMemory,
        IRevenueForecastService revenueForecastService,
        IConversionAnalyticsService conversionAnalyticsService,
        IDealIntelligenceSignalRepository dealIntelligenceSignals,
        ISalesLeadEmailEvidenceService leadEmailEvidence,
        ISalesMeetingSchedulingService meetingScheduling)
    {
        _companyContextAccessor = companyContextAccessor;
        _salesOperations = salesOperations;
        _customerMemory = customerMemory;
        _revenueForecastService = revenueForecastService;
        _conversionAnalyticsService = conversionAnalyticsService;
        _dealIntelligenceSignals = dealIntelligenceSignals;
        _leadEmailEvidence = leadEmailEvidence;
        _meetingScheduling = meetingScheduling;
    }

    [HttpGet("dashboard")]
    public Task<SalesDashboardResponse> GetDashboardAsync(CancellationToken cancellationToken) =>
        _salesOperations.GetDashboardAsync(CompanyId(), cancellationToken);

    [HttpGet("leads")]
    public Task<IReadOnlyList<SalesLeadSummaryResponse>> ListLeadsAsync(CancellationToken cancellationToken) =>
        _salesOperations.ListLeadsAsync(CompanyId(), cancellationToken);

    [HttpGet("leads/{id:guid}")]
    public async Task<ActionResult<SalesLeadDetailResponse>> GetLeadAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await _salesOperations.GetLeadAsync(CompanyId(), id, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("leads/{id:guid}/source-emails")]
    public Task<IReadOnlyList<SalesLeadSourceEmailResponse>> ListLeadSourceEmailsAsync(Guid id, CancellationToken cancellationToken) =>
        _leadEmailEvidence.ListAsync(CompanyId(), id, cancellationToken);
    [HttpGet("calendar-connections")]
    public Task<IReadOnlyList<SalesCalendarConnectionResponse>> ListCalendarConnectionsAsync(CancellationToken cancellationToken) =>
        _meetingScheduling.ListCalendarConnectionsAsync(CompanyId(), cancellationToken);

    [HttpPost("calendar-availability")]
    public async Task<ActionResult<SalesMeetingAvailabilityResponse>> GetCalendarAvailabilityAsync(
        [FromBody] SalesMeetingAvailabilityRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _meetingScheduling.GetAvailabilityAsync(CompanyId(), request, cancellationToken));
        }
        catch (SalesValidationException ex)
        {
            return ValidationProblem(ex.Errors);
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException or CalendarProviderException)
        {
            return Problem(title: "Calendar availability is unavailable.", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [HttpGet("leads/{id:guid}/meeting-invitations")]
    public Task<IReadOnlyList<SalesMeetingInvitationResponse>> ListMeetingInvitationsAsync(
        Guid id, CancellationToken cancellationToken) =>
        _meetingScheduling.ListForLeadAsync(CompanyId(), id, cancellationToken);

    [HttpPost("leads/{id:guid}/meeting-invitations")]
    public async Task<ActionResult<SalesMeetingInvitationResponse>> CreateMeetingInvitationAsync(
        Guid id,
        [FromBody] CreateSalesMeetingInvitationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _meetingScheduling.CreateForLeadAsync(
                CompanyId(), UserId(), id, request, cancellationToken);
            return CreatedAtAction(nameof(GetMeetingInvitationAsync), new { invitationId = result.Id }, result);
        }
        catch (SalesValidationException ex)
        {
            return ValidationProblem(ex.Errors);
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException or CalendarProviderException)
        {
            return Problem(title: "Meeting invitation could not be prepared.", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [HttpGet("meeting-invitations/{invitationId:guid}")]
    public async Task<ActionResult<SalesMeetingInvitationResponse>> GetMeetingInvitationAsync(
        Guid invitationId, CancellationToken cancellationToken)
    {
        var result = await _meetingScheduling.GetAsync(CompanyId(), invitationId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }
    [HttpGet("meeting-invitations/{invitationId:guid}/changes")]
    public Task<IReadOnlyList<SalesMeetingChangeRequestResponse>> ListMeetingChangesAsync(
        Guid invitationId, CancellationToken cancellationToken) =>
        _meetingScheduling.ListChangesAsync(CompanyId(), invitationId, cancellationToken);

    [HttpPost("meeting-invitations/{invitationId:guid}/reschedule")]
    public async Task<ActionResult<SalesMeetingChangeRequestResponse>> RequestMeetingRescheduleAsync(
        Guid invitationId,
        [FromBody] CreateSalesMeetingRescheduleRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _meetingScheduling.RequestRescheduleAsync(
                CompanyId(), UserId(), invitationId, request, cancellationToken));
        }
        catch (SalesValidationException ex)
        {
            return ValidationProblem(ex.Errors);
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            return Problem(title: "Meeting reschedule could not be prepared.", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [HttpPost("meeting-invitations/{invitationId:guid}/cancel")]
    public async Task<ActionResult<SalesMeetingChangeRequestResponse>> RequestMeetingCancellationAsync(
        Guid invitationId, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _meetingScheduling.RequestCancellationAsync(
                CompanyId(), UserId(), invitationId, cancellationToken));
        }
        catch (SalesValidationException ex)
        {
            return ValidationProblem(ex.Errors);
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            return Problem(title: "Meeting cancellation could not be prepared.", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }
    [HttpPost("leads/{id:guid}/qualify")]
    public async Task<ActionResult<SalesLeadDetailResponse>> QualifyLeadAsync(Guid id, [FromBody] SalesActionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _salesOperations.QualifyLeadAsync(CompanyId(), UserId(), id, request, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (SalesValidationException ex)
        {
            return ValidationProblem(ex.Errors);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(title: "Lead could not be qualified.", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [HttpPut("leads/{id:guid}/qualification")]
    public async Task<ActionResult<SalesLeadDetailResponse>> UpdateLeadQualificationAsync(Guid id, [FromBody] UpdateLeadQualificationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _salesOperations.UpdateLeadQualificationAsync(CompanyId(), UserId(), id, request, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (SalesValidationException ex)
        {
            return ValidationProblem(ex.Errors);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(title: "Lead qualification could not be saved.", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [HttpPost("leads/{id:guid}/reject")]
    public async Task<ActionResult<SalesLeadDetailResponse>> RejectLeadAsync(Guid id, [FromBody] SalesActionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _salesOperations.RejectLeadAsync(CompanyId(), UserId(), id, request, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (SalesValidationException ex)
        {
            return ValidationProblem(ex.Errors);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(title: "Lead could not be rejected.", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [HttpPost("leads/{id:guid}/convert")]
    public async Task<ActionResult<SalesDealDetailResponse>> ConvertLeadAsync(Guid id, [FromBody] ConvertLeadRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _salesOperations.ConvertLeadAsync(CompanyId(), UserId(), id, request, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (SalesValidationException ex)
        {
            return ValidationProblem(ex.Errors);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(title: "Lead could not be converted.", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [HttpGet("pipeline")]
    public Task<SalesPipelineResponse> GetPipelineAsync(CancellationToken cancellationToken) =>
        _salesOperations.GetPipelineAsync(CompanyId(), cancellationToken);

    [HttpGet("forecast")]
    public async Task<ActionResult<RevenueForecastSnapshotDto>> GetRevenueForecastAsync(CancellationToken cancellationToken)
    {
        var companyId = CompanyId();
        var latest = await _revenueForecastService.GetLatestForecastAsync(companyId, cancellationToken);
        return latest is not null
            ? Ok(latest)
            : Ok(await _revenueForecastService.CalculateAndPersistForecastAsync(companyId, DateTime.UtcNow, cancellationToken));
    }

    [HttpGet("analytics")]
    public Task<SalesAnalyticsDashboardDto> GetAnalyticsAsync(CancellationToken cancellationToken) =>
        _conversionAnalyticsService.GetDashboardAnalyticsAsync(CompanyId(), cancellationToken);

    [HttpGet("analytics/campaigns/{campaignId:guid}")]
    public async Task<ActionResult<CampaignPerformanceSummaryDto>> GetCampaignPerformanceAsync(Guid campaignId, CancellationToken cancellationToken)
    {
        var result = await _conversionAnalyticsService.GetCampaignPerformanceAsync(CompanyId(), campaignId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("analytics/variants")]
    public Task<IReadOnlyList<VariantPerformanceSummaryDto>> GetVariantPerformanceAsync(
        [FromQuery] Guid? campaignId,
        [FromQuery] Guid? sequenceId,
        [FromQuery] Guid? sequenceStepId,
        CancellationToken cancellationToken) =>
        _conversionAnalyticsService.GetVariantPerformanceAsync(CompanyId(), campaignId, sequenceId, sequenceStepId, cancellationToken);

    [HttpGet("deals/{id:guid}/intelligence-signals")]
    public Task<IReadOnlyList<DealIntelligenceSignalDto>> GetDealIntelligenceSignalsAsync(Guid id, CancellationToken cancellationToken) =>
        _dealIntelligenceSignals.ListLatestByDealAsync(CompanyId(), id, cancellationToken);

    [HttpGet("deals/{id:guid}")]
    public async Task<ActionResult<SalesDealDetailResponse>> GetDealAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await _salesOperations.GetDealAsync(CompanyId(), id, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("deals/{id:guid}/activities")]
    public Task<IReadOnlyList<SalesActivityResponse>> GetDealActivitiesAsync(Guid id, CancellationToken cancellationToken) =>
        _salesOperations.ListDealActivitiesAsync(CompanyId(), id, cancellationToken);

    [HttpGet("deals/{id:guid}/emails")]
    public Task<IReadOnlyList<SalesEmailTimelineResponse>> GetDealEmailsAsync(Guid id, CancellationToken cancellationToken) =>
        _salesOperations.ListDealEmailsAsync(CompanyId(), id, cancellationToken);

    [HttpGet("deals/{id:guid}/risk-score")]
    public async Task<ActionResult<DealRiskScoreDto>> GetDealRiskScoreAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await _revenueForecastService.GetLatestDealRiskScoreAsync(CompanyId(), id, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("recommendations")]
    public Task<IReadOnlyList<SalesRecommendationResponse>> GetRecommendationsAsync(CancellationToken cancellationToken) =>
        _salesOperations.ListRecommendationsAsync(CompanyId(), cancellationToken);

    [HttpGet("contacts/{id:guid}/profile")]
    public async Task<ActionResult<CustomerMemoryContext>> GetContactProfileAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await _customerMemory.GetContextAsync(CompanyId(), id, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("recommendations/detect")]
    public Task<IReadOnlyList<SalesRecommendationResponse>> DetectRecommendationsAsync(CancellationToken cancellationToken) =>
        _salesOperations.DetectFollowUpRecommendationsAsync(CompanyId(), UserId(), cancellationToken);

    [HttpPost("recommendations/{id:guid}/approve")]
    public async Task<ActionResult<SalesRecommendationResponse>> ApproveRecommendationAsync(Guid id, [FromBody] SalesActionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _salesOperations.ApproveRecommendationAsync(CompanyId(), UserId(), id, request, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (SalesValidationException ex)
        {
            return ValidationProblem(ex.Errors);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(title: "Recommendation could not be executed.", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [HttpPost("recommendations/{id:guid}/retry")]
    public async Task<ActionResult<SalesRecommendationResponse>> RetryRecommendationAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _salesOperations.RetryRecommendationAsync(CompanyId(), UserId(), id, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (SalesValidationException ex)
        {
            return ValidationProblem(ex.Errors);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(title: "Recommendation retry could not be started.", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [HttpGet("automation-policy")]
    public Task<SalesAutomationPolicyResponse> GetAutomationPolicyAsync(CancellationToken cancellationToken) =>
        _salesOperations.GetAutomationPolicyAsync(CompanyId(), cancellationToken);

    [HttpPut("automation-policy")]
    public Task<SalesAutomationPolicyResponse> UpdateAutomationPolicyAsync([FromBody] UpdateSalesAutomationPolicyRequest request, CancellationToken cancellationToken) =>
        _salesOperations.UpdateAutomationPolicyAsync(CompanyId(), UserId(), request, cancellationToken);

    [HttpGet("deals/{id:guid}/finance-handoff")]
    public async Task<ActionResult<SalesFinanceHandoffResponse>> GetFinanceHandoffAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await _salesOperations.GetFinanceHandoffAsync(CompanyId(), id, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("deals/{id:guid}/finance-handoff/approve")]
    public async Task<ActionResult<SalesFinanceHandoffResponse>> ApproveFinanceHandoffAsync(Guid id, [FromBody] SalesActionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _salesOperations.ApproveFinanceHandoffAsync(CompanyId(), UserId(), id, request, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (SalesValidationException ex)
        {
            return ValidationProblem(ex.Errors);
        }
    }

    [HttpPost("deals/{id:guid}/finance-handoff/retry")]
    public async Task<ActionResult<SalesFinanceHandoffResponse>> RetryFinanceHandoffAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _salesOperations.RetryFinanceHandoffAsync(CompanyId(), UserId(), id, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (SalesValidationException ex) { return ValidationProblem(ex.Errors); }
    }

    [HttpPost("deals/{id:guid}/stage")]
    [HttpPost("deals/{id:guid}/stage-change")]
    public async Task<ActionResult<SalesDealDetailResponse>> ChangeDealStageAsync(Guid id, [FromBody] ChangeDealStageRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _salesOperations.ChangeDealStageAsync(CompanyId(), UserId(), id, request, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (SalesValidationException ex)
        {
            return ValidationProblem(ex.Errors);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(title: "Deal stage could not be changed.", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [HttpPost("deals/{id:guid}/won")]
    [HttpPost("deals/{id:guid}/mark-won")]
    public async Task<ActionResult<SalesDealDetailResponse>> MarkWonAsync(Guid id, [FromBody] SalesActionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _salesOperations.MarkDealWonAsync(CompanyId(), UserId(), id, request, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (SalesValidationException ex)
        {
            return ValidationProblem(ex.Errors);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(title: "Deal could not be marked won.", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [HttpPost("deals/{id:guid}/lost")]
    [HttpPost("deals/{id:guid}/mark-lost")]
    public async Task<ActionResult<SalesDealDetailResponse>> MarkLostAsync(Guid id, [FromBody] SalesActionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _salesOperations.MarkDealLostAsync(CompanyId(), UserId(), id, request, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (SalesValidationException ex)
        {
            return ValidationProblem(ex.Errors);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(title: "Deal could not be marked lost.", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [HttpPost("email/process")]
    public async Task<ActionResult<ProcessSalesEmailResponse>> ProcessEmailAsync([FromBody] ProcessSalesEmailRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _salesOperations.ProcessEmailAsync(CompanyId(), UserId(), request, cancellationToken));
        }
        catch (SalesValidationException ex)
        {
            return ValidationProblem(ex.Errors);
        }
    }

    private Guid CompanyId() =>
        _companyContextAccessor.CompanyId is { } companyId && companyId != Guid.Empty ? companyId : throw new UnauthorizedAccessException("A resolved company is required.");

    private Guid UserId() =>
        _companyContextAccessor.UserId is { } userId && userId != Guid.Empty ? userId : throw new UnauthorizedAccessException("A resolved user is required.");

    private ActionResult ValidationProblem(IReadOnlyDictionary<string, string[]> errors) =>
        base.ValidationProblem(StableProblemDetails.CreateValidation(HttpContext, errors, ApiProblemCodes.SalesRequestInvalid));
}
