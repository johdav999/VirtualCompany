using Microsoft.AspNetCore.Components;
using VirtualCompany.Shared;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class YearEndRolloverPage : FinancePageBase
{
    [Inject] private FinanceApiClient Api { get; set; } = default!;

    private IReadOnlyList<YearEndRunSummaryResponse> Runs = [];
    private IReadOnlyList<AccountingFiscalYearResponse> FiscalYears = [];
    private IReadOnlyList<AccountingAccountListItemResponse> EquityAccounts = [];
    private YearEndRunResponse? Selected;
    private Guid? LoadedCompany;
    private bool IsWorking;
    private bool HasError;
    private bool ShowEventForm;
    private YearEndSubsequentEventResponse? ResolvingEvent;
    private string CorrectionJournalText = string.Empty;
    private string ReopenRequestText = string.Empty;
    private string ResolutionReason = string.Empty;
    private string? WorkspaceMessage;
    private PrepareYearEndRunApiRequest Draft = new();
    private RecordYearEndSubsequentEventApiRequest EventDraft = new();

    private IEnumerable<AccountingPeriodResponse> TargetPeriods => FiscalYears.SelectMany(x => x.Periods)
        .Where(x => !x.IsClosed && !x.IsReportingLocked).OrderBy(x => x.StartDate);
    private int BlockerCount => Selected?.CurrentReadiness?.BlockerCount ?? 0;
    private decimal NetIncome => Selected?.RetainedEarningsProposal?.NetIncome ?? 0m;
    private decimal OpeningDifference => Selected?.OpeningBalances.Sum(x => Math.Abs(x.Difference)) ?? 0m;
    private string Currency => Selected?.RetainedEarningsProposal?.Currency ?? "—";
    private bool CanPrepare => Draft.FiscalYearStart != default && Draft.TargetFiscalPeriodId != Guid.Empty &&
        Draft.RetainedEarningsAccountId != Guid.Empty && Draft.OpeningBalanceClearingAccountId != Guid.Empty &&
        Draft.RetainedEarningsAccountId != Draft.OpeningBalanceClearingAccountId;

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();
        if (!AccessState.IsAllowed || AccessState.CompanyId is not Guid companyId || LoadedCompany == companyId) return;
        LoadedCompany = companyId;
        await LoadWorkspaceAsync(companyId);
    }

    private async Task LoadWorkspaceAsync(Guid companyId)
    {
        try
        {
            HasError = false;
            var runs = Api.GetYearEndRunsAsync(companyId);
            var years = Api.GetAccountingFiscalYearsAsync(companyId);
            var accounts = Api.GetAccountingAccountsAsync(companyId, accountClass: "equity", status: "active");
            await Task.WhenAll(runs, years, accounts);
            Runs = await runs; FiscalYears = await years; EquityAccounts = await accounts;
            if (Selected is not null) Selected = await Api.GetYearEndRunAsync(companyId, Selected.Id);
            else if (Runs.FirstOrDefault() is { } latest) Selected = await Api.GetYearEndRunAsync(companyId, latest.Id);
            SetDefaults();
        }
        catch (FinanceApiException exception) { HasError = true; WorkspaceMessage = exception.Message; }
    }

    private void SetDefaults()
    {
        if (Draft.FiscalYearStart == default && FiscalYears.OrderByDescending(x => x.StartDate).FirstOrDefault() is { } year)
            Draft.FiscalYearStart = year.StartDate;
        if (Draft.TargetFiscalPeriodId == Guid.Empty && TargetPeriods.FirstOrDefault(x => x.StartDate == Draft.FiscalYearStart.AddYears(1)) is { } target)
            Draft.TargetFiscalPeriodId = target.Id;
        if (Draft.RetainedEarningsAccountId == Guid.Empty && EquityAccounts.FirstOrDefault(x => x.RoleName?.Contains("retained", StringComparison.OrdinalIgnoreCase) == true) is { } retained)
            Draft.RetainedEarningsAccountId = retained.Id;
        if (Draft.RetainedEarningsAccountId == Guid.Empty) Draft.RetainedEarningsAccountId = EquityAccounts.FirstOrDefault()?.Id ?? Guid.Empty;
        if (Draft.OpeningBalanceClearingAccountId == Guid.Empty) Draft.OpeningBalanceClearingAccountId = EquityAccounts.FirstOrDefault(x => x.Id != Draft.RetainedEarningsAccountId)?.Id ?? Guid.Empty;
        Draft.VoucherSeriesCode = string.IsNullOrWhiteSpace(Draft.VoucherSeriesCode) ? "YE" : Draft.VoucherSeriesCode;
        EventDraft.EventDate = EventDraft.EventDate == default ? DateOnly.FromDateTime(DateTime.Today) : EventDraft.EventDate;
        EventDraft.OwnerUserId = CurrentUserContext?.User.Id ?? Guid.Empty;
    }

    private async Task SelectRunAsync(ChangeEventArgs args)
    {
        if (AccessState.CompanyId is not Guid companyId || !Guid.TryParse(args.Value?.ToString(), out var runId)) return;
        Selected = await Api.GetYearEndRunAsync(companyId, runId);
    }

    private void BeginNewRun()
    {
        Selected = null; WorkspaceMessage = null; HasError = false; ShowEventForm = false; ResolvingEvent = null;
        SetDefaults();
    }

    private Task ReloadAsync() => WorkAsync(async companyId =>
    {
        if (Selected is { } selected)
            Selected = await Api.RefreshYearEndReadinessAsync(companyId, selected.Id, selected.Version, Key("refresh"));
        else await LoadWorkspaceAsync(companyId);
        WorkspaceMessage = "Authoritative year-end evidence was refreshed; prior approvals were invalidated if facts changed.";
    });

    private Task PrepareAsync() => WorkAsync(async companyId =>
    {
        Draft.IdempotencyKey = Key("prepare");
        Selected = await Api.PrepareYearEndRunAsync(companyId, Draft);
        WorkspaceMessage = BlockerCount == 0 ? "Readiness snapshot retained and ready for submission." : "Readiness snapshot retained with blocking checks.";
    });

    private Task SubmitAsync() => EvidenceWorkAsync("submit", (company, run, hash, key) => Api.SubmitYearEndRunAsync(company, run.Id, run.Version, hash, key));
    private Task ExecuteAsync() => EvidenceWorkAsync("execute", (company, run, hash, key) => Api.ExecuteYearEndRunAsync(company, run.Id, run.Version, hash, key));
    private Task ReconcileAsync() => EvidenceWorkAsync("reconcile", (company, run, hash, key) => Api.ReconcileYearEndRunAsync(company, run.Id, run.Version, hash, key));

    private Task ReviewAsync(bool approve) => EvidenceWorkAsync(approve ? "approve" : "reject",
        (company, run, hash, key) => Api.ReviewYearEndRunAsync(company, run.Id, run.Version, hash, approve,
            approve ? "Independent review of retained evidence completed." : "Returned for correction.", key));

    private Task FinalizeAsync() => WorkAsync(async companyId =>
    {
        if (Selected is null) return;
        Selected = await Api.FinalizeYearEndRunAsync(companyId, Selected.Id, Selected.Version, Key("finalize"));
        WorkspaceMessage = "Year-end rollover finalized with immutable journal and evidence links.";
    });

    private Task RecordEventAsync() => WorkAsync(async companyId =>
    {
        if (Selected is null) return;
        EventDraft.IdempotencyKey = Key("event");
        Selected = await Api.RecordYearEndSubsequentEventAsync(companyId, Selected.Id, EventDraft);
        EventDraft = new() { EventDate = DateOnly.FromDateTime(DateTime.Today), OwnerUserId = CurrentUserContext?.User.Id ?? Guid.Empty };
        ShowEventForm = false; WorkspaceMessage = "Subsequent event retained separately from prior-year evidence.";
    });

    private Task SubmitEventAsync(YearEndSubsequentEventResponse item) => WorkAsync(async companyId =>
    {
        if (Selected is null) return;
        Selected = await Api.SubmitYearEndSubsequentEventAsync(companyId, Selected.Id, item.Id, item.Version, Key("event-submit"));
    });

    private Task ReviewEventAsync(YearEndSubsequentEventResponse item, bool approve) => WorkAsync(async companyId =>
    {
        if (Selected is null) return;
        Selected = await Api.ReviewYearEndSubsequentEventAsync(companyId, Selected.Id, item.Id, item.Version, approve,
            approve ? "Independent subsequent-event review completed." : "Event returned for correction.", Key("event-review"));
    });

    private void BeginResolution(YearEndSubsequentEventResponse item)
    {
        ResolvingEvent = item; CorrectionJournalText = ReopenRequestText = string.Empty;
        ResolutionReason = item.Decision == "disclose_only" ? "Disclosure assessment retained; no prior-year journal mutation required." : string.Empty;
    }

    private Task ResolveEventAsync() => WorkAsync(async companyId =>
    {
        if (Selected is null || ResolvingEvent is null) return;
        Guid? journalId = Guid.TryParse(CorrectionJournalText, out var journal) ? journal : null;
        Guid? reopenId = Guid.TryParse(ReopenRequestText, out var reopen) ? reopen : null;
        Selected = await Api.LinkYearEndCorrectionAsync(companyId, Selected.Id, ResolvingEvent.Id,
            ResolvingEvent.Version, journalId, reopenId, ResolutionReason, Key("event-resolution"));
        ResolvingEvent = null; WorkspaceMessage = "The approved subsequent event was resolved with an immutable correction link.";
    });

    private Task EvidenceWorkAsync(string action, Func<Guid, YearEndRunResponse, string, string, Task<YearEndRunResponse>> operation) => WorkAsync(async companyId =>
    {
        if (Selected?.CurrentReadiness?.EvidenceHash is not { Length: > 0 } hash) return;
        Selected = await operation(companyId, Selected, hash, Key(action));
        WorkspaceMessage = action switch { "execute" => "Both year-end journals were committed atomically.", "reconcile" => "Opening balances matched by account, currency, and dimension.", _ => $"Year-end action '{Label(action)}' recorded." };
    });

    private async Task WorkAsync(Func<Guid, Task> action)
    {
        if (AccessState.CompanyId is not Guid companyId) return;
        IsWorking = true; HasError = false; WorkspaceMessage = null;
        try { await action(companyId); Runs = await Api.GetYearEndRunsAsync(companyId); }
        catch (FinanceApiException exception) { HasError = true; WorkspaceMessage = exception.Message; }
        finally { IsWorking = false; }
    }

    private IReadOnlyList<LifecycleStep> Lifecycle
    {
        get
        {
            var statuses = new[] { "ready", "pending_approval", "approved", "executed", "reconciled", "completed" };
            var labels = new[] { "Readiness", "Submission", "Approval", "Journal execution", "Opening verification", "Finalized" };
            var detail = new[] { "Checks and evidence hash", "Exact snapshot frozen", "Independent sign-off", "Atomic posting chain", "Zero-difference proof", "Immutable completion" };
            var current = Selected is null ? -1 : Array.IndexOf(statuses, Selected.Status);
            if (Selected?.Status == "draft") current = -1;
            if (Selected?.Status == "failed") current = Math.Max(0, Array.IndexOf(statuses, "executed"));
            return statuses.Select((status, index) => new LifecycleStep(index + 1, labels[index], detail[index], index < current ? "complete" : index == current ? "current" : "pending")).ToArray();
        }
    }

    private static string StepClass(string status) => $"step-{status}";
    private static string StatusClass(string status) => status switch { "completed" or "reconciled" or "ready" or "matched" or "approved" => "green", "failed" or "blocked" or "mismatch" or "rejected" => "red", "pending_approval" or "under_review" => "amber", _ => "blue" };
    private static string Label(string value) => value.Replace('_', ' ');
    private static string Money(decimal value) => value.ToString("N2");
    private static string Short(Guid value) => value.ToString("N")[..8];
    private static string ShortHash(string? value) => string.IsNullOrWhiteSpace(value) ? "No evidence" : value.Length > 14 ? value[..14] + "…" : value;
    private static string ShortDimension(string value) => value.Length > 32 ? value[..32] + "…" : value;
    private static string Key(string action) => $"year-end-ui-{action}-{Guid.NewGuid():N}";
    private sealed record LifecycleStep(int Number, string Label, string Detail, string Status);
}
