using Microsoft.AspNetCore.Components;
using VirtualCompany.Shared;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class CustomerBillingPage : FinancePageBase
{
    [Inject] private FinanceApiClient FinanceApiClient { get; set; } = default!;
    [Parameter] public Guid CustomerId { get; set; }
    private FinanceCounterpartyResponse? Customer { get; set; }
    private CustomerBillingProfileResponse? Profile { get; set; }
    private BillingEditor Editor { get; set; } = new();
    private IReadOnlyList<CustomerBillingProfileVersionResponse> History { get; set; } = [];
    private IReadOnlyList<CustomerDuplicateCandidateResponse> DuplicateCandidates { get; set; } = [];
    private string? ValidationMessage { get; set; }
    private string? ActionMessage { get; set; }
    private string GovernanceReason { get; set; } = string.Empty;
    private Guid? MergeCandidateId { get; set; }
    private bool IsBusy { get; set; }
    private Guid? _loadedCustomer;
    private bool CanManage => FinanceAccess.CanEdit(AccessState.MembershipRole);
    private IReadOnlyList<CustomerBillingSourceConflictResponse> OpenConflicts => Profile?.Conflicts.Where(x => x.Status == "pending").ToArray() ?? [];
    private IReadOnlyList<CustomerDuplicateCandidateResponse> RelatedDuplicateCandidates => DuplicateCandidates.Where(x => x.Status == "pending" && (x.FirstCounterpartyId == CustomerId || x.SecondCounterpartyId == CustomerId)).ToArray();
    private string VerificationExplanation => Editor.IdentityValidationState switch { "externally_verified" => FinanceText["ExternallyVerifiedHelp"], "user_attested" => FinanceText["ConfirmedByYourTeamHelp"], _ => FinanceText["FormatCheckedHelp"] };

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();
        if (!AccessState.IsAllowed || AccessState.CompanyId is not Guid companyId || _loadedCustomer == CustomerId) return;
        _loadedCustomer = CustomerId; await LoadAsync(companyId);
    }

    private async Task LoadAsync(Guid companyId)
    {
        ValidationMessage = null;
        try
        {
            var customerTask = FinanceApiClient.GetCustomerAsync(companyId, CustomerId);
            var profileTask = FinanceApiClient.GetCustomerBillingProfileAsync(companyId, CustomerId);
            var historyTask = FinanceApiClient.GetCustomerBillingProfileHistoryAsync(companyId, CustomerId);
            var duplicateTask = FinanceApiClient.GetCustomerDuplicateCandidatesAsync(companyId, "pending", 200);
            await Task.WhenAll(customerTask, profileTask, historyTask, duplicateTask);
            Customer = await customerTask; Profile = await profileTask; History = await historyTask; DuplicateCandidates = await duplicateTask;
            if (Customer is null) { ValidationMessage = FinanceText["CustomerNotFound"]; return; }
            Editor = Profile is null ? BillingEditor.Create(Customer) : BillingEditor.From(Profile.Profile);
        }
        catch (FinanceApiException ex) { ValidationMessage = ex.Message; }
    }

    private async Task SaveAsync()
    {
        if (!CanManage || AccessState.CompanyId is not Guid companyId) return;
        ValidationMessage = null; ActionMessage = null; IsBusy = true;
        try
        {
            Editor.Validate();
            Profile = await FinanceApiClient.UpsertCustomerBillingProfileAsync(companyId, CustomerId, new(Editor.ToInput(), Profile?.Version));
            Editor = BillingEditor.From(Profile.Profile); ActionMessage = FinanceText["CustomerBillingSaved"]; History = await FinanceApiClient.GetCustomerBillingProfileHistoryAsync(companyId, CustomerId);
        }
        catch (Exception ex) when (ex is FinanceApiException or InvalidOperationException) { ValidationMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    private async Task ResolveConflictAsync(CustomerBillingSourceConflictResponse conflict, bool useIncoming)
    {
        if (!CanManage || Profile is null || AccessState.CompanyId is not Guid companyId) return;
        if (string.IsNullOrWhiteSpace(GovernanceReason)) { ValidationMessage = FinanceText["DecisionReasonRequired"]; return; }
        IsBusy = true; ValidationMessage = null;
        try { Profile = await FinanceApiClient.ResolveCustomerBillingSourceConflictAsync(companyId, conflict.Id, new(conflict.Version, Profile.Version, useIncoming, GovernanceReason.Trim())); Editor = BillingEditor.From(Profile.Profile); GovernanceReason = string.Empty; ActionMessage = FinanceText["SourceConflictResolved"]; }
        catch (FinanceApiException ex) { ValidationMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    private async Task DecideDuplicateAsync(CustomerDuplicateCandidateResponse candidate, string decision, Guid? mergeSource, Guid? mergeTarget)
    {
        if (!CanManage || AccessState.CompanyId is not Guid companyId) return;
        if (string.IsNullOrWhiteSpace(GovernanceReason)) { ValidationMessage = FinanceText["DecisionReasonRequired"]; return; }
        IsBusy = true; ValidationMessage = null;
        try { await FinanceApiClient.DecideCustomerDuplicateAsync(companyId, candidate.Id, new(candidate.Version, decision, mergeSource, mergeTarget, GovernanceReason.Trim())); GovernanceReason = string.Empty; MergeCandidateId = null; ActionMessage = decision == "merge" ? FinanceText["CustomersMerged"] : FinanceText["CustomersKeptSeparate"]; DuplicateCandidates = await FinanceApiClient.GetCustomerDuplicateCandidatesAsync(companyId, "pending", 200); }
        catch (FinanceApiException ex) { ValidationMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    private string FriendlySource(string? source) => source switch { "user" or "user_entry" => FinanceText["EnteredByYourTeam"], "provider" or "provider_sync" => FinanceText["AccountingProvider"], "migration" => FinanceText["ImportedDuringMigration"], "merge" or "approved_merge" => FinanceText["ApprovedCustomerMerge"], _ => FinanceText["UnknownSource"] };
    private string FriendlyVerification(string? state) => state switch { "externally_verified" => FinanceText["ExternallyVerified"], "user_attested" => FinanceText["ConfirmedByYourTeam"], _ => FinanceText["FormatChecked"] };
    private static string FriendlyField(string value) => string.Join(' ', value.Split(['_', '.'], StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    private sealed class BillingEditor
    {
        public string LegalName { get; set; } = string.Empty; public string? DisplayName { get; set; } public string PartyKind { get; set; } = "organization"; public string? TaxIdentifier { get; set; } public string? VatIdentifier { get; set; } public string IdentityValidationState { get; set; } = "user_attested";
        public string AddressLine1 { get; set; } = string.Empty; public string PostalCode { get; set; } = string.Empty; public string City { get; set; } = string.Empty; public string CountryCode { get; set; } = "SE"; public string LanguageCode { get; set; } = "sv-SE"; public string CurrencyCode { get; set; } = "SEK";
        public int PaymentTermDays { get; set; } = 30; public string PaymentMethod { get; set; } = "bank_transfer"; public string InvoiceDeliveryChannel { get; set; } = "email"; public string? InvoiceDeliveryEmail { get; set; } public string? BuyerReference { get; set; } public string? EInvoiceIdentifier { get; set; }
        public decimal CreditLimit { get; set; } public string CreditStatus { get; set; } = "active"; public string? DefaultAccountMapping { get; set; } = "1510"; public string? DefaultDimensionCode { get; set; } public DateOnly EffectiveFrom { get; set; } = DateOnly.FromDateTime(DateTime.Today); public DateOnly? EffectiveTo { get; set; }
        public string SourceKind { get; set; } = "user"; public string? SourceReference { get; set; }

        public static BillingEditor Create(FinanceCounterpartyResponse customer) => new() { LegalName = customer.Name, DisplayName = customer.Name, TaxIdentifier = customer.TaxId, InvoiceDeliveryEmail = customer.Email, CreditLimit = customer.CreditLimit ?? 0, PaymentMethod = customer.PreferredPaymentMethod ?? "bank_transfer", DefaultAccountMapping = customer.DefaultAccountMapping ?? "1510" };
        public static BillingEditor From(CustomerBillingProfileInputResponse value) => new() { LegalName = value.LegalName, DisplayName = value.DisplayName, PartyKind = value.PartyKind, TaxIdentifier = value.TaxIdentifier, VatIdentifier = value.VatIdentifier, IdentityValidationState = value.IdentityValidationState, AddressLine1 = value.BillingAddress.Line1, PostalCode = value.BillingAddress.PostalCode, City = value.BillingAddress.City, CountryCode = value.BillingAddress.CountryCode, LanguageCode = value.LanguageCode, CurrencyCode = value.CurrencyCode, PaymentTermDays = value.PaymentTermDays, PaymentMethod = value.PaymentMethod, InvoiceDeliveryChannel = value.InvoiceDeliveryChannel, InvoiceDeliveryEmail = value.InvoiceDeliveryEmail, BuyerReference = value.BuyerReference, EInvoiceIdentifier = value.EInvoiceIdentifier, CreditLimit = value.CreditLimit, CreditStatus = value.CreditStatus, DefaultAccountMapping = value.DefaultAccountMapping, DefaultDimensionCode = value.DefaultDimensionCode, EffectiveFrom = value.EffectiveFrom, EffectiveTo = value.EffectiveTo, SourceKind = value.SourceKind, SourceReference = value.SourceReference };
        public void Validate() { if (string.IsNullOrWhiteSpace(LegalName) || string.IsNullOrWhiteSpace(AddressLine1) || string.IsNullOrWhiteSpace(PostalCode) || string.IsNullOrWhiteSpace(City)) throw new InvalidOperationException("Complete the legal name and billing address before saving."); if (CountryCode.Trim().Length != 2 || CurrencyCode.Trim().Length != 3) throw new InvalidOperationException("Use a two-letter country and three-letter currency code."); if (PaymentTermDays < 0) throw new InvalidOperationException("Payment terms cannot be negative."); }
        public CustomerBillingProfileInputResponse ToInput() => new(LegalName.Trim(), DisplayName?.Trim(), PartyKind, TaxIdentifier?.Trim(), VatIdentifier?.Trim(), IdentityValidationState, new(AddressLine1.Trim(), null, PostalCode.Trim(), City.Trim(), null, CountryCode.Trim().ToUpperInvariant()), null, LanguageCode, CurrencyCode.Trim().ToUpperInvariant(), "fixed_days", PaymentTermDays, PaymentMethod, InvoiceDeliveryChannel, InvoiceDeliveryEmail?.Trim(), BuyerReference?.Trim(), EInvoiceIdentifier?.Trim(), string.IsNullOrWhiteSpace(EInvoiceIdentifier) ? null : "peppol", CreditLimit, CreditStatus, DefaultAccountMapping?.Trim(), DefaultDimensionCode?.Trim(), EffectiveFrom, EffectiveTo, SourceKind, SourceReference, IdentityValidationState == "user_attested" ? DateTime.UtcNow : null, null, null);
    }
}
