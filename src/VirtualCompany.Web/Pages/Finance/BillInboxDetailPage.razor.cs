using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.Logging;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class BillInboxDetailPage : FinancePageBase
{
    [Inject] private FinanceApiClient FinanceApiClient { get; set; } = default!;
    [Inject] private ILogger<BillInboxDetailPage> Logger { get; set; } = default!;

    [Parameter] public Guid BillId { get; set; }

    private FinanceBillInboxDetailResponse? Detail { get; set; }
    private bool IsDetailLoading { get; set; }
    private bool IsSubmittingAction { get; set; }
    private string? DetailErrorMessage { get; set; }
    private string? ActionStatusMessage { get; set; }
    private string? FortnoxRegistrationStatusMessage { get; set; }
    private string Rationale { get; set; } = string.Empty;
    private bool IsBillProposalApproved => Detail?.Status.Equals("Approved", StringComparison.OrdinalIgnoreCase) == true;
    private bool IsSupplierCreationStep => string.Equals(Detail?.FortnoxRegistration?.ActionKind, "supplier_creation", StringComparison.OrdinalIgnoreCase);

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();

        Detail = null;
        DetailErrorMessage = null;
        ActionStatusMessage = null;
        FortnoxRegistrationStatusMessage = null;

        if (!AccessState.IsAllowed || AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        await LoadAsync(companyId);
    }

    private async Task ReloadAsync()
    {
        if (AccessState.CompanyId is Guid companyId)
        {
            await LoadAsync(companyId);
        }
    }

    private async Task LoadAsync(Guid companyId)
    {
        IsDetailLoading = true;
        DetailErrorMessage = null;

        try
        {
            Detail = await FinanceApiClient.GetBillInboxDetailAsync(companyId, BillId);
        }
        catch (FinanceApiException ex)
        {
            Detail = null;
            DetailErrorMessage = ex.Message;
        }
        finally
        {
            IsDetailLoading = false;
        }
    }

    private Task ApproveAsync() => SubmitActionAsync(
        (companyId, rationale) => FinanceApiClient.ApproveBillInboxItemAsync(companyId, BillId, rationale),
        "Invoice approved. Nothing was sent to Fortnox.",
        "Approved from supplier bill review.");

    private Task RejectAsync() => SubmitActionAsync((companyId, rationale) => FinanceApiClient.RejectBillInboxItemAsync(companyId, BillId, rationale), "Rejection recorded.");
    private Task RequestClarificationAsync() => SubmitActionAsync((companyId, rationale) => FinanceApiClient.RequestBillInboxClarificationAsync(companyId, BillId, rationale), "Clarification request recorded.");
    private Task RequestFortnoxRegistrationAsync()
    {
        Logger.LogInformation(
            "Bill inbox Fortnox registration button clicked. BillId: {BillId}. CompanyId: {CompanyId}. IsSubmittingAction: {IsSubmittingAction}.",
            BillId,
            AccessState.CompanyId,
            IsSubmittingAction);

        return SubmitRegistrationActionAsync(
            (companyId, rationale) => FinanceApiClient.RequestBillInboxFortnoxRegistrationAsync(companyId, BillId, rationale),
            "Fortnox registration approval was requested.",
            "Approved for Fortnox registration from supplier bill review.");
    }

    private async Task SubmitActionAsync(
        Func<Guid, string, Task<FinanceBillReviewActionResultResponse>> action,
        string successMessage,
        string? defaultRationale = null)
    {
        if (AccessState.CompanyId is not Guid companyId)
        {
            Logger.LogWarning(
                "Bill inbox review action ignored because the company context is missing. BillId: {BillId}.",
                BillId);
            return;
        }

        if (string.IsNullOrWhiteSpace(Rationale) && string.IsNullOrWhiteSpace(defaultRationale))
        {
            ActionStatusMessage = "Enter a rationale before recording the review action.";
            return;
        }

        IsSubmittingAction = true;
        ActionStatusMessage = null;

        try
        {
            var rationale = string.IsNullOrWhiteSpace(Rationale) ? defaultRationale! : Rationale.Trim();
            await action(companyId, rationale);
            Rationale = string.Empty;
            ActionStatusMessage = successMessage;
            await LoadAsync(companyId);
        }
        catch (FinanceApiException ex)
        {
            ActionStatusMessage = ex.Message;
        }
        finally
        {
            IsSubmittingAction = false;
        }
    }

    private async Task SubmitRegistrationActionAsync(
        Func<Guid, string, Task<FinanceBillFortnoxRegistrationResponse>> action,
        string successMessage,
        string defaultRationale)
    {
        if (AccessState.CompanyId is not Guid companyId)
        {
            Logger.LogWarning(
                "Bill inbox Fortnox registration action ignored because the company context is missing. BillId: {BillId}.",
                BillId);
            return;
        }

        IsSubmittingAction = true;
        ActionStatusMessage = null;
        FortnoxRegistrationStatusMessage = null;

        try
        {
            var rationale = string.IsNullOrWhiteSpace(Rationale) ? defaultRationale : Rationale.Trim();
            Logger.LogInformation(
                "Bill inbox Fortnox registration request started. CompanyId: {CompanyId}. BillId: {BillId}. HasUserRationale: {HasUserRationale}.",
                companyId,
                BillId,
                !string.IsNullOrWhiteSpace(Rationale));

            var result = await action(companyId, rationale);
            Rationale = string.Empty;
            FortnoxRegistrationStatusMessage = result.Status.Equals("Failed", StringComparison.OrdinalIgnoreCase)
                ? result.Message
                : successMessage;
            Logger.LogInformation(
                "Bill inbox Fortnox registration request completed. CompanyId: {CompanyId}. BillId: {BillId}. WriteRequestId: {WriteRequestId}. ApprovalId: {ApprovalId}. Status: {Status}. CanExecute: {CanExecute}. HasPendingRequest: {HasPendingRequest}.",
                companyId,
                BillId,
                result.WriteRequestId,
                result.ApprovalId,
                result.Status,
                result.CanExecute,
                result.HasPendingRequest);
            await LoadAsync(companyId);
        }
        catch (FinanceApiException ex)
        {
            Logger.LogWarning(
                ex,
                "Bill inbox Fortnox registration request failed in the web client. CompanyId: {CompanyId}. BillId: {BillId}.",
                companyId,
                BillId);
            FortnoxRegistrationStatusMessage = ex.Message;
        }
        catch (Exception ex)
        {
            Logger.LogError(
                ex,
                "Bill inbox Fortnox registration request failed before the API response was handled. CompanyId: {CompanyId}. BillId: {BillId}.",
                companyId,
                BillId);
            FortnoxRegistrationStatusMessage = "Fortnox registration failed before the request completed.";
        }
        finally
        {
            IsSubmittingAction = false;
        }
    }

    private async Task SendToFortnoxAsync()
    {
        if (AccessState.CompanyId is not Guid companyId)
        {
            Logger.LogWarning(
                "Bill inbox Fortnox registration send ignored because the company context is missing. BillId: {BillId}.",
                BillId);
            return;
        }

        IsSubmittingAction = true;
        ActionStatusMessage = null;
        FortnoxRegistrationStatusMessage = null;

        try
        {
            Logger.LogInformation(
                "Bill inbox Fortnox registration send started. CompanyId: {CompanyId}. BillId: {BillId}.",
                companyId,
                BillId);
            var result = await FinanceApiClient.SendBillInboxFortnoxRegistrationAsync(companyId, BillId);
            FortnoxRegistrationStatusMessage = result.ActionKind == "invoice_registration" && result.Status.Equals("Supplier ready", StringComparison.OrdinalIgnoreCase)
                ? result.Message
                : result.HasExecuted
                ? string.IsNullOrWhiteSpace(result.ExternalId)
                    ? (result.ActionKind == "supplier_creation" ? "Fortnox created this supplier." : "Fortnox accepted this supplier invoice.")
                    : result.ActionKind == "supplier_creation"
                        ? $"Fortnox created this supplier ({result.ExternalId})."
                        : $"Fortnox accepted this supplier invoice ({result.ExternalId})."
                : result.Message;
            Logger.LogInformation(
                "Bill inbox Fortnox registration send completed. CompanyId: {CompanyId}. BillId: {BillId}. WriteRequestId: {WriteRequestId}. ApprovalId: {ApprovalId}. Status: {Status}. HasExecuted: {HasExecuted}.",
                companyId,
                BillId,
                result.WriteRequestId,
                result.ApprovalId,
                result.Status,
                result.HasExecuted);
            await LoadAsync(companyId);
        }
        catch (FinanceApiException ex)
        {
            Logger.LogWarning(
                ex,
                "Bill inbox Fortnox registration send failed in the web client. CompanyId: {CompanyId}. BillId: {BillId}.",
                companyId,
                BillId);
            FortnoxRegistrationStatusMessage = ex.Message;
        }
        catch (Exception ex)
        {
            Logger.LogError(
                ex,
                "Bill inbox Fortnox registration send failed before the API response was handled. CompanyId: {CompanyId}. BillId: {BillId}.",
                companyId,
                BillId);
            FortnoxRegistrationStatusMessage = "Fortnox registration failed before the request completed.";
        }
        finally
        {
            IsSubmittingAction = false;
        }
    }

    private async Task ApproveAndSendToFortnoxAsync()
    {
        if (AccessState.CompanyId is not Guid companyId)
        {
            Logger.LogWarning(
                "Bill inbox approve and Fortnox registration send ignored because the company context is missing. BillId: {BillId}.",
                BillId);
            return;
        }

        IsSubmittingAction = true;
        ActionStatusMessage = null;
        FortnoxRegistrationStatusMessage = null;

        try
        {
            var rationale = string.IsNullOrWhiteSpace(Rationale)
                ? "Approved and registered in Fortnox from supplier bill review."
                : Rationale.Trim();

            if (!IsBillProposalApproved)
            {
                await FinanceApiClient.ApproveBillInboxItemAsync(companyId, BillId, rationale);
            }

            var result = await FinanceApiClient.SendBillInboxFortnoxRegistrationAsync(companyId, BillId);
            Rationale = string.Empty;
            FortnoxRegistrationStatusMessage = result.HasExecuted
                ? string.IsNullOrWhiteSpace(result.ExternalId)
                    ? "Invoice approved. Fortnox accepted this supplier invoice."
                    : $"Invoice approved. Fortnox accepted this supplier invoice ({result.ExternalId})."
                : result.Message;
            await LoadAsync(companyId);
        }
        catch (FinanceApiException ex)
        {
            Logger.LogWarning(
                ex,
                "Bill inbox approve and Fortnox registration send failed in the web client. CompanyId: {CompanyId}. BillId: {BillId}.",
                companyId,
                BillId);
            FortnoxRegistrationStatusMessage = ex.Message;
        }
        finally
        {
            IsSubmittingAction = false;
        }
    }

    private RenderFragment RenderWarnings(string title, IReadOnlyList<FinanceBillWarningResponse> warnings) => builder =>
    {
        builder.OpenElement(0, "section");
        builder.AddAttribute(1, "class", "mb-3");
        builder.OpenElement(2, "h3");
        builder.AddAttribute(3, "class", "h6");
        builder.AddContent(4, title);
        builder.CloseElement();

        if (warnings.Count == 0)
        {
            builder.OpenElement(5, "p");
                builder.AddAttribute(6, "class", "bill-review-muted mb-0");
            builder.AddContent(7, "No warnings.");
            builder.CloseElement();
        }
        else
        {
            builder.OpenElement(8, "ul");
            builder.AddAttribute(9, "class", "list-unstyled mb-0");
            foreach (var warning in warnings)
            {
                builder.OpenElement(10, "li");
                builder.AddAttribute(11, "class", "bill-review-warning-item");
                builder.OpenElement(12, "span");
                builder.AddAttribute(13, "class", "badge text-bg-warning me-2");
                builder.AddContent(14, warning.Severity);
                builder.CloseElement();
                builder.AddContent(15, warning.Message);
                builder.CloseElement();
            }

            builder.CloseElement();
        }

        builder.CloseElement();
    };

    private string FormatAmount(decimal? amount, string? currency) =>
        amount.HasValue ? string.IsNullOrWhiteSpace(currency) ? LocalNumber.Decimal(amount.Value) : LocalMoney.Format(amount.Value, currency) : "n/a";

    private string FormatDate(DateTime? value) => value.HasValue ? LocalDateTime.Date(DateOnly.FromDateTime(value.Value)) : "n/a";
    private string FormatDateTime(DateTime value) => LocalDateTime.DateTime(value);
    private string FormatConfidence(decimal? value) => value.HasValue ? $"({LocalNumber.Percentage(value.Value)})" : string.Empty;
    private string BuildSourcePreviewHeading() =>
        string.Equals(Detail?.SourcePreview?.SourceLabel, "Email body", StringComparison.OrdinalIgnoreCase)
            ? "Invoice from email"
            : "Invoice document";

    private string BuildLauraRecommendationHeadline()
    {
        if (Detail is null)
        {
            return "Review needed";
        }

        return HasUnresolvedWarnings(Detail)
            ? "Review the warnings before approving."
            : "This bill looks ready.";
    }

    private string BuildLauraRecommendationSummary()
    {
        if (Detail is null)
        {
            return string.Empty;
        }

        var due = Detail.DueDateUtc.HasValue
            ? $" It is due {FormatDate(Detail.DueDateUtc)}."
            : string.Empty;
        var warningText = HasUnresolvedWarnings(Detail)
            ? " Laura found warnings that need a decision first."
            : " No validation or duplicate warnings were found.";

        return $"Laura found invoice {Detail.BillReference} from {Detail.SupplierName} for {FormatAmount(Detail.Amount, Detail.Currency)}.{due} Confidence is {Detail.ConfidenceLevel.ToLowerInvariant()} {FormatConfidence(Detail.Confidence)}.{warningText}";
    }

    private string BuildLauraNextStep()
    {
        if (Detail is null)
        {
            return string.Empty;
        }

        if (IsBillProposalApproved)
        {
            return IsSupplierCreationStep
                ? "Next step: create the missing Fortnox supplier, then send the invoice."
                : "Next step: send it to Fortnox when you are ready.";
        }

        if (IsSupplierCreationStep)
        {
            return "Next step: approve the invoice, then create the missing Fortnox supplier.";
        }

        return Detail.FortnoxRegistration?.CanSendDirect == true
            ? "Next step: approve only, or approve and register it in Fortnox now."
            : "Next step: approve the invoice, or request approval to send it to Fortnox.";
    }

    private static bool HasUnresolvedWarnings(FinanceBillInboxDetailResponse detail) =>
        detail.ValidationWarnings.Concat(detail.DuplicateWarnings).Any(x => !x.IsResolved);

    private static string FormatActionName(string value) =>
        value switch
        {
            "approve" => "Approve",
            "clarification_requested" => "Request clarification",
            _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.Replace('_', ' '))
        };

    private static string FormatEvidenceLocation(FinanceBillEvidenceReferenceResponse evidence) =>
        string.Join(" / ", new[] { evidence.PageReference, evidence.SectionReference, evidence.TextSpan }.Where(x => !string.IsNullOrWhiteSpace(x)));

    private string BuildApprovalHref(Guid approvalId) =>
        AccessState.CompanyId is Guid companyId
            ? $"/approvals?companyId={companyId:D}&approvalId={approvalId:D}"
            : $"/approvals?approvalId={approvalId:D}";

    private static string BuildBillStatusClass(string status) =>
        $"bill-status-badge {ResolveBillToneClass(status)}";

    private static string BuildFortnoxStatusClass(string status) =>
        $"bill-status-badge {ResolveFortnoxToneClass(status)}";

    private static string ResolveBillToneClass(string status) =>
        status.ToLowerInvariant() switch
        {
            "approved" => "bill-status-badge--success",
            "rejected" => "bill-status-badge--danger",
            "needs review" or "proposed for approval" => "bill-status-badge--warning",
            _ => "bill-status-badge--neutral"
        };

    private static string ResolveFortnoxToneClass(string status) =>
        status.ToLowerInvariant() switch
        {
            "sent to fortnox" or "executed" => "bill-status-badge--success",
            "supplier ready" or "ready to send" or "approved" or "approved to send" or "waiting for approval" => "bill-status-badge--info",
            "supplier approval needed" => "bill-status-badge--warning",
            "sent with outdated details" => "bill-status-badge--warning",
            "failed" => "bill-status-badge--danger",
            _ => "bill-status-badge--neutral"
        };
}
