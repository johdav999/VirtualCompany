using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;

public sealed partial class InternalFinanceController
{
    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/provider-switches")]
    public async Task<ActionResult<IReadOnlyList<AccountingProviderSwitchDto>>> ListAccountingProviderSwitchesAsync(
        Guid companyId,
        [FromQuery] string? status,
        [FromQuery] int limit,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _accountingProviderSwitchService.ListAsync(
            new ListAccountingProviderSwitchesQuery(companyId, status, limit <= 0 ? 50 : limit), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/provider-switches/{switchId:guid}")]
    public async Task<ActionResult<AccountingProviderSwitchDto>> GetAccountingProviderSwitchAsync(
        Guid companyId,
        Guid switchId,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _accountingProviderSwitchService.GetAsync(
            new GetAccountingProviderSwitchQuery(companyId, switchId), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/provider-switches")]
    public async Task<ActionResult<AccountingProviderSwitchDto>> CreateAccountingProviderSwitchAsync(
        Guid companyId,
        [FromBody] CreateAccountingProviderSwitchRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingProviderSwitchService.CreateAsync(
            new CreateAccountingProviderSwitchCommand(
                companyId, request.SourceKind, request.SourceProviderKey, request.TargetKind,
                request.TargetProviderKey, request.EffectiveFiscalPeriodId, request.MigrationStrategy,
                request.Reason, request.ResponsibleUserId, request.ResponsibleAgentId,
                ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required."),
                ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPut("accounting/provider-switches/{switchId:guid}/plan")]
    public async Task<ActionResult<AccountingProviderSwitchDto>> UpdateAccountingProviderSwitchPlanAsync(
        Guid companyId,
        Guid switchId,
        [FromBody] UpdateAccountingProviderSwitchPlanRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingProviderSwitchService.UpdatePlanAsync(
            new UpdateAccountingProviderSwitchPlanCommand(
                companyId, switchId, request.SourceKind, request.SourceProviderKey, request.TargetKind,
                request.TargetProviderKey, request.EffectiveFiscalPeriodId, request.MigrationStrategy,
                request.Reason, request.ResponsibleUserId, request.ResponsibleAgentId, request.ExpectedVersion,
                ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required."),
                ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/provider-switches/{switchId:guid}/cancel")]
    public async Task<ActionResult<AccountingProviderSwitchDto>> CancelAccountingProviderSwitchAsync(
        Guid companyId,
        Guid switchId,
        [FromBody] CancelAccountingProviderSwitchRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingProviderSwitchService.CancelAsync(
            new CancelAccountingProviderSwitchCommand(
                companyId, switchId, request.Reason, request.ExpectedVersion,
                ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required."),
                ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/provider-switches/{switchId:guid}/allowed-actions")]
    public async Task<ActionResult<AccountingProviderSwitchAllowedActionsDto>> GetAccountingProviderSwitchAllowedActionsAsync(
        Guid companyId,
        Guid switchId,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _accountingProviderSwitchService.GetAllowedActionsAsync(
            new GetAccountingProviderSwitchAllowedActionsQuery(companyId, switchId), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/provider-switches/{switchId:guid}/guidance")]
    public async Task<ActionResult<AccountingProviderSwitchAgentBriefingDto>> GetAccountingProviderSwitchGuidanceAsync(
        Guid companyId, Guid switchId, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _accountingProviderSwitchAgentService.GetBriefingAsync(
            new GetAccountingProviderSwitchAgentBriefingQuery(companyId, switchId), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/provider-switches/{switchId:guid}/guidance/recommendation")]
    public async Task<ActionResult<AccountingProviderSwitchAgentRecommendationDto>> GetAccountingProviderSwitchRecommendationAsync(
        Guid companyId, Guid switchId, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _accountingProviderSwitchAgentService.RecommendAsync(
            new RecommendAccountingProviderSwitchActionQuery(companyId, switchId,
                AccountingProviderSwitchAgentToolIds.ExplainReadiness), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/provider-switches/{switchId:guid}/evidence/{view}")]
    public async Task<ActionResult<AccountingProviderSwitchAgentEvidenceDto>> GetAccountingProviderSwitchEvidenceAsync(
        Guid companyId, Guid switchId, string view, [FromQuery] int limit,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _accountingProviderSwitchAgentService.GetEvidenceAsync(
            new GetAccountingProviderSwitchAgentEvidenceQuery(companyId, switchId, view, limit <= 0 ? 20 : limit),
            cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/provider-switches/{switchId:guid}/assessments")]
    public async Task<ActionResult<AccountingProviderSwitchAssessmentDto>> StartAccountingProviderSwitchAssessmentAsync(
        Guid companyId,
        Guid switchId,
        [FromBody] StartAccountingProviderSwitchAssessmentRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingProviderSwitchAssessmentService.StartAsync(
            new StartAccountingProviderSwitchAssessmentCommand(companyId, switchId, request.ExpectedSwitchVersion,
                ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required."),
                ResolveCorrelationId(), request.IdempotencyKey), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/provider-switches/{switchId:guid}/assessments/{assessmentId:guid}/replay")]
    public async Task<ActionResult<AccountingProviderSwitchAssessmentDto>> ReplayAccountingProviderSwitchAssessmentAsync(
        Guid companyId,
        Guid switchId,
        Guid assessmentId,
        [FromBody] ReplayAccountingProviderSwitchAssessmentRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingProviderSwitchAssessmentService.ReplayAsync(
            new ReplayAccountingProviderSwitchAssessmentCommand(companyId, switchId, assessmentId,
                request.ExpectedSwitchVersion,
                ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required."),
                ResolveCorrelationId(), request.IdempotencyKey), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/provider-switches/{switchId:guid}/assessments/latest")]
    public async Task<ActionResult<AccountingProviderSwitchAssessmentDto>> GetLatestAccountingProviderSwitchAssessmentAsync(
        Guid companyId, Guid switchId, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _accountingProviderSwitchAssessmentService.GetAsync(
            new GetAccountingProviderSwitchAssessmentQuery(companyId, switchId), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/provider-switches/{switchId:guid}/assessments/{assessmentId:guid}")]
    public async Task<ActionResult<AccountingProviderSwitchAssessmentDto>> GetAccountingProviderSwitchAssessmentAsync(
        Guid companyId, Guid switchId, Guid assessmentId, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _accountingProviderSwitchAssessmentService.GetAsync(
            new GetAccountingProviderSwitchAssessmentQuery(companyId, switchId, assessmentId), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/provider-switches/{switchId:guid}/assessments/{assessmentId:guid}/capabilities")]
    public async Task<ActionResult<IReadOnlyList<AccountingProviderSwitchCapabilityDto>>> GetAccountingProviderSwitchCapabilitiesAsync(
        Guid companyId, Guid switchId, Guid assessmentId, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(async () => (IReadOnlyList<AccountingProviderSwitchCapabilityDto>)(await _accountingProviderSwitchAssessmentService.GetAsync(
            new GetAccountingProviderSwitchAssessmentQuery(companyId, switchId, assessmentId), cancellationToken)).Capabilities);

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/provider-switches/{switchId:guid}/assessments/{assessmentId:guid}/datasets")]
    public async Task<ActionResult<IReadOnlyList<AccountingProviderSwitchDatasetDto>>> GetAccountingProviderSwitchDatasetsAsync(
        Guid companyId, Guid switchId, Guid assessmentId, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(async () => (IReadOnlyList<AccountingProviderSwitchDatasetDto>)(await _accountingProviderSwitchAssessmentService.GetAsync(
            new GetAccountingProviderSwitchAssessmentQuery(companyId, switchId, assessmentId), cancellationToken)).Datasets);

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/provider-switches/{switchId:guid}/assessments/{assessmentId:guid}/gaps")]
    public async Task<ActionResult<IReadOnlyList<AccountingProviderSwitchGapDto>>> GetAccountingProviderSwitchGapsAsync(
        Guid companyId, Guid switchId, Guid assessmentId, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(async () => (IReadOnlyList<AccountingProviderSwitchGapDto>)(await _accountingProviderSwitchAssessmentService.GetAsync(
            new GetAccountingProviderSwitchAssessmentQuery(companyId, switchId, assessmentId), cancellationToken)).Gaps);

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/provider-switches/{switchId:guid}/assessments/{assessmentId:guid}/progress")]
    public async Task<ActionResult<AccountingProviderSwitchAssessmentProgressDto>> GetAccountingProviderSwitchAssessmentProgressAsync(
        Guid companyId, Guid switchId, Guid assessmentId, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(async () =>
        {
            var result = await _accountingProviderSwitchAssessmentService.GetAsync(
                new GetAccountingProviderSwitchAssessmentQuery(companyId, switchId, assessmentId), cancellationToken);
            return new AccountingProviderSwitchAssessmentProgressDto(result.Id, result.Status, result.CompletedWorkItems,
                result.TotalWorkItems, result.ProgressPercent, result.AttemptCount, result.NextAttemptUtc,
                result.FailureCode, result.FailureSummary, result.HasBlockingGaps, result.AllowedNextAction,
                result.AllowedNextActionExplanation);
        });

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/provider-switches/{switchId:guid}/staging")]
    public async Task<ActionResult<IReadOnlyList<AccountingProviderSwitchStagedRecordDto>>> ListAccountingProviderSwitchStagingAsync(
        Guid companyId, Guid switchId, [FromQuery] string? dataset, [FromQuery] string? disposition,
        [FromQuery] bool includeSuperseded, [FromQuery] int limit, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _accountingProviderSwitchStagingService.ListAsync(
            new ListAccountingProviderSwitchStagedRecordsQuery(companyId, switchId, dataset, disposition,
                includeSuperseded, limit <= 0 ? 200 : limit), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/provider-switches/{switchId:guid}/mappings")]
    public async Task<ActionResult<IReadOnlyList<AccountingProviderSwitchMappingDecisionDto>>> ListAccountingProviderSwitchMappingsAsync(
        Guid companyId, Guid switchId, [FromQuery] bool includeSuperseded, [FromQuery] int limit,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _accountingProviderSwitchStagingService.ListMappingsAsync(
            new ListAccountingProviderSwitchMappingsQuery(companyId, switchId, includeSuperseded,
                limit <= 0 ? 200 : limit), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/provider-switches/{switchId:guid}/staging")]
    public async Task<ActionResult<AccountingProviderSwitchStagedRecordDto>> StageAccountingProviderSwitchRecordAsync(
        Guid companyId, Guid switchId, [FromBody] StageAccountingProviderSwitchRecordRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingProviderSwitchStagingService.StageAsync(
            new StageAccountingProviderSwitchRecordCommand(companyId, switchId, request.ExtractionBatchId,
                request.Dataset, request.SourceIdentity, request.SourceVersion, request.ProviderModifiedUtc,
                request.SourceHash, request.NormalizedDataJson, request.EvidenceJson, request.FinancialAmount,
                request.Currency, request.InitialDisposition,
                ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required."),
                ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/provider-switches/{switchId:guid}/mappings/preview")]
    public async Task<ActionResult<AccountingProviderSwitchMappingDecisionDto>> PreviewAccountingProviderSwitchMappingAsync(
        Guid companyId, Guid switchId, [FromBody] PreviewAccountingProviderSwitchMappingRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingProviderSwitchStagingService.PreviewMappingAsync(
            new PreviewAccountingProviderSwitchMappingCommand(companyId, switchId, request.MappingType,
                request.SourceKey, request.ProposedTargetKey, request.SourceSemantic,
                request.AffectedStagedRecordIds, request.IsMaterial,
                ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required."),
                ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/provider-switches/{switchId:guid}/mappings/{mappingDecisionId:guid}/approval")]
    public async Task<ActionResult<AccountingProviderSwitchMappingDecisionDto>> RequestAccountingProviderSwitchMappingApprovalAsync(
        Guid companyId, Guid switchId, Guid mappingDecisionId,
        [FromBody] RequestAccountingProviderSwitchMappingApprovalRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingProviderSwitchStagingService.RequestMappingApprovalAsync(
            new RequestAccountingProviderSwitchMappingApprovalCommand(companyId, switchId, mappingDecisionId,
                request.ExpectedVersion,
                ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required."),
                ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPut("accounting/provider-switches/{switchId:guid}/staging/{stagedRecordId:guid}/disposition")]
    public async Task<ActionResult<AccountingProviderSwitchStagedRecordDto>> ResolveAccountingProviderSwitchDispositionAsync(
        Guid companyId, Guid switchId, Guid stagedRecordId,
        [FromBody] ResolveAccountingProviderSwitchDispositionRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingProviderSwitchStagingService.ResolveDispositionAsync(
            new ResolveAccountingProviderSwitchDispositionCommand(companyId, switchId, stagedRecordId,
                request.Disposition, request.Reason, request.MappingDecisionId, request.DuplicateOfStagedRecordId,
                request.ExpectedVersion,
                ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required."),
                ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/provider-switches/{switchId:guid}/staging/completeness")]
    public async Task<ActionResult<AccountingProviderSwitchCompletenessDto>> GetAccountingProviderSwitchCompletenessAsync(
        Guid companyId, Guid switchId, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _accountingProviderSwitchStagingService.GetCompletenessAsync(
            new GetAccountingProviderSwitchCompletenessQuery(companyId, switchId), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/provider-switches/{switchId:guid}/rehearsals")]
    public async Task<ActionResult<AccountingProviderSwitchRehearsalDto>> StartAccountingProviderSwitchRehearsalAsync(
        Guid companyId, Guid switchId, [FromBody] StartAccountingProviderSwitchRehearsalRequest request,
        CancellationToken cancellationToken) => await ExecuteWriteAsync(() =>
        _accountingProviderSwitchRehearsalService.StartAsync(new(companyId, switchId,
            request.ExpectedSwitchVersion, ResolveActorId() ?? throw new UnauthorizedAccessException(
                "A resolved company user is required."), ResolveCorrelationId(), request.IdempotencyKey), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/provider-switches/{switchId:guid}/rehearsals/{rehearsalId:guid}/replay")]
    public async Task<ActionResult<AccountingProviderSwitchRehearsalDto>> ReplayAccountingProviderSwitchRehearsalAsync(
        Guid companyId, Guid switchId, Guid rehearsalId,
        [FromBody] ReplayAccountingProviderSwitchRehearsalRequest request, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingProviderSwitchRehearsalService.ReplayAsync(new(companyId,
            switchId, rehearsalId, request.ExpectedSwitchVersion,
            ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required."),
            ResolveCorrelationId(), request.IdempotencyKey), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/provider-switches/{switchId:guid}/rehearsals/latest")]
    public async Task<ActionResult<AccountingProviderSwitchRehearsalDto>> GetLatestAccountingProviderSwitchRehearsalAsync(
        Guid companyId, Guid switchId, CancellationToken cancellationToken) => await ExecuteReadAsync(() =>
        _accountingProviderSwitchRehearsalService.GetAsync(new(companyId, switchId), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/provider-switches/{switchId:guid}/rehearsals/{rehearsalId:guid}")]
    public async Task<ActionResult<AccountingProviderSwitchRehearsalDto>> GetAccountingProviderSwitchRehearsalAsync(
        Guid companyId, Guid switchId, Guid rehearsalId, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _accountingProviderSwitchRehearsalService.GetAsync(
            new(companyId, switchId, rehearsalId), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/provider-switches/{switchId:guid}/rehearsals/{rehearsalId:guid}/progress")]
    public async Task<ActionResult<AccountingProviderSwitchRehearsalProgressDto>> GetAccountingProviderSwitchRehearsalProgressAsync(
        Guid companyId, Guid switchId, Guid rehearsalId, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(async () =>
        {
            var result = await _accountingProviderSwitchRehearsalService.GetAsync(
                new(companyId, switchId, rehearsalId), cancellationToken);
            return new AccountingProviderSwitchRehearsalProgressDto(result.Id, result.Status,
                result.CompletedWorkItems, result.TotalWorkItems, result.ProgressPercent, result.AttemptCount,
                result.NextAttemptUtc, result.FailureCode, result.FailureSummary, result.IsReadyForPlan,
                result.ReadinessExplanation);
        });

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/provider-switches/{switchId:guid}/rehearsals/{rehearsalId:guid}/checks")]
    public async Task<ActionResult<IReadOnlyList<AccountingProviderSwitchReconciliationCheckDto>>> GetAccountingProviderSwitchReconciliationChecksAsync(
        Guid companyId, Guid switchId, Guid rehearsalId, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(async () => (IReadOnlyList<AccountingProviderSwitchReconciliationCheckDto>)(
            await _accountingProviderSwitchRehearsalService.GetAsync(new(companyId, switchId, rehearsalId),
                cancellationToken)).Checks);

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/provider-switches/{switchId:guid}/rehearsals/{rehearsalId:guid}/datasets")]
    public async Task<ActionResult<IReadOnlyList<AccountingProviderSwitchRehearsalDatasetResultDto>>> GetAccountingProviderSwitchRehearsalDatasetsAsync(
        Guid companyId, Guid switchId, Guid rehearsalId, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(async () => (IReadOnlyList<AccountingProviderSwitchRehearsalDatasetResultDto>)(
            await _accountingProviderSwitchRehearsalService.GetAsync(new(companyId, switchId, rehearsalId),
                cancellationToken)).Datasets);

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/provider-switches/{switchId:guid}/rehearsals/{rehearsalId:guid}/checks/{checkId:guid}/evidence")]
    public async Task<ActionResult<AccountingProviderSwitchManualEvidenceDto>> RecordAccountingProviderSwitchManualEvidenceAsync(
        Guid companyId, Guid switchId, Guid rehearsalId, Guid checkId,
        [FromBody] RecordAccountingProviderSwitchManualEvidenceRequest request, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingProviderSwitchRehearsalService.RecordManualEvidenceAsync(
            new(companyId, switchId, rehearsalId, checkId, request.Explanation, request.EvidenceReference,
                request.ExpiresUtc, ResolveActorId() ?? throw new UnauthorizedAccessException(
                    "A resolved company user is required."), ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/provider-switches/{switchId:guid}/cutover-plans")]
    public async Task<ActionResult<AccountingProviderSwitchCutoverPlanDto>> GenerateAccountingProviderSwitchCutoverPlanAsync(
        Guid companyId, Guid switchId, [FromBody] GenerateAccountingProviderSwitchCutoverPlanRequest request,
        CancellationToken cancellationToken) => await ExecuteWriteAsync(() =>
        _accountingProviderSwitchRehearsalService.GeneratePlanAsync(new(companyId, switchId,
            request.RehearsalId, request.ExpectedSwitchVersion, request.FreezeStartsUtc, request.FreezeEndsUtc,
            request.RecoveryBoundary, request.ParticipantUserIds,
            ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required."),
            ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/provider-switches/{switchId:guid}/cutover-plans/{planId:guid}/approval")]
    public async Task<ActionResult<AccountingProviderSwitchCutoverPlanDto>> RequestAccountingProviderSwitchPlanApprovalAsync(
        Guid companyId, Guid switchId, Guid planId,
        [FromBody] RequestAccountingProviderSwitchPlanApprovalRequest request, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingProviderSwitchRehearsalService.RequestPlanApprovalAsync(
            new(companyId, switchId, planId, request.ExpectedSwitchVersion,
                ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required."),
                ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/provider-switches/{switchId:guid}/cutover-plans/readiness")]
    public async Task<ActionResult<AccountingProviderSwitchPlanReadinessDto>> GetAccountingProviderSwitchPlanReadinessAsync(
        Guid companyId, Guid switchId, [FromQuery] Guid? planId, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _accountingProviderSwitchRehearsalService.GetPlanReadinessAsync(
            new(companyId, switchId, planId), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/provider-switches/{switchId:guid}/preparation/readiness")]
    public async Task<ActionResult<AccountingProviderSwitchInternalReadinessDto>> GetAccountingProviderSwitchInternalReadinessAsync(
        Guid companyId, Guid switchId, [FromQuery] Guid? planId, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _accountingProviderSwitchPreparationService.GetReadinessAsync(
            new(companyId, switchId, planId), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/provider-switches/{switchId:guid}/preparations")]
    public async Task<ActionResult<AccountingProviderSwitchPreparationDto>> StartAccountingProviderSwitchPreparationAsync(
        Guid companyId, Guid switchId, [FromBody] StartAccountingProviderSwitchPreparationRequest request,
        CancellationToken cancellationToken) => await ExecuteWriteAsync(() =>
        _accountingProviderSwitchPreparationService.StartAsync(new(companyId, switchId, request.PlanId,
            request.ExpectedSwitchVersion,
            ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required."),
            request.IdempotencyKey, ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/provider-switches/{switchId:guid}/preparations/{preparationId:guid}/replay")]
    public async Task<ActionResult<AccountingProviderSwitchPreparationDto>> ReplayAccountingProviderSwitchPreparationAsync(
        Guid companyId, Guid switchId, Guid preparationId, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingProviderSwitchPreparationService.ReplayAsync(new(companyId,
            switchId, preparationId,
            ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required."),
            ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/provider-switches/{switchId:guid}/preparations/latest")]
    public async Task<ActionResult<AccountingProviderSwitchPreparationDto>> GetLatestAccountingProviderSwitchPreparationAsync(
        Guid companyId, Guid switchId, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _accountingProviderSwitchPreparationService.GetAsync(
            new(companyId, switchId), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/provider-switches/{switchId:guid}/preparations/{preparationId:guid}")]
    public async Task<ActionResult<AccountingProviderSwitchPreparationDto>> GetAccountingProviderSwitchPreparationAsync(
        Guid companyId, Guid switchId, Guid preparationId, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _accountingProviderSwitchPreparationService.GetAsync(
            new(companyId, switchId, preparationId), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/provider-switches/{switchId:guid}/preparation/candidates")]
    public async Task<ActionResult<IReadOnlyList<AccountingProviderSwitchNativeCandidateDto>>> ListAccountingProviderSwitchNativeCandidatesAsync(
        Guid companyId, Guid switchId, [FromQuery] Guid? preparationId, [FromQuery] string? candidateKind,
        [FromQuery] string? status, [FromQuery] int limit, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _accountingProviderSwitchPreparationService.ListCandidatesAsync(
            new(companyId, switchId, preparationId, candidateKind, status, limit <= 0 ? 500 : limit),
            cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/provider-switches/{switchId:guid}/target-transfer-batches")]
    public async Task<ActionResult<AccountingProviderSwitchTargetTransferBatchDto>> StartAccountingProviderSwitchTargetTransferAsync(
        Guid companyId, Guid switchId, [FromBody] StartAccountingProviderSwitchTargetTransferRequest request,
        CancellationToken cancellationToken) => await ExecuteWriteAsync(() =>
        _accountingProviderSwitchTargetTransferService.StartAsync(new(companyId, switchId, request.PlanId,
            request.ExpectedSwitchVersion,
            ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required."),
            request.IdempotencyKey, ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/provider-switches/{switchId:guid}/target-transfer-batches/{batchId:guid}/replay")]
    public async Task<ActionResult<AccountingProviderSwitchTargetTransferBatchDto>> ReplayAccountingProviderSwitchTargetTransferAsync(
        Guid companyId, Guid switchId, Guid batchId,
        [FromBody] ReplayAccountingProviderSwitchTargetTransferRequest request, CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingProviderSwitchTargetTransferService.ReplayAsync(new(companyId,
            switchId, batchId, request.ExpectedBatchVersion,
            ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required."),
            ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/provider-switches/{switchId:guid}/target-transfer-batches/latest")]
    public async Task<ActionResult<AccountingProviderSwitchTargetTransferBatchDto>> GetLatestAccountingProviderSwitchTargetTransferAsync(
        Guid companyId, Guid switchId, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _accountingProviderSwitchTargetTransferService.GetAsync(
            new(companyId, switchId), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/provider-switches/{switchId:guid}/target-transfer-batches/{batchId:guid}")]
    public async Task<ActionResult<AccountingProviderSwitchTargetTransferBatchDto>> GetAccountingProviderSwitchTargetTransferAsync(
        Guid companyId, Guid switchId, Guid batchId, CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _accountingProviderSwitchTargetTransferService.GetAsync(
            new(companyId, switchId, batchId), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/provider-switches/{switchId:guid}/target-transfer-batches/{batchId:guid}/items/{itemId:guid}/reconcile")]
    public async Task<ActionResult<AccountingProviderSwitchTargetTransferItemDto>> ReconcileAccountingProviderSwitchTargetTransferItemAsync(
        Guid companyId, Guid switchId, Guid batchId, Guid itemId,
        [FromBody] ReconcileAccountingProviderSwitchTargetTransferItemRequest request,
        CancellationToken cancellationToken) => await ExecuteWriteAsync(() =>
        _accountingProviderSwitchTargetTransferService.ReconcileAsync(new(companyId, switchId, batchId, itemId,
            request.ProviderConfirmedSuccess, request.ProviderExternalId, request.Summary, request.ExpectedItemVersion,
            ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required."),
            ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/authority")]
    public async Task<ActionResult<AccountingAuthorityReadModel>> GetAccountingAuthorityAsync(
        Guid companyId,
        [FromQuery] DateOnly? asOf,
        [FromQuery] int exportLimit,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _accountingAuthorityService.GetAsync(
            new GetAccountingAuthorityQuery(companyId, asOf, exportLimit <= 0 ? 50 : exportLimit), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/authority/preview")]
    public async Task<ActionResult<AccountingAuthorityChangePreview>> PreviewAccountingAuthorityChangeAsync(
        Guid companyId,
        [FromBody] PreviewAccountingAuthorityChangeRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _accountingAuthorityService.PreviewChangeAsync(
            new PreviewAccountingAuthorityChangeQuery(
                companyId, request.EffectiveFiscalPeriodId, request.TargetAuthority, request.ProviderKey),
            cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/authority/change")]
    public async Task<ActionResult<AccountingAuthorityReadModel>> StartAccountingAuthorityChangeAsync(
        Guid companyId,
        [FromBody] StartAccountingAuthorityChangeRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingAuthorityService.StartChangeAsync(
            new StartAccountingAuthorityChangeCommand(
                companyId, request.EffectiveFiscalPeriodId, request.TargetAuthority, request.ProviderKey,
                request.Reason, request.PreviewToken, request.ExpectedCurrentVersion,
                ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required."),
                ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPut("accounting/authority/{authorityPeriodId:guid}/cutover-validation")]
    public async Task<ActionResult<AccountingAuthorityReadModel>> RecordAccountingCutoverValidationAsync(
        Guid companyId,
        Guid authorityPeriodId,
        [FromBody] RecordAccountingCutoverValidationRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingAuthorityService.RecordCutoverValidationAsync(
            new RecordAccountingCutoverValidationCommand(
                companyId, authorityPeriodId, request.OpeningBalancesReconciled,
                request.TrialBalanceReconciled, request.SourceMappingsReconciled, request.ConflictCount,
                request.Summary, request.ExpectedVersion,
                ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required."),
                ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/authority/{authorityPeriodId:guid}/complete")]
    public async Task<ActionResult<AccountingAuthorityReadModel>> CompleteAccountingAuthorityCutoverAsync(
        Guid companyId,
        Guid authorityPeriodId,
        [FromBody] CompleteAccountingAuthorityCutoverRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingAuthorityService.CompleteCutoverAsync(
            new CompleteAccountingAuthorityCutoverCommand(
                companyId, authorityPeriodId, request.ExpectedVersion,
                ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required."),
                ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/provider-exports")]
    public async Task<ActionResult<AccountingProviderExportDto>> QueueAccountingProviderExportAsync(
        Guid companyId,
        [FromBody] QueueAccountingProviderExportRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingProviderExportService.QueueAsync(
            new QueueAccountingProviderExportCommand(
                companyId, request.LedgerEntryId, request.ProviderKey,
                ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required."),
                ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/provider-exports/{exportId:guid}/reconcile")]
    public async Task<ActionResult<AccountingProviderExportDto>> ReconcileAccountingProviderExportAsync(
        Guid companyId,
        Guid exportId,
        [FromBody] ReconcileAccountingProviderExportRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingProviderExportService.ReconcileAsync(
            new ReconcileAccountingProviderExportCommand(
                companyId, exportId, request.ProviderConfirmedSuccess, request.ProviderExternalId,
                request.Summary, request.ExpectedVersion,
                ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required."),
                ResolveCorrelationId()), cancellationToken));
}

public sealed record PreviewAccountingAuthorityChangeRequest(
    Guid EffectiveFiscalPeriodId,
    string TargetAuthority,
    string? ProviderKey);
public sealed record StartAccountingAuthorityChangeRequest(
    Guid EffectiveFiscalPeriodId,
    string TargetAuthority,
    string? ProviderKey,
    string Reason,
    string PreviewToken,
    long ExpectedCurrentVersion);
public sealed record RecordAccountingCutoverValidationRequest(
    bool OpeningBalancesReconciled,
    bool TrialBalanceReconciled,
    bool SourceMappingsReconciled,
    int ConflictCount,
    string Summary,
    long ExpectedVersion);
public sealed record CompleteAccountingAuthorityCutoverRequest(long ExpectedVersion);
public sealed record QueueAccountingProviderExportRequest(Guid LedgerEntryId, string ProviderKey);
public sealed record ReconcileAccountingProviderExportRequest(
    bool ProviderConfirmedSuccess,
    string? ProviderExternalId,
    string Summary,
    long ExpectedVersion);
public sealed record CreateAccountingProviderSwitchRequest(
    string SourceKind,
    string? SourceProviderKey,
    string TargetKind,
    string? TargetProviderKey,
    Guid EffectiveFiscalPeriodId,
    string MigrationStrategy,
    string Reason,
    Guid ResponsibleUserId,
    Guid? ResponsibleAgentId);
public sealed record UpdateAccountingProviderSwitchPlanRequest(
    string SourceKind,
    string? SourceProviderKey,
    string TargetKind,
    string? TargetProviderKey,
    Guid EffectiveFiscalPeriodId,
    string MigrationStrategy,
    string Reason,
    Guid ResponsibleUserId,
    Guid? ResponsibleAgentId,
    long ExpectedVersion);
public sealed record CancelAccountingProviderSwitchRequest(string Reason, long ExpectedVersion);
public sealed record StartAccountingProviderSwitchAssessmentRequest(long ExpectedSwitchVersion, string IdempotencyKey);
public sealed record ReplayAccountingProviderSwitchAssessmentRequest(long ExpectedSwitchVersion, string IdempotencyKey);
public sealed record StageAccountingProviderSwitchRecordRequest(Guid ExtractionBatchId, string Dataset,
    string SourceIdentity, string SourceVersion, DateTime? ProviderModifiedUtc, string SourceHash,
    string NormalizedDataJson, string EvidenceJson, decimal FinancialAmount, string? Currency,
    string InitialDisposition);
public sealed record PreviewAccountingProviderSwitchMappingRequest(string MappingType, string SourceKey,
    string? ProposedTargetKey, string? SourceSemantic, IReadOnlyList<Guid> AffectedStagedRecordIds,
    bool IsMaterial);
public sealed record RequestAccountingProviderSwitchMappingApprovalRequest(long ExpectedVersion);
public sealed record ResolveAccountingProviderSwitchDispositionRequest(string Disposition, string Reason,
    Guid? MappingDecisionId, Guid? DuplicateOfStagedRecordId, long ExpectedVersion);
public sealed record StartAccountingProviderSwitchRehearsalRequest(long ExpectedSwitchVersion, string IdempotencyKey);
public sealed record ReplayAccountingProviderSwitchRehearsalRequest(long ExpectedSwitchVersion, string IdempotencyKey);
public sealed record RecordAccountingProviderSwitchManualEvidenceRequest(string Explanation,
    string EvidenceReference, DateTime? ExpiresUtc);
public sealed record GenerateAccountingProviderSwitchCutoverPlanRequest(Guid RehearsalId,
    long ExpectedSwitchVersion, DateTime FreezeStartsUtc, DateTime FreezeEndsUtc, string RecoveryBoundary,
    IReadOnlyList<Guid> ParticipantUserIds);
public sealed record RequestAccountingProviderSwitchPlanApprovalRequest(long ExpectedSwitchVersion);
public sealed record StartAccountingProviderSwitchPreparationRequest(Guid PlanId,
    long ExpectedSwitchVersion, string IdempotencyKey);
public sealed record StartAccountingProviderSwitchTargetTransferRequest(Guid PlanId,
    long ExpectedSwitchVersion, string IdempotencyKey);
public sealed record ReplayAccountingProviderSwitchTargetTransferRequest(long ExpectedBatchVersion);
public sealed record ReconcileAccountingProviderSwitchTargetTransferItemRequest(bool ProviderConfirmedSuccess,
    string? ProviderExternalId, string Summary, long ExpectedItemVersion);
