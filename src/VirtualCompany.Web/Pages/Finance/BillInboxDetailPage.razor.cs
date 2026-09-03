using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.Logging;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class BillInboxDetailPage : FinancePageBase
{
    [Inject] private FinanceApiClient FinanceApiClient { get; set; } = default!;
    [Inject] private ApprovalApiClient ApprovalApiClient { get; set; } = default!;
    [Inject] private InboxApiClient InboxApiClient { get; set; } = default!;
    [Inject] private ILogger<BillInboxDetailPage> Logger { get; set; } = default!;

    [Parameter] public Guid BillId { get; set; }

    private FinanceBillInboxDetailResponse? Detail { get; set; }
    private bool IsDetailLoading { get; set; }
    private bool IsSubmittingAction { get; set; }
    private bool IsApprovalLoading { get; set; }
    private bool IsSubmittingApprovalDecision { get; set; }
    private bool IsUpdatingApprovalAutomation { get; set; }
    private string? DetailErrorMessage { get; set; }
    private string? ActionStatusMessage { get; set; }
    private string? FortnoxRegistrationStatusMessage { get; set; }
    private string? ApprovalErrorMessage { get; set; }
    private string? ApprovalStatusMessage { get; set; }
    private string? ApprovalAutomationMessage { get; set; }
    private string Rationale { get; set; } = string.Empty;
    private string ApprovalComment { get; set; } = string.Empty;
    private ApprovalRequestViewModel? EmbeddedApproval { get; set; }
    private SupplierApprovalAutomationResponse? ApprovalAutomation { get; set; }
    private bool IsBillProposalApproved => Detail?.Status.Equals("Approved", StringComparison.OrdinalIgnoreCase) == true;
    private bool IsSupplierCreationStep => string.Equals(Detail?.FortnoxRegistration?.ActionKind, "supplier_creation", StringComparison.OrdinalIgnoreCase);
    private bool HasPendingEmbeddedApproval =>
        EmbeddedApproval?.CurrentStep is not null &&
        string.Equals(EmbeddedApproval.Status, "pending", StringComparison.OrdinalIgnoreCase);

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();

        Detail = null;
        DetailErrorMessage = null;
        ActionStatusMessage = null;
        FortnoxRegistrationStatusMessage = null;
        ApprovalStatusMessage = null;
        ApprovalComment = string.Empty;
        EmbeddedApproval = null;
        ApprovalAutomation = null;
        ApprovalAutomationMessage = null;

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
            if (Detail?.Status.Equals("Sent to payment/exported", StringComparison.OrdinalIgnoreCase) == true)
            {
                Navigation.NavigateTo(
                    FinanceRoutes.WithCompanyContext(FinanceRoutes.SupplierBillsReview, companyId),
                    replace: true);
                return;
            }

            if (Detail?.CanUseFortnoxAccounting == true)
            {
                await LoadApprovalAutomationAsync(companyId);
            }
            await LoadEmbeddedApprovalAsync(companyId);
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

    private async Task LoadApprovalAutomationAsync(Guid companyId)
    {
        try
        {
            ApprovalAutomation = await FinanceApiClient.GetSupplierApprovalAutomationAsync(companyId, BillId);
        }
        catch (FinanceApiException ex)
        {
            ApprovalAutomation = null;
            ApprovalAutomationMessage = ex.Message;
            Logger.LogWarning(
                ex,
                "Supplier approval automation could not be loaded. CompanyId: {CompanyId}. BillId: {BillId}.",
                companyId,
                BillId);
        }
    }

    private async Task ToggleApprovalAutomationAsync(SupplierApprovalAutomationStageResponse stage)
    {
        if (AccessState.CompanyId is not Guid companyId || !stage.CanConfigure)
        {
            return;
        }

        IsUpdatingApprovalAutomation = true;
        ApprovalAutomationMessage = null;
        try
        {
            ApprovalAutomation = await FinanceApiClient.SetSupplierApprovalAutomationAsync(
                companyId,
                BillId,
                stage.Stage,
                !stage.IsEnabled);
            ApprovalAutomationMessage = stage.IsEnabled
                ? $"Automatic {stage.StepName} approval was turned off for this supplier."
                : $"{stage.AgentDisplayName} can now automatically approve this supplier's {stage.StepName}.";
            await LoadAsync(companyId);
        }
        catch (FinanceApiException ex)
        {
            ApprovalAutomationMessage = ex.Message;
        }
        finally
        {
            IsUpdatingApprovalAutomation = false;
        }
    }

    private async Task LoadEmbeddedApprovalAsync(Guid companyId)
    {
        var approvalId = Detail?.FortnoxRegistration?.ApprovalId;
        if (approvalId is null)
        {
            EmbeddedApproval = null;
            ApprovalErrorMessage = null;
            return;
        }

        IsApprovalLoading = true;
        ApprovalErrorMessage = null;

        try
        {
            EmbeddedApproval = ApprovalPresentationFormatter.Format(
                await ApprovalApiClient.GetAsync(companyId, approvalId.Value));
        }
        catch (OnboardingApiException ex)
        {
            EmbeddedApproval = null;
            ApprovalErrorMessage = ex.Message;
            Logger.LogWarning(
                ex,
                "Supplier bill approval could not be loaded in the bill review. CompanyId: {CompanyId}. BillId: {BillId}. ApprovalId: {ApprovalId}.",
                companyId,
                BillId,
                approvalId);
        }
        finally
        {
            IsApprovalLoading = false;
        }
    }

    private async Task DecideEmbeddedApprovalAsync(string decision)
    {
        if (AccessState.CompanyId is not Guid companyId ||
            EmbeddedApproval?.CurrentStep is not { } currentStep)
        {
            return;
        }

        IsSubmittingApprovalDecision = true;
        ApprovalErrorMessage = null;
        ApprovalStatusMessage = null;

        try
        {
            var result = await InboxApiClient.DecidePendingApprovalAsync(
                companyId,
                EmbeddedApproval.Id,
                new ApprovalDecisionRequest
                {
                    ApprovalId = EmbeddedApproval.Id,
                    StepId = currentStep.Id,
                    Decision = decision,
                    Comment = string.IsNullOrWhiteSpace(ApprovalComment) ? null : ApprovalComment.Trim()
                });

            EmbeddedApproval = ApprovalPresentationFormatter.Format(result.Approval);
            ApprovalComment = string.Empty;
            ApprovalStatusMessage = decision.StartsWith("reject", StringComparison.OrdinalIgnoreCase)
                ? "Approval rejected."
                : result.IsFinalized
                    ? "Approval recorded."
                    : "Approval recorded. The next approval step is now waiting.";

            Logger.LogInformation(
                "Supplier bill approval decided from bill review. CompanyId: {CompanyId}. BillId: {BillId}. ApprovalId: {ApprovalId}. Decision: {Decision}. IsFinalized: {IsFinalized}.",
                companyId,
                BillId,
                EmbeddedApproval.Id,
                decision,
                result.IsFinalized);

            await LoadAsync(companyId);
        }
        catch (OnboardingApiException ex)
        {
            ApprovalErrorMessage = ex.Message;
            Logger.LogWarning(
                ex,
                "Supplier bill approval decision failed in bill review. CompanyId: {CompanyId}. BillId: {BillId}. ApprovalId: {ApprovalId}. Decision: {Decision}.",
                companyId,
                BillId,
                EmbeddedApproval.Id,
                decision);
        }
        finally
        {
            IsSubmittingApprovalDecision = false;
        }
    }

    private Task ApproveAsync() => SubmitActionAsync(
        (companyId, rationale) => FinanceApiClient.ApproveBillInboxItemAsync(companyId, BillId, rationale),
        Detail?.UsesInternalAccounting == true
            ? "Invoice approved for accounting in Virtual Company."
            : "Invoice approved. Nothing was sent to Fortnox.",
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
            if (result.HasExecuted &&
                result.Status.Equals("Sent to Fortnox", StringComparison.OrdinalIgnoreCase))
            {
                Navigation.NavigateTo(
                    FinanceRoutes.WithCompanyContext(FinanceRoutes.SupplierBillsReview, companyId));
                return;
            }

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

    private SourceInvoicePresentation BuildSourceInvoicePresentation(string bodyText)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in bodyText.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            var line = rawLine.Trim();
            var separator = line.IndexOf(':');
            if (separator <= 0 || separator == line.Length - 1)
            {
                continue;
            }

            var label = line[..separator].Trim();
            var value = CleanSourceValue(line[(separator + 1)..]);
            if (!string.IsNullOrWhiteSpace(value) && !fields.ContainsKey(label))
            {
                fields[label] = value;
            }
        }

        string? Find(params string[] labels)
        {
            foreach (var label in labels)
            {
                if (fields.TryGetValue(label, out var value))
                {
                    return value;
                }
            }

            return null;
        }

        var supplier = new List<SourceInvoiceField>();
        AddSourceField(supplier, "Supplier", Find("Supplier", "Leverantör") ?? Detail?.SupplierName);
        AddSourceField(supplier, "Organisation number", Find("Organisation number", "Organization number", "Org.nr", "Organisationsnummer") ?? Detail?.SupplierOrgNumber);
        AddSourceField(supplier, "VAT number", Find("VAT number", "VAT No", "Momsregistreringsnummer"));
        AddSourceField(supplier, "Address", Find("Address", "Adress"));
        AddSourceField(supplier, "Email", Find("Email", "E-mail"));

        var invoice = new List<SourceInvoiceField>();
        AddSourceField(invoice, "Invoice number", Find("Invoice number", "Fakturanummer") ?? Detail?.BillReference);
        AddSourceField(invoice, "Invoice date", Find("Invoice date", "Fakturadatum") ?? FormatDate(Detail?.BillDateUtc));
        AddSourceField(invoice, "Due date", Find("Due date", "Förfallodatum") ?? FormatDate(Detail?.DueDateUtc));
        AddSourceField(invoice, "Payment terms", Find("Payment terms", "Betalningsvillkor"));
        AddSourceField(invoice, "Currency", Find("Currency", "Valuta") ?? Detail?.Currency);

        var service = new List<SourceInvoiceField>();
        AddSourceField(service, "Description", Find("Description", "Beskrivning"));
        AddSourceField(service, "Quantity", Find("Quantity", "Antal"));
        AddSourceField(service, "Amount excluding VAT", Find("Amount excluding VAT", "Amount excl. VAT", "Belopp exklusive moms"));
        AddSourceField(service, "VAT rate", Find("VAT rate", "Momssats"));
        AddSourceField(service, "VAT amount", Find("VAT amount", "VAT", "Moms") ?? FormatAmount(Detail?.VatAmount, Detail?.Currency));
        AddSourceField(service, "Total amount due", Find("Total amount due", "Total", "Att betala") ?? FormatAmount(Detail?.Amount, Detail?.Currency), isTotal: true);

        var payment = new List<SourceInvoiceField>();
        AddSourceField(payment, "Bankgiro", Find("Bankgiro"));
        AddSourceField(payment, "Payment reference", Find("Payment reference", "Reference", "OCR", "Betalningsreferens"));

        var invoiceNumber = Find("Invoice number", "Fakturanummer") ?? Detail?.BillReference ?? "Supplier invoice";
        var totalAmount = Find("Total amount due", "Total", "Att betala") ?? FormatAmount(Detail?.Amount, Detail?.Currency);
        var recognizedFieldCount = supplier.Count + invoice.Count + service.Count + payment.Count;

        return new SourceInvoicePresentation(supplier, invoice, service, payment, invoiceNumber, totalAmount, recognizedFieldCount >= 3);
    }

    private static void AddSourceField(List<SourceInvoiceField> fields, string label, string? value, bool isTotal = false)
    {
        if (!string.IsNullOrWhiteSpace(value) && !string.Equals(value, "n/a", StringComparison.OrdinalIgnoreCase))
        {
            fields.Add(new SourceInvoiceField(label, value, isTotal));
        }
    }

    private static string CleanSourceValue(string value)
    {
        var cleaned = value.Trim();
        var mailtoIndex = cleaned.IndexOf("<mailto:", StringComparison.OrdinalIgnoreCase);
        return mailtoIndex >= 0 ? cleaned[..mailtoIndex].Trim() : cleaned;
    }

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

        if (Detail.UsesInternalAccounting)
        {
            return IsBillProposalApproved
                ? "Next step: continue under Supplier bills to prepare the native accounting entry."
                : "Next step: approve the invoice, then prepare it in Virtual Company accounting.";
        }

        if (!Detail.CanUseFortnoxAccounting)
        {
            return $"Next step: {Detail.AccountingGuidance}";
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

    private string BuildReviewDescription() =>
        Detail?.UsesInternalAccounting == true
            ? "Review Laura's extraction and approve the supplier invoice for accounting in Virtual Company."
            : Detail?.CanUseFortnoxAccounting == true
                ? FinanceText["BillReviewDescription"]
                : "Review Laura's extraction and decide the next accounting step.";

    private string BuildReviewDecisionHelp()
    {
        if (Detail?.UsesInternalAccounting == true)
        {
            return IsBillProposalApproved
                ? "This supplier invoice is approved. Continue under Supplier bills to prepare and post it in Virtual Company."
                : "Approve this supplier invoice for accounting in Virtual Company. Nothing will be sent to Fortnox.";
        }

        if (Detail?.CanUseFortnoxAccounting == false)
        {
            return Detail.AccountingGuidance;
        }

        return IsBillProposalApproved
            ? FinanceText["ApprovedSendHelp"]
            : FinanceText["ChooseReviewDecisionHelp"];
    }

    private string BuildSupplierBillsHref() =>
        Detail?.OperationalBillId is Guid operationalBillId
            ? FinanceRoutes.BuildBillDetailPath(operationalBillId, AccessState.CompanyId)
            : FinanceRoutes.WithCompanyContext(FinanceRoutes.SupplierBills, AccessState.CompanyId);

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

    private static string BuildBillStatusClass(string status) =>
        $"bill-status-badge {ResolveBillToneClass(status)}";

    private static string BuildFortnoxStatusClass(string status) =>
        $"bill-status-badge {ResolveFortnoxToneClass(status)}";

    private static string BuildApprovalStatusClass(string status) =>
        $"bill-status-badge {ResolveApprovalToneClass(status)}";

    private static string ResolveApprovalToneClass(string status) =>
        status.ToLowerInvariant() switch
        {
            "approved" => "bill-status-badge--success",
            "rejected" => "bill-status-badge--danger",
            "pending" => "bill-status-badge--warning",
            _ => "bill-status-badge--neutral"
        };

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

    private sealed record SourceInvoiceField(string Label, string Value, bool IsTotal = false);

    private sealed record SourceInvoicePresentation(
        IReadOnlyList<SourceInvoiceField> SupplierFields,
        IReadOnlyList<SourceInvoiceField> InvoiceFields,
        IReadOnlyList<SourceInvoiceField> ServiceFields,
        IReadOnlyList<SourceInvoiceField> PaymentFields,
        string InvoiceNumber,
        string TotalAmount,
        bool HasStructuredContent);
}
