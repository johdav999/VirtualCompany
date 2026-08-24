using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class SupplierSubscriptionsPage : FinancePageBase
{
    [Inject] private FinanceApiClient FinanceApiClient { get; set; } = default!;
    [Inject] private ILogger<SupplierSubscriptionsPage> Logger { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "proposalId")]
    public Guid? ProposalId { get; set; }

    private IReadOnlyList<SupplierSubscriptionSummaryResponse> Subscriptions { get; set; } = [];
    private SupplierSubscriptionDetailResponse? SelectedSubscription { get; set; }
    private IReadOnlyList<SupplierSubscriptionIntakeProposalSummaryResponse> Proposals { get; set; } = [];
    private SupplierSubscriptionIntakeProposalDetailResponse? SelectedProposal { get; set; }
    private bool IsListLoading { get; set; }
    private bool IsDetailLoading { get; set; }
    private bool IsEditing { get; set; }
    private bool IsSaving { get; set; }
    private bool IsLifecycleSubmitting { get; set; }
    private bool IsMatchSubmitting { get; set; }
    private string? ListErrorMessage { get; set; }
    private string? DetailErrorMessage { get; set; }
    private string? FormErrorMessage { get; set; }
    private string SearchText { get; set; } = string.Empty;
    private string StatusFilter { get; set; } = string.Empty;
    private Guid? EditingSubscriptionId { get; set; }
    private SupplierSubscriptionFormModel Form { get; set; } = SupplierSubscriptionFormModel.CreateDefault();
    private IReadOnlyList<FinanceCounterpartyResponse> Suppliers { get; set; } = [];
    private bool IsSuppliersLoading { get; set; }
    private bool IsProposalLoading { get; set; }
    private bool IsProposalSubmitting { get; set; }
    private bool IsSupplierPickerOpen { get; set; }
    private string? SuppliersErrorMessage { get; set; }
    private string? ProposalErrorMessage { get; set; }
    private string SupplierSearchText { get; set; } = string.Empty;
    private string ProposalSupplierSearchText { get; set; } = string.Empty;
    private bool IsProposalSupplierPickerOpen { get; set; }
    private SupplierSubscriptionProposalReviewModel ProposalForm { get; set; } = SupplierSubscriptionProposalReviewModel.CreateDefault();

    private IReadOnlyList<SupplierSubscriptionSummaryResponse> FilteredSubscriptions =>
        Subscriptions
            .Where(x => string.IsNullOrWhiteSpace(StatusFilter) || string.Equals(x.Status, StatusFilter, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(SearchText) || x.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) || x.SupplierName.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Health == "needs_review")
            .ThenByDescending(x => x.Health == "missing_bill")
            .ThenBy(x => x.NextExpectedBillDateUtc)
            .ToList();

    private IReadOnlyList<SupplierSubscriptionIntakeProposalSummaryResponse> ReviewProposals =>
        Proposals.Where(x => x.Status is "needs_review" or "detected" or "failed").OrderByDescending(x => x.ConfidenceScore).ThenByDescending(x => x.CreatedUtc).ToList();

    private IReadOnlyList<SupplierSubscriptionMatchResponse> ReviewMatches =>
        SelectedSubscription?.Matches.Where(x => x.Status is "suggested" or "exception").ToList() ?? [];

    private FinanceCounterpartyResponse? SelectedSupplier =>
        Suppliers.FirstOrDefault(x => x.Id == Form.CounterpartyId);

    private FinanceCounterpartyResponse? SelectedProposalSupplier =>
        ProposalForm.CounterpartyId is Guid counterpartyId
            ? Suppliers.FirstOrDefault(x => x.Id == counterpartyId)
            : null;

    private IReadOnlyList<FinanceCounterpartyResponse> FilteredSupplierOptions
    {
        get
        {
            var query = SupplierSearchText.Trim();
            var matches = string.IsNullOrWhiteSpace(query)
                ? Suppliers
                : Suppliers.Where(x =>
                    x.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (x.TaxId?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (x.Email?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    x.Id.ToString().Contains(query, StringComparison.OrdinalIgnoreCase));

            return matches
                .OrderBy(x => x.Name)
                .Take(8)
                .ToList();
        }
    }

    private IReadOnlyList<FinanceCounterpartyResponse> FilteredProposalSupplierOptions
    {
        get
        {
            var query = ProposalSupplierSearchText.Trim();
            var matches = string.IsNullOrWhiteSpace(query)
                ? Suppliers
                : Suppliers.Where(x =>
                    x.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (x.TaxId?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (x.Email?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));

            return matches
                .OrderBy(x => x.Name)
                .Take(8)
                .ToList();
        }
    }

    private bool CanAcceptSelectedProposal =>
        SelectedProposal is not null &&
        ProposalForm.CounterpartyId is Guid &&
        ProposalForm.ExpectedAmount is > 0 &&
        !string.IsNullOrWhiteSpace(ProposalForm.Currency) &&
        !string.IsNullOrWhiteSpace(ProposalForm.Cadence) &&
        ProposalForm.BillingDay is >= 1 and <= 31 &&
        ProposalForm.StartDateUtc.HasValue &&
        ProposalForm.NextExpectedBillDateUtc.HasValue;
    private IReadOnlyList<LifecycleAction> AvailableLifecycleActions => SelectedSubscription?.Status switch
    {
        "draft" => [new("activate")],
        "active" => [new("pause"), new("cancel")],
        "paused" => [new("resume"), new("cancel")],
        _ => []
    };

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();
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
            await LoadAsync(companyId, SelectedSubscription?.Id);
        }
    }

    private async Task LoadAsync(Guid companyId, Guid? selectedId = null)
    {
        IsListLoading = true;
        ListErrorMessage = null;
        DetailErrorMessage = null;
        try
        {
            await LoadSuppliersAsync(companyId);
            await LoadProposalsAsync(companyId, ProposalId);
            Subscriptions = await FinanceApiClient.GetSupplierSubscriptionsAsync(companyId);
            var id = selectedId ?? SelectedSubscription?.Id ?? Subscriptions.FirstOrDefault()?.Id;
            if (id is Guid subscriptionId)
            {
                await SelectSubscriptionAsync(subscriptionId);
            }
            else
            {
                SelectedSubscription = null;
            }
        }
        catch (FinanceApiException ex)
        {
            ListErrorMessage = ex.Message;
            Logger.LogWarning(ex, "Supplier subscriptions failed to load for company {CompanyId}.", companyId);
        }
        finally
        {
            IsListLoading = false;
        }
    }

    private async Task LoadProposalsAsync(Guid companyId, Guid? selectedProposalId = null)
    {
        IsProposalLoading = true;
        ProposalErrorMessage = null;
        try
        {
            Proposals = await FinanceApiClient.GetSupplierSubscriptionProposalsAsync(companyId);
            var proposalId = selectedProposalId ?? SelectedProposal?.Id ?? ReviewProposals.FirstOrDefault()?.Id;
            if (proposalId is Guid id)
            {
                await SelectProposalAsync(id);
            }
            else
            {
                SelectedProposal = null;
                ProposalForm = SupplierSubscriptionProposalReviewModel.CreateDefault();
            }
        }
        catch (FinanceApiException ex)
        {
            Proposals = [];
            ProposalErrorMessage = ex.Message;
            Logger.LogWarning(ex, "Supplier subscription proposals failed to load for company {CompanyId}.", companyId);
        }
        finally
        {
            IsProposalLoading = false;
        }
    }
    private async Task LoadSuppliersAsync(Guid companyId)
    {
        IsSuppliersLoading = true;
        SuppliersErrorMessage = null;
        try
        {
            Suppliers = await FinanceApiClient.GetSuppliersAsync(companyId, 500);
        }
        catch (FinanceApiException ex)
        {
            Suppliers = [];
            SuppliersErrorMessage = ex.Message;
            Logger.LogWarning(ex, "Suppliers failed to load for company {CompanyId}.", companyId);
        }
        finally
        {
            IsSuppliersLoading = false;
        }
    }

    private async Task SelectProposalAsync(Guid proposalId)
    {
        if (AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        IsProposalLoading = true;
        ProposalErrorMessage = null;
        try
        {
            SelectedProposal = await FinanceApiClient.GetSupplierSubscriptionProposalAsync(companyId, proposalId);
            ProposalForm = SupplierSubscriptionProposalReviewModel.From(SelectedProposal, FinanceText["SubscriptionProposal"]);
            ProposalSupplierSearchText = SelectedProposal?.SupplierName ?? string.Empty;
            IsProposalSupplierPickerOpen = SelectedProposal?.Terms.CounterpartyId is null;
        }
        catch (FinanceApiException ex)
        {
            ProposalErrorMessage = ex.Message;
        }
        finally
        {
            IsProposalLoading = false;
        }
    }

    private async Task AcceptProposalAsync(Guid proposalId)
    {
        if (AccessState.CompanyId is not Guid companyId || SelectedProposal is null)
        {
            return;
        }

        IsProposalSubmitting = true;
        ProposalErrorMessage = null;
        try
        {
            SelectedSubscription = await FinanceApiClient.AcceptSupplierSubscriptionProposalAsync(
                companyId,
                proposalId,
                new AcceptSupplierSubscriptionProposalRequest(ProposalForm.ToRequest(), FinanceText["SubscriptionAcceptedFromEvidence"]));
            SelectedProposal = null;
            await LoadAsync(companyId, SelectedSubscription.Id);
        }
        catch (FinanceApiException ex)
        {
            ProposalErrorMessage = ex.Message;
        }
        finally
        {
            IsProposalSubmitting = false;
        }
    }

    private async Task RejectProposalAsync(Guid proposalId)
    {
        if (AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        IsProposalSubmitting = true;
        ProposalErrorMessage = null;
        try
        {
            await FinanceApiClient.RejectSupplierSubscriptionProposalAsync(companyId, proposalId, new RejectSupplierSubscriptionProposalRequest(FinanceText["NotSupplierSubscriptionAgreement"]));
            SelectedProposal = null;
            await LoadProposalsAsync(companyId);
        }
        catch (FinanceApiException ex)
        {
            ProposalErrorMessage = ex.Message;
        }
        finally
        {
            IsProposalSubmitting = false;
        }
    }

    private async Task RetryProposalAsync(Guid proposalId)
    {
        if (AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        IsProposalSubmitting = true;
        ProposalErrorMessage = null;
        try
        {
            SelectedProposal = await FinanceApiClient.RetrySupplierSubscriptionProposalAsync(companyId, proposalId);
            await LoadProposalsAsync(companyId);
        }
        catch (FinanceApiException ex)
        {
            ProposalErrorMessage = ex.Message;
        }
        finally
        {
            IsProposalSubmitting = false;
        }
    }
    private async Task SelectSubscriptionAsync(Guid subscriptionId)
    {
        if (AccessState.CompanyId is not Guid companyId || IsEditing)
        {
            return;
        }

        IsDetailLoading = true;
        DetailErrorMessage = null;
        try
        {
            SelectedSubscription = await FinanceApiClient.GetSupplierSubscriptionAsync(companyId, subscriptionId);
        }
        catch (FinanceApiException ex)
        {
            DetailErrorMessage = ex.Message;
            Logger.LogWarning(ex, "Supplier subscription detail failed to load for company {CompanyId}, subscription {SubscriptionId}.", companyId, subscriptionId);
        }
        finally
        {
            IsDetailLoading = false;
        }
    }

    private void StartCreate()
    {
        EditingSubscriptionId = null;
        Form = SupplierSubscriptionFormModel.CreateDefault();
        SupplierSearchText = string.Empty;
        IsSupplierPickerOpen = false;
        FormErrorMessage = null;
        IsEditing = true;
    }

    private void StartEdit()
    {
        if (SelectedSubscription is null)
        {
            return;
        }

        EditingSubscriptionId = SelectedSubscription.Id;
        Form = SupplierSubscriptionFormModel.From(SelectedSubscription);
        SupplierSearchText = SelectedSubscription.SupplierName;
        IsSupplierPickerOpen = false;
        FormErrorMessage = null;
        IsEditing = true;
    }

    private void CancelEdit()
    {
        IsEditing = false;
        FormErrorMessage = null;
        IsSupplierPickerOpen = false;
    }

    private async Task SaveAsync()
    {
        if (AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        IsSaving = true;
        FormErrorMessage = null;
        try
        {
            if (Form.CounterpartyId == Guid.Empty)
            {
                FormErrorMessage = FinanceText["ChooseSupplierBeforeSavingSubscription"];
                return;
            }

            var request = Form.ToRequest();
            SelectedSubscription = EditingSubscriptionId is Guid id
                ? await FinanceApiClient.UpdateSupplierSubscriptionAsync(companyId, id, request)
                : await FinanceApiClient.CreateSupplierSubscriptionAsync(companyId, request);
            IsEditing = false;
            await LoadAsync(companyId, SelectedSubscription.Id);
        }
        catch (FinanceApiException ex)
        {
            FormErrorMessage = ex.Message;
        }
        finally
        {
            IsSaving = false;
        }
    }

    private void ShowSupplierPicker()
    {
        IsSupplierPickerOpen = true;
    }

    private void SelectSupplier(FinanceCounterpartyResponse supplier)
    {
        Form.CounterpartyId = supplier.Id;
        SupplierSearchText = supplier.Name;
        IsSupplierPickerOpen = false;
    }

    private void ClearSupplier()
    {
        Form.CounterpartyId = Guid.Empty;
        SupplierSearchText = string.Empty;
        IsSupplierPickerOpen = true;
    }
    private void ShowProposalSupplierPicker()
    {
        IsProposalSupplierPickerOpen = true;
    }

    private void SelectProposalSupplier(FinanceCounterpartyResponse supplier)
    {
        ProposalForm.CounterpartyId = supplier.Id;
        ProposalSupplierSearchText = supplier.Name;
        IsProposalSupplierPickerOpen = false;
    }

    private void ClearProposalSupplier()
    {
        ProposalForm.CounterpartyId = null;
        ProposalSupplierSearchText = string.Empty;
        IsProposalSupplierPickerOpen = true;
    }

    private string SupplierMeta(FinanceCounterpartyResponse supplier)
    {
        var parts = new[] { supplier.TaxId, supplier.Email, supplier.PaymentTerms }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        return parts.Length == 0 ? FinanceText["Supplier"] : string.Join(" - ", parts);
    }

    private async Task ChangeStatusAsync(string action)
    {
        if (AccessState.CompanyId is not Guid companyId || SelectedSubscription is null)
        {
            return;
        }

        IsLifecycleSubmitting = true;
        DetailErrorMessage = null;
        try
        {
            SelectedSubscription = await FinanceApiClient.ChangeSupplierSubscriptionStatusAsync(companyId, SelectedSubscription.Id, action);
            await LoadAsync(companyId, SelectedSubscription.Id);
        }
        catch (FinanceApiException ex)
        {
            DetailErrorMessage = ex.Message;
        }
        finally
        {
            IsLifecycleSubmitting = false;
        }
    }

    private async Task DecideMatchAsync(Guid matchId, bool confirm)
    {
        if (AccessState.CompanyId is not Guid companyId || SelectedSubscription is null)
        {
            return;
        }

        IsMatchSubmitting = true;
        DetailErrorMessage = null;
        try
        {
            if (confirm)
            {
                await FinanceApiClient.ConfirmSupplierSubscriptionMatchAsync(companyId, matchId);
            }
            else
            {
                await FinanceApiClient.RejectSupplierSubscriptionMatchAsync(companyId, matchId);
            }

            await LoadAsync(companyId, SelectedSubscription.Id);
        }
        catch (FinanceApiException ex)
        {
            DetailErrorMessage = ex.Message;
        }
        finally
        {
            IsMatchSubmitting = false;
        }
    }

    private string FormatOptionalMoney(decimal? amount, string? currency) =>
        amount.HasValue ? string.Format(CultureInfo.CurrentCulture, "{0:N2} {1}", amount.Value, currency ?? "SEK") : FinanceText["NeedsReview"];

    private string FormatOptionalDate(DateTime? value) =>
        value.HasValue ? FormatDate(value.Value) : FinanceText["NeedsReview"];

    private static string FormatMoney(decimal amount, string currency) =>
        string.Format(CultureInfo.CurrentCulture, "{0:N2} {1}", amount, currency);

    private static string FormatDate(DateTime value) => value.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture);

    private string Label(string value) => FinanceText[value.Trim().ToLowerInvariant() switch
    {
        "active" => "Active",
        "activate" => "Activate",
        "accepted" => "Accepted",
        "cancel" => "Cancel",
        "cancelled" => "Cancelled",
        "confirmed" => "Confirmed",
        "detected" => "Detected",
        "draft" => "Draft",
        "due" => "Due",
        "exception" => "Exception",
        "failed" => "Failed",
        "missing_bill" => "MissingBill",
        "monthly" => "Monthly",
        "needs_review" => "NeedsReview",
        "pause" => "Pause",
        "paused" => "Paused",
        "quarterly" => "Quarterly",
        "rejected" => "Rejected",
        "resume" => "Resume",
        "suggested" => "Suggested",
        "upcoming" => "Upcoming",
        "yearly" => "Yearly",
        _ => "Unknown"
    }];

    private static string Tone(string value) => value switch
    {
        "active" or "upcoming" or "confirmed" or "accepted" => "success",
        "due" or "needs_review" or "suggested" or "detected" => "warning",
        "missing_bill" or "exception" or "cancelled" or "failed" or "rejected" => "danger",
        _ => "neutral"
    };

    private static string Initials(string value)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Concat(parts.Take(2).Select(x => char.ToUpperInvariant(x[0])));
    }

    private sealed record LifecycleAction(string Value);

    private sealed class SupplierSubscriptionProposalReviewModel
    {
        public Guid? CounterpartyId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Currency { get; set; } = "SEK";
        public decimal? ExpectedAmount { get; set; }
        public string Cadence { get; set; } = "monthly";
        public int? BillingDay { get; set; }
        public DateTime? StartDateUtc { get; set; }
        public DateTime? NextExpectedBillDateUtc { get; set; }
        public decimal? AmountTolerance { get; set; }
        public int? DateToleranceDays { get; set; }
        public DateTime? EndDateUtc { get; set; }
        public string? ContractReference { get; set; }
        public string? Description { get; set; }
        public int? NoticePeriodDays { get; set; }
        public bool AutoRenews { get; set; } = true;
        public Guid? ContractDocumentId { get; set; }

        public static SupplierSubscriptionProposalReviewModel CreateDefault() => new()
        {
            BillingDay = DateTime.UtcNow.Day,
            StartDateUtc = DateTime.UtcNow.Date,
            NextExpectedBillDateUtc = DateTime.UtcNow.Date,
            AmountTolerance = 0,
            DateToleranceDays = 5,
            NoticePeriodDays = 30
        };

        public static SupplierSubscriptionProposalReviewModel From(SupplierSubscriptionIntakeProposalDetailResponse? proposal, string defaultName) => proposal is null
            ? CreateDefault()
            : new SupplierSubscriptionProposalReviewModel
            {
                CounterpartyId = proposal.Terms.CounterpartyId,
                Name = string.IsNullOrWhiteSpace(proposal.Terms.Name) ? defaultName : proposal.Terms.Name!,
                Currency = string.IsNullOrWhiteSpace(proposal.Terms.Currency) ? "SEK" : proposal.Terms.Currency!,
                ExpectedAmount = proposal.Terms.ExpectedAmount,
                Cadence = string.IsNullOrWhiteSpace(proposal.Terms.Cadence) ? "monthly" : proposal.Terms.Cadence!,
                BillingDay = proposal.Terms.BillingDay,
                StartDateUtc = proposal.Terms.StartDateUtc?.Date ?? DateTime.UtcNow.Date,
                NextExpectedBillDateUtc = proposal.Terms.NextExpectedBillDateUtc?.Date ?? DateTime.UtcNow.Date,
                AmountTolerance = proposal.Terms.AmountTolerance ?? 0,
                DateToleranceDays = proposal.Terms.DateToleranceDays ?? 5,
                EndDateUtc = proposal.Terms.EndDateUtc?.Date,
                ContractReference = proposal.Terms.ContractReference,
                Description = proposal.Terms.Description,
                NoticePeriodDays = proposal.Terms.NoticePeriodDays ?? 30,
                AutoRenews = proposal.Terms.AutoRenews ?? true,
                ContractDocumentId = proposal.Terms.ContractDocumentId
            };

        public SupplierSubscriptionProposalTermsRequest ToRequest() => new(
            CounterpartyId,
            Name,
            Currency,
            ExpectedAmount,
            Cadence,
            BillingDay,
            StartDateUtc.HasValue ? DateTime.SpecifyKind(StartDateUtc.Value.Date, DateTimeKind.Utc) : null,
            NextExpectedBillDateUtc.HasValue ? DateTime.SpecifyKind(NextExpectedBillDateUtc.Value.Date, DateTimeKind.Utc) : null,
            AmountTolerance,
            DateToleranceDays,
            EndDateUtc.HasValue ? DateTime.SpecifyKind(EndDateUtc.Value.Date, DateTimeKind.Utc) : null,
            ContractReference,
            Description,
            NoticePeriodDays,
            AutoRenews,
            ContractDocumentId);
    }

    private sealed class SupplierSubscriptionFormModel
    {
        public Guid CounterpartyId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Currency { get; set; } = "SEK";
        public decimal ExpectedAmount { get; set; }
        public string Cadence { get; set; } = "monthly";
        public int BillingDay { get; set; } = DateTime.UtcNow.Day;
        public DateTime StartDate { get; set; } = DateTime.UtcNow.Date;
        public DateTime NextExpectedBillDate { get; set; } = DateTime.UtcNow.Date;
        public decimal AmountTolerance { get; set; }
        public int DateToleranceDays { get; set; } = 5;
        public int NoticePeriodDays { get; set; } = 30;
        public string? ContractReference { get; set; }
        public string? Description { get; set; }
        public bool AutoRenews { get; set; } = true;

        public static SupplierSubscriptionFormModel CreateDefault() => new();

        public static SupplierSubscriptionFormModel From(SupplierSubscriptionDetailResponse subscription) => new()
        {
            CounterpartyId = subscription.CounterpartyId,
            Name = subscription.Name,
            Currency = subscription.Currency,
            ExpectedAmount = subscription.ExpectedAmount,
            Cadence = subscription.Cadence,
            BillingDay = subscription.BillingDay,
            StartDate = subscription.StartDateUtc.Date,
            NextExpectedBillDate = subscription.NextExpectedBillDateUtc.Date,
            AmountTolerance = subscription.AmountTolerance,
            DateToleranceDays = subscription.DateToleranceDays,
            NoticePeriodDays = subscription.NoticePeriodDays,
            ContractReference = subscription.ContractReference,
            Description = subscription.Description,
            AutoRenews = subscription.AutoRenews
        };

        public UpsertSupplierSubscriptionRequest ToRequest() => new(
            CounterpartyId,
            Name,
            Currency,
            ExpectedAmount,
            Cadence,
            BillingDay,
            DateTime.SpecifyKind(StartDate.Date, DateTimeKind.Utc),
            DateTime.SpecifyKind(NextExpectedBillDate.Date, DateTimeKind.Utc),
            AmountTolerance,
            DateToleranceDays,
            null,
            ContractReference,
            Description,
            NoticePeriodDays,
            AutoRenews,
            null);
    }
}




