using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using VirtualCompany.Shared;
using VirtualCompany.Web.Localization.Finance;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class AccountingConnectionsPage : FinancePageBase, IDisposable
{
    [Inject] private FinanceApiClient FinanceApiClient { get; set; } = default!;
    [Inject] private IStringLocalizer<FinanceResources> MigrationText { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "migrationId")]
    public Guid? MigrationId { get; set; }

    private AccountingAuthorityReadModelResponse? Authority { get; set; }
    private IReadOnlyList<AccountingPeriodResponse> AvailablePeriods { get; set; } = [];
    private IReadOnlyList<AccountingProviderSwitchResponse> Switches { get; set; } = [];
    private AccountingProviderSwitchResponse? SelectedSwitch { get; set; }
    private AccountingProviderSwitchAllowedActionsResponse? SwitchAllowedActions { get; set; }
    private AccountingProviderSwitchAssessmentResponse? Assessment { get; set; }
    private AccountingProviderSwitchCompletenessResponse? Completeness { get; set; }
    private IReadOnlyList<AccountingProviderSwitchMappingResponse> Mappings { get; set; } = [];
    private AccountingProviderSwitchRehearsalResponse? Rehearsal { get; set; }
    private AccountingProviderSwitchPlanReadinessResponse? PlanReadiness { get; set; }
    private AccountingProviderSwitchInternalReadinessResponse? InternalReadiness { get; set; }
    private AccountingProviderSwitchPreparationResponse? Preparation { get; set; }
    private AccountingProviderSwitchTargetTransferResponse? Transfer { get; set; }
    private AccountingProviderSwitchCutoverResponse? SwitchCutover { get; set; }
    private AccountingProviderSwitchMonitoringResponse? SwitchMonitoring { get; set; }
    private AccountingProviderSwitchOperationsResponse? SwitchOperations { get; set; }
    private AccountingMigrationGuidanceResponse? Guidance { get; set; }
    private AccountingMigrationRecommendationResponse? Recommendation { get; set; }
    private AccountingMigrationEvidenceResponse? AuditEvidence { get; set; }
    private AccountingMigrationEvidenceResponse? MonitoringEvidence { get; set; }
    private IReadOnlyList<string> PartialFailures { get; set; } = [];

    private bool IsActing { get; set; }
    private string MigrationTarget { get; set; } = "internal";
    private string MigrationStrategy { get; set; } = "opening_balances_and_open_items";
    private string MigrationReason { get; set; } = "Move the authoritative books at a reviewed monthly boundary.";
    private Guid EffectiveFiscalPeriodId { get; set; }
    private bool OpeningBalancesReconciled { get; set; }
    private bool TrialBalanceReconciled { get; set; }
    private bool SourceMappingsReconciled { get; set; }
    private int ConflictCount { get; set; }
    private string CutoverSummary { get; set; } = "Reviewed against the selected authority source.";
    private string? ActionMessage { get; set; }
    private string? ActionError { get; set; }
    private CancellationTokenSource? _loadCancellation;

    private bool CanManageAccounting => FinanceAccess.CanManageAccounting(AccessState.MembershipRole);
    private AccountingAuthorityPeriodResponse? CurrentCutover =>
        Authority?.Periods.FirstOrDefault(x => x.Authority == "migration" && !x.CompletedUtc.HasValue);
    private string CurrentAuthorityLabel => Authority?.CurrentPeriod?.AuthorityLabel ?? MigrationText["NotConfigured"];
    private string CurrentEffectivePeriod => Authority?.CurrentPeriod is null
        ? MigrationText["NotAvailable"]
        : MigrationText["FromPeriodOnward", Authority.CurrentPeriod.EffectiveFrom.ToString("yyyy-MM")];

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        if (AccessState.IsAllowed && AccessState.CompanyId is Guid companyId)
            await LoadAsync(companyId, _loadCancellation.Token);
    }

    private async Task LoadAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        ActionError = null;
        var authorityTask = FinanceApiClient.GetAccountingAuthorityAsync(companyId, cancellationToken: cancellationToken);
        var yearsTask = FinanceApiClient.GetAccountingFiscalYearsAsync(companyId, cancellationToken);
        var switchesTask = FinanceApiClient.GetAccountingProviderSwitchesAsync(companyId, cancellationToken: cancellationToken);
        try
        {
            await Task.WhenAll(authorityTask, yearsTask, switchesTask);
            Authority = await authorityTask;
            Switches = await switchesTask;
            AvailablePeriods = (await yearsTask).SelectMany(x => x.Periods)
                .Where(x => x.StartDate > (Authority?.CurrentPeriod?.EffectiveFrom ?? DateOnly.FromDateTime(DateTime.UtcNow)))
                .OrderBy(x => x.StartDate)
                .ToArray();
            EffectiveFiscalPeriodId = AvailablePeriods.FirstOrDefault()?.Id ?? Guid.Empty;
            InitializeMigrationTarget();
            SelectedSwitch = MigrationId.HasValue
                ? Switches.FirstOrDefault(x => x.Id == MigrationId.Value)
                : Switches.FirstOrDefault(x => x.Status is not ("completed" or "cancelled"));
            LoadLegacyCutoverForm();
            if (SelectedSwitch is not null)
                await LoadSwitchWorkspaceAsync(companyId, SelectedSwitch.Id, cancellationToken);
            else
                ClearSwitchWorkspace();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (FinanceApiException exception)
        {
            ActionError = exception.Message;
        }
    }

    private async Task LoadSwitchWorkspaceAsync(Guid companyId, Guid switchId, CancellationToken cancellationToken)
    {
        var failures = new List<string>();
        var allowedTask = LoadPieceAsync(MigrationText["AllowedActionsEvidence"],
            () => FinanceApiClient.GetAccountingProviderSwitchAllowedActionsAsync(companyId, switchId, cancellationToken),
            (AccountingProviderSwitchAllowedActionsResponse?)null, failures);
        var assessmentTask = LoadPieceAsync(MigrationText["AssessmentEvidence"],
            () => FinanceApiClient.GetLatestAccountingProviderSwitchAssessmentAsync(companyId, switchId, cancellationToken),
            (AccountingProviderSwitchAssessmentResponse?)null, failures);
        var completenessTask = LoadPieceAsync(MigrationText["MappingEvidence"],
            () => FinanceApiClient.GetAccountingProviderSwitchCompletenessAsync(companyId, switchId, cancellationToken),
            (AccountingProviderSwitchCompletenessResponse?)null, failures);
        var mappingsTask = LoadPieceAsync(MigrationText["MappingEvidence"],
            () => FinanceApiClient.GetAccountingProviderSwitchMappingsAsync(companyId, switchId, cancellationToken: cancellationToken),
            (IReadOnlyList<AccountingProviderSwitchMappingResponse>)[], failures);
        var rehearsalTask = LoadPieceAsync(MigrationText["RehearsalEvidence"],
            () => FinanceApiClient.GetLatestAccountingProviderSwitchRehearsalAsync(companyId, switchId, cancellationToken),
            (AccountingProviderSwitchRehearsalResponse?)null, failures);
        var planTask = LoadPieceAsync(MigrationText["ApprovalEvidence"],
            () => FinanceApiClient.GetAccountingProviderSwitchPlanReadinessAsync(companyId, switchId, cancellationToken),
            (AccountingProviderSwitchPlanReadinessResponse?)null, failures);
        var cutoverTask = LoadPieceAsync(MigrationText["CutoverEvidence"],
            () => FinanceApiClient.GetLatestAccountingProviderSwitchCutoverAsync(companyId, switchId, cancellationToken),
            (AccountingProviderSwitchCutoverResponse?)null, failures);
        var guidanceTask = LoadPieceAsync(MigrationText["LauraGuidanceEvidence"],
            () => FinanceApiClient.GetAccountingMigrationGuidanceAsync(companyId, switchId, cancellationToken),
            (AccountingMigrationGuidanceResponse?)null, failures);
        var recommendationTask = LoadPieceAsync(MigrationText["LauraGuidanceEvidence"],
            () => FinanceApiClient.GetAccountingMigrationRecommendationAsync(companyId, switchId, cancellationToken),
            (AccountingMigrationRecommendationResponse?)null, failures);
        var auditTask = LoadPieceAsync(MigrationText["AuditEvidence"],
            () => FinanceApiClient.GetAccountingMigrationEvidenceAsync(companyId, switchId, "audit", cancellationToken: cancellationToken),
            (AccountingMigrationEvidenceResponse?)null, failures);
        var monitoringTask = LoadPieceAsync(MigrationText["MonitoringEvidence"],
            () => FinanceApiClient.GetAccountingMigrationEvidenceAsync(companyId, switchId, "monitoring", cancellationToken: cancellationToken),
            (AccountingMigrationEvidenceResponse?)null, failures);
        var postActivationTask = LoadPieceAsync(MigrationText["MonitoringEvidence"],
            () => FinanceApiClient.GetAccountingProviderSwitchMonitoringAsync(companyId, switchId, cancellationToken),
            (AccountingProviderSwitchMonitoringResponse?)null, failures);
        var operationsTask = LoadPieceAsync(MigrationText["MonitoringEvidence"],
            () => FinanceApiClient.GetAccountingProviderSwitchOperationsAsync(companyId, cancellationToken),
            (AccountingProviderSwitchOperationsResponse?)null, failures);

        var isInternalTarget = SelectedSwitch?.Target.Kind == "internal";
        var readinessTask = isInternalTarget
            ? LoadPieceAsync(MigrationText["TargetReadinessEvidence"],
                () => FinanceApiClient.GetAccountingProviderSwitchInternalReadinessAsync(companyId, switchId, cancellationToken),
                (AccountingProviderSwitchInternalReadinessResponse?)null, failures)
            : Task.FromResult<AccountingProviderSwitchInternalReadinessResponse?>(null);
        var preparationTask = isInternalTarget
            ? LoadPieceAsync(MigrationText["TargetReadinessEvidence"],
                () => FinanceApiClient.GetLatestAccountingProviderSwitchPreparationAsync(companyId, switchId, cancellationToken),
                (AccountingProviderSwitchPreparationResponse?)null, failures)
            : Task.FromResult<AccountingProviderSwitchPreparationResponse?>(null);
        var transferTask = !isInternalTarget
            ? LoadPieceAsync(MigrationText["TransferEvidence"],
                () => FinanceApiClient.GetLatestAccountingProviderSwitchTargetTransferAsync(companyId, switchId, cancellationToken),
                (AccountingProviderSwitchTargetTransferResponse?)null, failures)
            : Task.FromResult<AccountingProviderSwitchTargetTransferResponse?>(null);

        await Task.WhenAll(allowedTask, assessmentTask, completenessTask, mappingsTask, rehearsalTask, planTask,
            cutoverTask, guidanceTask, recommendationTask, auditTask, monitoringTask, postActivationTask, operationsTask, readinessTask,
            preparationTask, transferTask);
        SwitchAllowedActions = await allowedTask;
        Assessment = await assessmentTask;
        Completeness = await completenessTask;
        Mappings = await mappingsTask;
        Rehearsal = await rehearsalTask;
        PlanReadiness = await planTask;
        SwitchCutover = await cutoverTask;
        Guidance = await guidanceTask;
        Recommendation = await recommendationTask;
        AuditEvidence = await auditTask;
        MonitoringEvidence = await monitoringTask;
        SwitchMonitoring = await postActivationTask;
        SwitchOperations = await operationsTask;
        InternalReadiness = await readinessTask;
        Preparation = await preparationTask;
        Transfer = await transferTask;
        PartialFailures = failures.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static async Task<T> LoadPieceAsync<T>(string label, Func<Task<T>> load, T fallback, List<string> failures)
    {
        try { return await load(); }
        catch (OperationCanceledException) { throw; }
        catch (FinanceApiException) { lock (failures) failures.Add(label); return fallback; }
    }

    private void ClearSwitchWorkspace()
    {
        SwitchAllowedActions = null; Assessment = null; Completeness = null; Mappings = []; Rehearsal = null;
        PlanReadiness = null; InternalReadiness = null; Preparation = null; Transfer = null; SwitchCutover = null;
        SwitchMonitoring = null; SwitchOperations = null;
        Guidance = null; Recommendation = null; AuditEvidence = null; MonitoringEvidence = null; PartialFailures = [];
    }

    private void InitializeMigrationTarget()
    {
        if (Authority?.CurrentPeriod?.Authority == "internal_ledger")
        {
            var provider = Authority.Providers.FirstOrDefault();
            MigrationTarget = provider is null ? "internal" : $"external:{provider.ProviderKey}";
        }
        else
        {
            MigrationTarget = "internal";
        }
    }

    private async Task StartGuidedMigrationAsync()
    {
        if (AccessState.CompanyId is not Guid companyId || CurrentUserContext?.User.Id is not Guid responsibleUserId ||
            responsibleUserId == Guid.Empty || EffectiveFiscalPeriodId == Guid.Empty) return;
        var sourceExternal = Authority?.CurrentPeriod?.Authority == "external_provider";
        var targetParts = MigrationTarget.Split(':', 2);
        await ActAsync(async () =>
        {
            var created = await FinanceApiClient.CreateAccountingProviderSwitchAsync(companyId, new()
            {
                SourceKind = sourceExternal ? "external" : "internal",
                SourceProviderKey = sourceExternal ? Authority?.CurrentPeriod?.ProviderKey : null,
                TargetKind = targetParts[0],
                TargetProviderKey = targetParts.Length == 2 ? targetParts[1] : null,
                EffectiveFiscalPeriodId = EffectiveFiscalPeriodId,
                MigrationStrategy = MigrationStrategy,
                Reason = MigrationReason,
                ResponsibleUserId = responsibleUserId
            });
            MigrationId = created.Id;
            SelectedSwitch = created;
            ActionMessage = MigrationText["GuidedMigrationCreated"];
            await LoadAsync(companyId, _loadCancellation?.Token ?? default);
        });
    }

    private async Task HandleMigrationActionAsync(string action)
    {
        if (AccessState.CompanyId is not Guid companyId || SelectedSwitch is null) return;
        if (action == "refresh") { await ReloadAsync(); return; }
        await ActAsync(async () =>
        {
            if (action.StartsWith("mapping-approval:", StringComparison.Ordinal) &&
                Guid.TryParse(action["mapping-approval:".Length..], out var mappingId))
            {
                var mapping = Mappings.First(x => x.Id == mappingId);
                await FinanceApiClient.RequestAccountingProviderSwitchMappingApprovalAsync(companyId,
                    SelectedSwitch.Id, mapping.Id, mapping.Version);
                ActionMessage = MigrationText["MappingApprovalRequested"];
            }
            else if (action.StartsWith("reconcile-transfer:", StringComparison.Ordinal) && Transfer is not null)
            {
                var parts = action.Split(':', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 3 || !Guid.TryParse(parts[1], out var itemId)) return;
                var item = Transfer.Items.First(x => x.Id == itemId && x.ReconciliationNeeded);
                var succeeded = parts[2] == "success";
                await FinanceApiClient.ReconcileAccountingProviderSwitchTargetTransferItemAsync(companyId,
                    SelectedSwitch.Id, Transfer.Id, item.Id, new()
                    {
                        ProviderConfirmedSuccess = succeeded,
                        Summary = succeeded ? MigrationText["ProviderRecordVerified"] : MigrationText["ProviderConfirmedNotSent"],
                        ExpectedItemVersion = item.Version
                    });
                ActionMessage = succeeded ? MigrationText["ProviderSuccessReconciled"] : MigrationText["ProviderNotSentReconciled"];
            }
            else
            {
                await RunMigrationActionAsync(companyId, action);
            }
            await LoadAsync(companyId, _loadCancellation?.Token ?? default);
        });
    }

    private async Task RunMigrationActionAsync(Guid companyId, string action)
    {
        var providerSwitch = SelectedSwitch!;
        var run = new StartAccountingProviderSwitchRunApiRequest
        {
            ExpectedSwitchVersion = providerSwitch.Version,
            IdempotencyKey = $"web-{action}-{providerSwitch.Id:N}-{Guid.NewGuid():N}"
        };
        switch (action)
        {
            case "start-assessment":
                await FinanceApiClient.StartAccountingProviderSwitchAssessmentAsync(companyId, providerSwitch.Id, run);
                ActionMessage = MigrationText["AssessmentStarted"];
                break;
            case "retry-assessment" when Assessment is not null:
                await FinanceApiClient.ReplayAccountingProviderSwitchAssessmentAsync(companyId, providerSwitch.Id, Assessment.Id, run);
                ActionMessage = MigrationText["AssessmentRestarted"];
                break;
            case "start-rehearsal":
                await FinanceApiClient.StartAccountingProviderSwitchRehearsalAsync(companyId, providerSwitch.Id, run);
                ActionMessage = MigrationText["RehearsalStarted"];
                break;
            case "retry-rehearsal" when Rehearsal is not null:
                await FinanceApiClient.ReplayAccountingProviderSwitchRehearsalAsync(companyId, providerSwitch.Id, Rehearsal.Id, run);
                ActionMessage = MigrationText["RehearsalRestarted"];
                break;
            case "generate-plan" when Rehearsal is not null && CurrentUserContext?.User.Id is Guid userId:
                var freezeStart = DateTime.SpecifyKind(providerSwitch.EffectiveFrom.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
                await FinanceApiClient.GenerateAccountingProviderSwitchCutoverPlanAsync(companyId, providerSwitch.Id, new()
                {
                    RehearsalId = Rehearsal.Id,
                    ExpectedSwitchVersion = providerSwitch.Version,
                    FreezeStartsUtc = freezeStart,
                    FreezeEndsUtc = freezeStart.AddHours(4),
                    RecoveryBoundary = "Before target activation and authoritative target activity.",
                    ParticipantUserIds = [userId]
                });
                ActionMessage = MigrationText["CutoverPlanGenerated"];
                break;
            case "request-plan-approval" when PlanReadiness?.Plan is not null:
                await FinanceApiClient.RequestAccountingProviderSwitchPlanApprovalAsync(companyId,
                    providerSwitch.Id, PlanReadiness.Plan.Id, providerSwitch.Version);
                ActionMessage = MigrationText["PlanApprovalRequested"];
                break;
            case "prepare-target" when PlanReadiness?.Plan is not null:
                var planRun = new StartAccountingProviderSwitchPlanRunApiRequest
                {
                    PlanId = PlanReadiness.Plan.Id,
                    ExpectedSwitchVersion = providerSwitch.Version,
                    IdempotencyKey = run.IdempotencyKey
                };
                if (providerSwitch.Target.Kind == "internal")
                    await FinanceApiClient.StartAccountingProviderSwitchPreparationAsync(companyId, providerSwitch.Id, planRun);
                else
                    await FinanceApiClient.StartAccountingProviderSwitchTargetTransferAsync(companyId, providerSwitch.Id, planRun);
                ActionMessage = MigrationText["TargetPreparationStarted"];
                break;
            case "schedule-cutover" when PlanReadiness?.Plan is not null:
                await FinanceApiClient.ScheduleAccountingProviderSwitchCutoverAsync(companyId, providerSwitch.Id, new()
                {
                    PlanId = PlanReadiness.Plan.Id,
                    ExpectedSwitchVersion = providerSwitch.Version,
                    IdempotencyKey = run.IdempotencyKey
                });
                ActionMessage = MigrationText["CutoverScheduled"];
                break;
            case "start-freeze" when SwitchCutover is not null:
                await RunCutoverActionAsync(companyId, "freeze");
                ActionMessage = MigrationText["FreezeStarted"];
                break;
            case "request-activation-approval" when SwitchCutover is not null:
                await RunCutoverActionAsync(companyId, "activation-approval");
                ActionMessage = MigrationText["ActivationApprovalRequested"];
                break;
            case "activate" when SwitchCutover is not null:
                await RunCutoverActionAsync(companyId, "activate");
                ActionMessage = MigrationText["TargetActivationStarted"];
                break;
            case "retry-cutover" when SwitchCutover is not null:
                await RunCutoverActionAsync(companyId, "retry");
                ActionMessage = MigrationText["CutoverRetryStarted"];
                break;
            case "recover-source" when SwitchCutover is not null:
                await RunCutoverActionAsync(companyId, "recover", MigrationText["OperatorReviewedRecoveryReason"]);
                ActionMessage = MigrationText["RecoveryStarted"];
                break;
            case "monitoring-run" when SwitchMonitoring is not null:
                await FinanceApiClient.RunAccountingProviderSwitchMonitoringActionAsync(companyId,
                    providerSwitch.Id, "run", SwitchMonitoring.Version);
                ActionMessage = MigrationText["MonitoringCheckQueued"];
                break;
            case "monitoring-retry" when SwitchMonitoring is not null:
                await FinanceApiClient.RunAccountingProviderSwitchMonitoringActionAsync(companyId,
                    providerSwitch.Id, "retry", SwitchMonitoring.Version);
                ActionMessage = MigrationText["MonitoringRetryQueued"];
                break;
            case "monitoring-request-closure" when SwitchMonitoring is not null:
                await FinanceApiClient.RunAccountingProviderSwitchMonitoringActionAsync(companyId,
                    providerSwitch.Id, "closure-approval", SwitchMonitoring.Version);
                ActionMessage = MigrationText["MonitoringClosureRequested"];
                break;
            case "monitoring-close" when SwitchMonitoring is not null:
                await FinanceApiClient.RunAccountingProviderSwitchMonitoringActionAsync(companyId,
                    providerSwitch.Id, "close", SwitchMonitoring.Version, CutoverSummary);
                ActionMessage = MigrationText["MonitoringClosed"];
                break;
            case "monitoring-corrective-cutover" when SwitchMonitoring is not null:
                await FinanceApiClient.CreateCorrectiveAccountingProviderSwitchAsync(companyId, providerSwitch.Id, new()
                {
                    EffectiveFiscalPeriodId = EffectiveFiscalPeriodId,
                    ExpectedVersion = SwitchMonitoring.Version,
                    Reason = MigrationText["CorrectiveCutoverReason"]
                });
                ActionMessage = MigrationText["CorrectiveCutoverCreated"];
                break;
            case "cancel":
                await FinanceApiClient.CancelAccountingProviderSwitchAsync(companyId, providerSwitch.Id, new()
                {
                    ExpectedVersion = providerSwitch.Version,
                    Reason = MigrationText["OperatorCancelledMigrationReason"]
                });
                ActionMessage = MigrationText["MigrationCancelled"];
                MigrationId = null;
                break;
        }
    }

    private async Task AcceptMonitoringExceptionAsync(AcceptAccountingProviderSwitchMonitoringExceptionApiRequest request)
    {
        if (AccessState.CompanyId is not Guid companyId || SelectedSwitch is null || SwitchMonitoring is null) return;
        await ActAsync(async () =>
        {
            var incident = SwitchMonitoring.Incidents.Single(x => x.Id == request.IncidentId);
            request.ExpectedVersion = incident.Version;
            await FinanceApiClient.AcceptAccountingProviderSwitchMonitoringExceptionAsync(companyId,
                SelectedSwitch.Id, incident.Id, request);
            ActionMessage = MigrationText["MonitoringExceptionAccepted"];
            await LoadAsync(companyId, _loadCancellation?.Token ?? default);
        });
    }

    private Task<AccountingProviderSwitchCutoverResponse> RunCutoverActionAsync(Guid companyId, string action,
        string? reason = null) => FinanceApiClient.RunAccountingProviderSwitchCutoverActionAsync(companyId,
        SelectedSwitch!.Id, SwitchCutover!.Id, action, SwitchCutover.Version, reason);

    private Task ReloadAsync() => AccessState.CompanyId is Guid companyId
        ? LoadAsync(companyId, _loadCancellation?.Token ?? default)
        : Task.CompletedTask;

    private async Task SaveCutoverValidationAsync()
    {
        if (AccessState.CompanyId is not Guid companyId || CurrentCutover is null) return;
        await ActAsync(async () =>
        {
            Authority = await FinanceApiClient.RecordAccountingCutoverValidationAsync(companyId, CurrentCutover.Id, new()
            {
                OpeningBalancesReconciled = OpeningBalancesReconciled,
                TrialBalanceReconciled = TrialBalanceReconciled,
                SourceMappingsReconciled = SourceMappingsReconciled,
                ConflictCount = ConflictCount,
                Summary = CutoverSummary,
                ExpectedVersion = CurrentCutover.Version
            });
            LoadLegacyCutoverForm();
            ActionMessage = MigrationText["LegacyCutoverChecksSaved"];
        });
    }

    private async Task CompleteCutoverAsync()
    {
        if (AccessState.CompanyId is not Guid companyId || CurrentCutover?.IsCutoverReady != true) return;
        await ActAsync(async () =>
        {
            Authority = await FinanceApiClient.CompleteAccountingAuthorityCutoverAsync(companyId, CurrentCutover.Id,
                new() { ExpectedVersion = CurrentCutover.Version });
            ActionMessage = MigrationText["LegacyCutoverCompleted"];
        });
    }

    private async Task ReconcileExportAsync(AccountingProviderExportResponse export, bool succeeded)
    {
        if (AccessState.CompanyId is not Guid companyId) return;
        await ActAsync(async () =>
        {
            await FinanceApiClient.ReconcileAccountingProviderExportAsync(companyId, export.Id, new()
            {
                ProviderConfirmedSuccess = succeeded,
                ProviderExternalId = succeeded ? export.ProviderExternalId : null,
                Summary = succeeded ? MigrationText["ProviderRecordVerified"] : MigrationText["ProviderConfirmedNotSent"],
                ExpectedVersion = export.Version
            });
            await LoadAsync(companyId, _loadCancellation?.Token ?? default);
            ActionMessage = succeeded ? MigrationText["ProviderSuccessReconciled"] : MigrationText["ProviderNotSentReconciled"];
        });
    }

    private async Task ActAsync(Func<Task> action)
    {
        IsActing = true;
        ActionError = null;
        ActionMessage = null;
        try { await action(); }
        catch (OperationCanceledException) when (_loadCancellation?.IsCancellationRequested == true) { }
        catch (FinanceApiException exception) { ActionError = exception.Message; }
        finally { IsActing = false; }
    }

    private void LoadLegacyCutoverForm()
    {
        if (CurrentCutover is null) return;
        OpeningBalancesReconciled = CurrentCutover.OpeningBalancesReconciled;
        TrialBalanceReconciled = CurrentCutover.TrialBalanceReconciled;
        SourceMappingsReconciled = CurrentCutover.SourceMappingsReconciled;
        ConflictCount = CurrentCutover.ConflictCount;
        CutoverSummary = CurrentCutover.ValidationSummary ?? CutoverSummary;
    }

    private static string AuthorityIcon(string authority) => authority switch { "internal_ledger" => "◇", "external_provider" => "▣", _ => "↔" };
    private static string ProviderMark(string name) => string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => x[0])).ToUpperInvariant()[..1];
    private static string SourceLabel(string source) => source.Replace('_', ' ');
    private static string ExportBadge(string status) => status switch
    {
        "exported" => "status-badge status-badge-success",
        "reconciliation_required" or "failed" => "status-badge status-badge-danger",
        "awaiting_approval" or "approved" or "executing" => "status-badge status-badge-warning",
        _ => "status-badge"
    };

    public void Dispose()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
    }
}
