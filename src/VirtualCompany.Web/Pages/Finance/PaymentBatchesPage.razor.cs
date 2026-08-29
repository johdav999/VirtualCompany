using System.Globalization;
using Microsoft.AspNetCore.Components;
using VirtualCompany.Shared;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class PaymentBatchesPage : FinancePageBase
{
    [Inject] private FinanceApiClient FinanceApiClient { get; set; } = default!;
    [Parameter] public Guid? BatchId { get; set; }
    private PaymentBatchListResponse? Workspace { get; set; }
    private PaymentBatchDetailResponse? Selected { get; set; }
    private PaymentBatchPreviewResponse? Preview { get; set; }
    private PaymentBatchExecutionResponse? Execution { get; set; }
    private BankConnectionStatusResponse? BankConnections { get; set; }
    private IReadOnlyList<EligiblePaymentObligationResponse> EligibleObligations { get; set; } = [];
    private IReadOnlyList<PaymentExecutionAccountOption> ExecutionAccounts { get; set; } = [];
    private bool IsSubmitting { get; set; } private string? ActionError { get; set; } private string? ActionMessage { get; set; }
    private string NewBatchName { get; set; } = string.Empty; private DateOnly NewPlannedDate { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
    private string CreateIdempotencyKey { get; set; } = $"web-create-{Guid.NewGuid():N}";
    private Guid SelectedExecutionConnectionId { get; set; }
    private Guid SelectedExecutionAccountId { get; set; }
    private string ProviderPaymentId { get; set; } = string.Empty;
    private string SettlementBankTransactionId { get; set; } = string.Empty;
    private long SettlementBankSourceVersion { get; set; }
    private bool CanManage => FinanceAccess.CanEdit(AccessState.MembershipRole);
    private bool CanApprove => FinanceAccess.CanApproveInvoices(AccessState.MembershipRole);
    private string LauraHref => AccessState.CompanyId is Guid companyId ? $"/agents?companyId={companyId:D}&agent=Laura" : "/agents";
    private string PlannedTotalText => Workspace?.PlannedTotals.Count > 0 ? FormatTotals(Workspace.PlannedTotals) : "—";
    private string ValidationLabel => Selected?.Validation is null ? FinanceText["NeedsValidation"] : Selected.Validation.IsValid ? FinanceText["Validated"] : FinanceText["AttentionNeeded"];
    private IReadOnlyList<PaymentExecutionProgressStep> ExecutionProgressSteps =>
    [
        new("queued", FinanceText["Queued"]),
        new("awaiting_authorization", FinanceText["SentToBank"]),
        new("provider_accepted", FinanceText["BankAccepted"]),
        new("processing", FinanceText["Processing"]),
        new("settled", FinanceText["Settled"])
    ];

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync(); if (!AccessState.IsAllowed || AccessState.CompanyId is not Guid companyId) return;
        await LoadAsync(companyId);
    }

    private async Task LoadAsync(Guid companyId)
    {
        ActionError = null;
        try
        {
            Workspace = await FinanceApiClient.ListPaymentBatchesAsync(companyId, limit: 200) ?? new();
            EligibleObligations = await FinanceApiClient.ListEligiblePaymentObligationsAsync(companyId) ?? [];
            BankConnections = await FinanceApiClient.GetBankConnectionsAsync(companyId);
            ExecutionAccounts = BuildExecutionAccounts(BankConnections);
            if (ExecutionAccounts.FirstOrDefault() is { } first)
            {
                SelectedExecutionConnectionId = first.ConnectionId;
                SelectedExecutionAccountId = first.CompanyBankAccountId;
            }
            var selectedId = BatchId ?? Workspace.Items.FirstOrDefault()?.Id;
            Selected = selectedId.HasValue ? await FinanceApiClient.GetPaymentBatchAsync(companyId, selectedId.Value) : null;
            Execution = Selected is null ? null : await FinanceApiClient.GetPaymentExecutionForBatchAsync(companyId, Selected.Summary.Id);
            ProviderPaymentId = Execution?.ProviderPaymentId ?? string.Empty;
        }
        catch (FinanceApiException ex) { ActionError = ex.Message; }
    }

    private async Task CreateBatchAsync()
    {
        if (!CanManage || AccessState.CompanyId is not Guid companyId || IsSubmitting) return;
        if (string.IsNullOrWhiteSpace(NewBatchName)) NewBatchName = $"Payment run {NewPlannedDate:yyyy-MM-dd}";
        await RunAsync(async () =>
        {
            var result = await FinanceApiClient.CreatePaymentBatchAsync(companyId, new() { Name = NewBatchName, PlannedExecutionDate = NewPlannedDate, IdempotencyKey = CreateIdempotencyKey });
            CreateIdempotencyKey = $"web-create-{Guid.NewGuid():N}"; NewBatchName = string.Empty;
            Navigation.NavigateTo(FinanceRoutes.BuildPaymentBatchPath(result.Summary.Id, companyId));
        }, FinanceText["BatchCreated"]);
    }

    private Task PreviewAsync() => Selected is null || AccessState.CompanyId is not Guid companyId ? Task.CompletedTask : RunAsync(async () =>
    { Preview = await FinanceApiClient.PreviewPaymentBatchAsync(companyId, Selected.Summary.Id); }, FinanceText["PreviewUpdated"]);
    private Task ValidateAsync() => MutateAsync("validate", (company, id, request) => FinanceApiClient.ValidatePaymentBatchAsync(company, id, request), FinanceText["BatchValidated"]);
    private Task SubmitAsync() => MutateAsync("submit", (company, id, request) => FinanceApiClient.SubmitPaymentBatchAsync(company, id, request), FinanceText["BatchSubmittedForApproval"]);
    private Task RegenerateAsync() => MutateAsync("regenerate", (company, id, request) => FinanceApiClient.RegeneratePaymentBatchAsync(company, id, request), FinanceText["BatchRegenerated"]);
    private Task ApproveAsync() => DecideAsync("approve", true);
    private Task RejectAsync() => DecideAsync("reject", false);
    private async Task DecideAsync(string action, bool approve)
    {
        if (Selected is null || AccessState.CompanyId is not Guid companyId || IsSubmitting) return;
        await RunAsync(async () =>
        {
            var request = new DecidePaymentBatchApiRequest { ExpectedVersion = Selected.Summary.Version, IdempotencyKey = OperationKey(action), Comment = approve ? FinanceText["ApprovalReviewComment"] : FinanceText["RejectionReviewComment"] };
            Selected = approve ? await FinanceApiClient.ApprovePaymentBatchAsync(companyId, Selected.Summary.Id, request) : await FinanceApiClient.RejectPaymentBatchAsync(companyId, Selected.Summary.Id, request);
            await ReloadWorkspaceAsync(companyId);
        }, approve ? FinanceText["BatchApprovedInternally"] : FinanceText["BatchRejected"]);
    }
    private async Task CancelAsync()
    {
        if (Selected is null || AccessState.CompanyId is not Guid companyId || IsSubmitting) return;
        await RunAsync(async () =>
        {
            Selected = await FinanceApiClient.CancelPaymentBatchAsync(companyId, Selected.Summary.Id, new() { ExpectedVersion = Selected.Summary.Version, IdempotencyKey = OperationKey("cancel"), Reason = FinanceText["CancelledBeforeBankSubmission"] });
            await ReloadWorkspaceAsync(companyId);
        }, FinanceText["BatchCancelled"]);
    }

    private async Task QueueExecutionAsync()
    {
        if (!CanApprove || Selected is null || AccessState.CompanyId is not Guid companyId ||
            SelectedExecutionConnectionId == Guid.Empty || SelectedExecutionAccountId == Guid.Empty || IsSubmitting) return;
        await RunAsync(async () => Execution = await FinanceApiClient.QueuePaymentExecutionAsync(companyId,
            Selected.Summary.Id, new()
            {
                ExpectedBatchVersion = Selected.Summary.Version,
                BankConnectionId = SelectedExecutionConnectionId,
                CompanyBankAccountId = SelectedExecutionAccountId,
                IdempotencyKey = $"web-execute-{Selected.Summary.Id:N}-v{Selected.Summary.InstructionSetVersion}"
            }), FinanceText["PaymentExecutionQueued"]);
    }

    private void SelectExecutionAccount(PaymentExecutionAccountOption option)
    {
        SelectedExecutionConnectionId = option.ConnectionId;
        SelectedExecutionAccountId = option.CompanyBankAccountId;
    }

    private async Task RefreshExecutionAsync()
    {
        if (!CanApprove || Execution is null || AccessState.CompanyId is not Guid companyId || IsSubmitting) return;
        await RunAsync(async () => Execution = await FinanceApiClient.ReconcilePaymentExecutionAsync(companyId,
            Execution.Id, new()
            {
                ExpectedVersion = Execution.Version,
                ProviderPaymentId = null,
                Reason = FinanceText["OperatorStatusRefreshReason"],
                IdempotencyKey = ExecutionKey("refresh")
            }), FinanceText["PaymentStatusRefreshQueued"]);
    }

    private async Task ReconcileExecutionAsync()
    {
        if (!CanApprove || Execution is null || AccessState.CompanyId is not Guid companyId || IsSubmitting) return;
        await RunAsync(async () => Execution = await FinanceApiClient.ReconcilePaymentExecutionAsync(companyId,
            Execution.Id, new()
            {
                ExpectedVersion = Execution.Version,
                ProviderPaymentId = string.IsNullOrWhiteSpace(ProviderPaymentId) ? null : ProviderPaymentId.Trim(),
                Reason = FinanceText["OperatorReconciliationReason"],
                IdempotencyKey = ExecutionKey("reconcile")
            }), FinanceText["PaymentReconciliationQueued"]);
    }

    private async Task CancelExecutionAsync()
    {
        if (!CanApprove || Execution is null || AccessState.CompanyId is not Guid companyId || IsSubmitting) return;
        await RunAsync(async () => Execution = await FinanceApiClient.CancelPaymentExecutionAsync(companyId,
            Execution.Id, new()
            {
                ExpectedVersion = Execution.Version,
                Reason = FinanceText["OperatorCancellationReason"],
                IdempotencyKey = ExecutionKey("cancel")
            }), FinanceText["PaymentExecutionCancelled"]);
    }

    private async Task SettleExecutionAsync()
    {
        if (!CanApprove || Execution is null || AccessState.CompanyId is not Guid companyId || IsSubmitting ||
            !Guid.TryParse(SettlementBankTransactionId, out var bankTransactionId)) return;
        await RunAsync(async () => Execution = await FinanceApiClient.SettlePaymentExecutionAsync(companyId,
            Execution.Id, new()
            {
                ExpectedVersion = Execution.Version,
                BankTransactionId = bankTransactionId,
                ExpectedBankTransactionSourceVersion = SettlementBankSourceVersion,
                IdempotencyKey = ExecutionKey("settle")
            }), FinanceText["PaymentExecutionSettled"]);
    }

    private async Task RetryRemittanceAsync(PaymentRemittanceResponse remittance)
    {
        if (!CanApprove || Execution is null || AccessState.CompanyId is not Guid companyId || IsSubmitting) return;
        await RunAsync(async () => Execution = await FinanceApiClient.RetryPaymentRemittanceAsync(companyId,
            Execution.Id, remittance.Id, new()
            {
                ExpectedExecutionVersion = Execution.Version,
                IdempotencyKey = $"web-remittance-{remittance.Id:N}-{remittance.AttemptCount + 1}"
            }), FinanceText["RemittanceRetryQueued"]);
    }
    private async Task AddAsync(EligiblePaymentObligationResponse item)
    {
        if (Selected is null || AccessState.CompanyId is not Guid companyId || IsSubmitting) return;
        await RunAsync(async () =>
        {
            Selected = await FinanceApiClient.AddPaymentBatchObligationAsync(companyId, Selected.Summary.Id, new() { ObligationType = item.ObligationType, SourceId = item.SourceId, ExpectedVersion = Selected.Summary.Version, IdempotencyKey = $"web-add-{Selected.Summary.Id:N}-{item.SourceId:N}-{Selected.Summary.Version}" });
            await ReloadWorkspaceAsync(companyId);
        }, FinanceText["ObligationAdded"]);
    }
    private async Task RemoveAsync(PaymentBatchObligationResponse item)
    {
        if (Selected is null || AccessState.CompanyId is not Guid companyId || IsSubmitting) return;
        await RunAsync(async () =>
        {
            Selected = await FinanceApiClient.RemovePaymentBatchObligationAsync(companyId, Selected.Summary.Id, item.Id, new() { ExpectedVersion = Selected.Summary.Version, IdempotencyKey = $"web-remove-{Selected.Summary.Id:N}-{item.Id:N}-{Selected.Summary.Version}" });
            await ReloadWorkspaceAsync(companyId);
        }, FinanceText["ObligationRemoved"]);
    }
    private async Task MutateAsync(string action, Func<Guid, Guid, PaymentBatchVersionedApiRequest, Task<PaymentBatchDetailResponse>> mutation, string message)
    {
        if (Selected is null || AccessState.CompanyId is not Guid companyId || IsSubmitting) return;
        await RunAsync(async () => { Selected = await mutation(companyId, Selected.Summary.Id, new() { ExpectedVersion = Selected.Summary.Version, IdempotencyKey = OperationKey(action) }); await ReloadWorkspaceAsync(companyId); }, message);
    }
    private async Task ReloadWorkspaceAsync(Guid companyId) => Workspace = await FinanceApiClient.ListPaymentBatchesAsync(companyId, limit: 200) ?? new();
    private async Task RunAsync(Func<Task> action, string message)
    {
        IsSubmitting = true; ActionError = null; ActionMessage = null;
        try { await action(); ActionMessage = message; }
        catch (FinanceApiException ex) { ActionError = ex.Message; }
        finally { IsSubmitting = false; }
    }
    private string OperationKey(string action) => Selected is null ? $"web-{action}-{Guid.NewGuid():N}" : $"web-{action}-{Selected.Summary.Id:N}-{Selected.Summary.Version}";
    private string ExecutionKey(string action) => Execution is null ? $"web-{action}-{Guid.NewGuid():N}" : $"web-{action}-{Execution.Id:N}-{Execution.Version}";
    private static IReadOnlyList<PaymentExecutionAccountOption> BuildExecutionAccounts(BankConnectionStatusResponse status)
    {
        var internalAccounts = status.InternalAccounts.Where(x => x.IsActive).ToDictionary(x => x.Id);
        return status.Connections
            .Where(x => x.Status == "active" && x.HealthStatus == "healthy" &&
                x.Capabilities.Contains("payment_initiation", StringComparer.OrdinalIgnoreCase))
            .SelectMany(connection => connection.Accounts
                .Where(account => account.OwnershipStatus == "verified" && account.MappedCompanyBankAccountId.HasValue &&
                    internalAccounts.ContainsKey(account.MappedCompanyBankAccountId.Value))
                .Select(account => new PaymentExecutionAccountOption(connection.Id,
                    account.MappedCompanyBankAccountId!.Value,
                    $"{connection.InstitutionName} · {internalAccounts[account.MappedCompanyBankAccountId.Value].DisplayName} · {account.MaskedAccountNumber}")))
            .ToArray();
    }
    private bool IsAlreadyAdded(EligiblePaymentObligationResponse item) => Selected?.Obligations.Any(x => x.ObligationType == item.ObligationType && x.SourceId == item.SourceId) == true;
    private static string FriendlyType(string value) => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(value.Replace('_', ' '));
    private static string FriendlyReason(string value) => FriendlyType(value.Replace("payment_batch_", string.Empty));
    private string StatusLabel(string value) => value switch { "draft" => FinanceText["Draft"], "validated" => FinanceText["Validated"], "awaiting_approval" => FinanceText["WaitingForApproval"], "approved" => FinanceText["InternallyApproved"], "rejected" => FinanceText["Rejected"], "cancelled" => FinanceText["Cancelled"], _ => FriendlyType(value) };
    private static string StatusTone(string value) => value switch { "approved" => "success", "validated" => "success", "awaiting_approval" => "warning", "rejected" or "cancelled" => "muted", _ => "info" };
    private string ExecutionStatusLabel(string value) => value switch
    {
        "queued" => FinanceText["Queued"],
        "submitting" => FinanceText["SubmittingToBank"],
        "awaiting_authorization" => FinanceText["AwaitingBankAuthorization"],
        "provider_accepted" => FinanceText["BankAccepted"],
        "processing" => FinanceText["Processing"],
        "reconciliation_required" => FinanceText["ReconciliationRequired"],
        "provider_completed" => FinanceText["ProviderCompleted"],
        "settled" => FinanceText["Settled"],
        "rejected" => FinanceText["Rejected"],
        "cancelled" => FinanceText["Cancelled"],
        _ => FriendlyType(value)
    };
    private static string ExecutionStatusTone(string value) => value switch
    {
        "settled" or "provider_completed" => "success",
        "reconciliation_required" => "warning",
        "rejected" or "cancelled" => "muted",
        _ => "info"
    };
    private bool ExecutionStepReached(string step) => Execution is not null &&
        ExecutionRank(Execution.Status) >= ExecutionRank(step) &&
        Execution.Status is not ("rejected" or "cancelled" or "reconciliation_required");
    private static int ExecutionRank(string status) => status switch
    {
        "queued" or "submitting" => 0,
        "awaiting_authorization" => 1,
        "provider_accepted" => 2,
        "processing" => 3,
        "provider_completed" or "settled" => 4,
        _ => -1
    };
    private static string FormatDate(DateOnly value) => value.ToString("d", CultureInfo.CurrentCulture);
    private static string FormatMoney(decimal amount, string currency) => $"{amount:N2} {currency}";
    private static string FormatTotals(IEnumerable<PaymentBatchTotalResponse> totals) => string.Join(" · ", totals.Select(x => FormatMoney(x.Amount, x.Currency)));
    private sealed record PaymentExecutionAccountOption(Guid ConnectionId, Guid CompanyBankAccountId, string Label);
    private sealed record PaymentExecutionProgressStep(string Status, string Label);
}
