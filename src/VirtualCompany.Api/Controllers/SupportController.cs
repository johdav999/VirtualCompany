using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Support;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/support")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class SupportController : ControllerBase
{
    private readonly ICompanyContextAccessor _companyContextAccessor;
    private readonly ISupportCaseService _cases;
    private readonly ISupportMailboxIngestionService _mailbox;
    private readonly ISupportContextResolutionService _context;
    private readonly ISupportTriageService _triage;
    private readonly ISupportReplyDraftService _drafts;
    private readonly ISupportToolActionService _tools;
    private readonly ISupportRefundWorkflowService _refunds;
    private readonly ISupportSlaMonitor _sla;
    private readonly ISupportKnowledgeGapService _knowledgeGaps;
    private readonly ISupportAnalyticsService _analytics;

    public SupportController(
        ICompanyContextAccessor companyContextAccessor,
        ISupportCaseService cases,
        ISupportMailboxIngestionService mailbox,
        ISupportContextResolutionService context,
        ISupportTriageService triage,
        ISupportReplyDraftService drafts,
        ISupportToolActionService tools,
        ISupportRefundWorkflowService refunds,
        ISupportSlaMonitor sla,
        ISupportKnowledgeGapService knowledgeGaps,
        ISupportAnalyticsService analytics)
    {
        _companyContextAccessor = companyContextAccessor;
        _cases = cases;
        _mailbox = mailbox;
        _context = context;
        _triage = triage;
        _drafts = drafts;
        _tools = tools;
        _refunds = refunds;
        _sla = sla;
        _knowledgeGaps = knowledgeGaps;
        _analytics = analytics;
    }

    [HttpGet("cases")]
    public Task<SupportCaseListResponse> ListCasesAsync([FromQuery] SupportCaseListQuery query, CancellationToken cancellationToken) =>
        _cases.ListCasesAsync(CompanyId(), query, cancellationToken);

    [HttpPost("cases")]
    public async Task<ActionResult<SupportCaseDetailResponse>> CreateCaseAsync([FromBody] CreateSupportCaseRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _cases.CreateCaseAsync(CompanyId(), UserId(), request, cancellationToken));
        }
        catch (SupportValidationException ex)
        {
            return ValidationProblem(ex.Errors);
        }
    }

    [HttpGet("cases/{id:guid}")]
    public async Task<ActionResult<SupportCaseDetailResponse>> GetCaseAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await _cases.GetCaseAsync(CompanyId(), id, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("cases/{id:guid}/notes")]
    public Task<ActionResult<SupportCaseDetailResponse>> AddNoteAsync(Guid id, [FromBody] AddSupportInternalNoteRequest request, CancellationToken cancellationToken) =>
        ExecuteCaseActionAsync(() => _cases.AddInternalNoteAsync(CompanyId(), UserId(), id, request, cancellationToken));

    [HttpPost("cases/{id:guid}/status")]
    public Task<ActionResult<SupportCaseDetailResponse>> ChangeStatusAsync(Guid id, [FromBody] ChangeSupportStatusRequest request, CancellationToken cancellationToken) =>
        ExecuteCaseActionAsync(() => _cases.ChangeStatusAsync(CompanyId(), UserId(), id, request, cancellationToken));

    [HttpPost("cases/{id:guid}/priority")]
    public Task<ActionResult<SupportCaseDetailResponse>> ChangePriorityAsync(Guid id, [FromBody] ChangeSupportPriorityRequest request, CancellationToken cancellationToken) =>
        ExecuteCaseActionAsync(() => _cases.ChangePriorityAsync(CompanyId(), UserId(), id, request, cancellationToken));

    [HttpPost("cases/{id:guid}/category")]
    public Task<ActionResult<SupportCaseDetailResponse>> ChangeCategoryAsync(Guid id, [FromBody] ChangeSupportCategoryRequest request, CancellationToken cancellationToken) =>
        ExecuteCaseActionAsync(() => _cases.ChangeCategoryAsync(CompanyId(), UserId(), id, request, cancellationToken));

    [HttpPost("cases/{id:guid}/assign")]
    public Task<ActionResult<SupportCaseDetailResponse>> AssignAsync(Guid id, [FromBody] AssignSupportCaseRequest request, CancellationToken cancellationToken) =>
        ExecuteCaseActionAsync(() => _cases.AssignAsync(CompanyId(), UserId(), id, request, cancellationToken));

    [HttpPost("cases/{id:guid}/resolve")]
    public Task<ActionResult<SupportCaseDetailResponse>> ResolveAsync(Guid id, [FromBody] ResolveSupportCaseRequest request, CancellationToken cancellationToken) =>
        ExecuteCaseActionAsync(() => _cases.ResolveAsync(CompanyId(), UserId(), id, request, cancellationToken));

    [HttpPost("cases/{id:guid}/reopen")]
    public Task<ActionResult<SupportCaseDetailResponse>> ReopenAsync(Guid id, [FromBody] SupportActionRequest request, CancellationToken cancellationToken) =>
        ExecuteCaseActionAsync(() => _cases.ReopenAsync(CompanyId(), UserId(), id, request, cancellationToken));

    [HttpPost("cases/{id:guid}/close")]
    public Task<ActionResult<SupportCaseDetailResponse>> CloseAsync(Guid id, [FromBody] SupportActionRequest request, CancellationToken cancellationToken) =>
        ExecuteCaseActionAsync(() => _cases.CloseAsync(CompanyId(), UserId(), id, request, cancellationToken));

    [HttpPost("mailbox/messages")]
    public async Task<ActionResult<SupportMailboxIngestionResult>> IngestMailboxMessageAsync([FromBody] SupportMailboxMessageInput request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _mailbox.IngestMessageAsync(CompanyId(), request, cancellationToken));
        }
        catch (SupportValidationException ex)
        {
            return ValidationProblem(ex.Errors);
        }
    }

    [HttpPost("cases/{id:guid}/resolve-context")]
    public async Task<ActionResult<SupportCaseContextSummary>> ResolveContextAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _context.ResolveAsync(CompanyId(), id, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return Problem(title: "Support context could not be resolved.", detail: ex.Message, statusCode: StatusCodes.Status404NotFound);
        }
    }

    [HttpPost("cases/{id:guid}/triage")]
    public async Task<ActionResult<SupportTriageResult>> TriageAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await _triage.TriageAsync(CompanyId(), UserId(), id, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("cases/{id:guid}/reply-drafts/generate")]
    public async Task<ActionResult<SupportReplyDraftDto>> GenerateDraftAsync(Guid id, [FromBody] GenerateSupportReplyDraftRequest request, CancellationToken cancellationToken)
    {
        var result = await _drafts.GenerateDraftAsync(CompanyId(), UserId(), id, request, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("cases/{id:guid}/reply-drafts")]
    public Task<IReadOnlyList<SupportReplyDraftDto>> ListDraftsAsync(Guid id, CancellationToken cancellationToken) =>
        _drafts.ListDraftsAsync(CompanyId(), id, cancellationToken);

    [HttpPut("reply-drafts/{draftId:guid}")]
    public async Task<ActionResult<SupportReplyDraftDto>> EditDraftAsync(Guid draftId, [FromBody] EditSupportReplyDraftRequest request, CancellationToken cancellationToken)
    {
        var result = await _drafts.EditDraftAsync(CompanyId(), UserId(), draftId, request, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("reply-drafts/{draftId:guid}/approve")]
    public async Task<ActionResult<SupportReplyDraftDto>> ApproveDraftAsync(Guid draftId, [FromBody] SupportActionRequest request, CancellationToken cancellationToken)
    {
        var result = await _drafts.ApproveDraftAsync(CompanyId(), UserId(), draftId, request, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("reply-drafts/{draftId:guid}/reject")]
    public async Task<ActionResult<SupportReplyDraftDto>> RejectDraftAsync(Guid draftId, [FromBody] SupportActionRequest request, CancellationToken cancellationToken)
    {
        var result = await _drafts.RejectDraftAsync(CompanyId(), UserId(), draftId, request, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("reply-drafts/{draftId:guid}/send")]
    public Task<ActionResult<SupportCaseDetailResponse>> SendDraftAsync(Guid draftId, [FromBody] SendSupportReplyDraftRequest request, CancellationToken cancellationToken) =>
        ExecuteCaseActionAsync(() => _drafts.SendDraftAsync(CompanyId(), UserId(), draftId, request, cancellationToken));

    [HttpPost("tools/execute")]
    public Task<SupportToolActionResult> ExecuteToolAsync([FromBody] SupportToolActionRequest request, CancellationToken cancellationToken) =>
        _tools.ExecuteAsync(CompanyId(), request.Payload.TryGetValue("agentId", out var agentId) && Guid.TryParse(agentId, out var parsedAgentId) ? parsedAgentId : Guid.Empty, request, cancellationToken);

    [HttpPost("cases/{id:guid}/refund-requests")]
    public async Task<ActionResult<SupportRefundRequestDto>> RequestRefundAsync(Guid id, [FromBody] CreateSupportRefundRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _refunds.RequestRefundAsync(CompanyId(), UserId(), id, request, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (ArgumentException ex)
        {
            return Problem(title: "Refund request could not be created.", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [HttpPost("sla/run")]
    public Task<SupportSlaMonitorResult> RunSlaAsync(CancellationToken cancellationToken) =>
        _sla.RunAsync(DateTime.UtcNow, cancellationToken);

    [HttpGet("knowledge-gaps")]
    public Task<IReadOnlyList<SupportKnowledgeGapDto>> ListKnowledgeGapsAsync([FromQuery] string? status, CancellationToken cancellationToken) =>
        _knowledgeGaps.ListAsync(CompanyId(), status, cancellationToken);

    [HttpPost("knowledge-gaps")]
    public Task<SupportKnowledgeGapDto> CreateKnowledgeGapAsync([FromBody] CreateSupportKnowledgeGapRequest request, CancellationToken cancellationToken) =>
        _knowledgeGaps.CreateOrIncrementAsync(CompanyId(), request, cancellationToken);

    [HttpPost("knowledge-gaps/{id:guid}/documentation-task")]
    public async Task<ActionResult<SupportKnowledgeGapDto>> CreateDocumentationTaskAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await _knowledgeGaps.CreateDocumentationTaskAsync(CompanyId(), UserId(), id, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("analytics")]
    public Task<SupportAnalyticsDashboardResponse> GetAnalyticsAsync(CancellationToken cancellationToken) =>
        _analytics.GetDashboardAsync(CompanyId(), cancellationToken);

    private async Task<ActionResult<SupportCaseDetailResponse>> ExecuteCaseActionAsync(Func<Task<SupportCaseDetailResponse?>> action)
    {
        try
        {
            var result = await action();
            return result is null ? NotFound() : Ok(result);
        }
        catch (SupportValidationException ex)
        {
            return ValidationProblem(ex.Errors);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(title: "Support action could not be completed.", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (ArgumentException ex)
        {
            return Problem(title: "Support action could not be completed.", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private Guid CompanyId() =>
        _companyContextAccessor.CompanyId is { } companyId && companyId != Guid.Empty ? companyId : throw new UnauthorizedAccessException("A resolved company is required.");

    private Guid UserId() =>
        _companyContextAccessor.UserId is { } userId && userId != Guid.Empty ? userId : throw new UnauthorizedAccessException("A resolved user is required.");

    private ActionResult ValidationProblem(IReadOnlyDictionary<string, string[]> errors) =>
        ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Validation failed."
        });
}
