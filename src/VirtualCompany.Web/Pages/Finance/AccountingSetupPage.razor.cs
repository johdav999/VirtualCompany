using System.Globalization;
using Microsoft.AspNetCore.Components;
using VirtualCompany.Shared;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class AccountingSetupPage : FinancePageBase
{
    [Inject] private FinanceApiClient FinanceApiClient { get; set; } = default!;

    private static readonly IReadOnlyList<SetupStep> StandardSteps =
    [
        new(1, "AccountingBasicsStep"), new(2, "AccountingAccountsStep"),
        new(3, "AccountingPeriodsStep"), new(4, "AccountingReviewStep")
    ];
    private static readonly IReadOnlyList<SetupStep> SwedishSteps =
    [
        new(1, "LegalIdentityStep"), new(2, "AccountingSettingsStep"),
        new(3, "SwedishChartStep"), new(4, "VatRegistrationStep"),
        new(5, "DocumentSeriesStep"), new(6, "AccountingReviewStep")
    ];

    private IReadOnlyList<AccountingPolicyPackOptionResponse> PolicyPacks { get; set; } = [];
    private IReadOnlyList<StatutoryDocumentSeriesResponse> DocumentSeries { get; set; } = [];
    private AccountingSetupStatusResponse? SetupStatus { get; set; }
    private AccountingSetupPreviewResponse? Preview { get; set; }
    private SetupDraft Draft { get; set; } = SetupDraft.CreateDefault();
    private StatutoryProfileDraft ProfileDraft { get; set; } = StatutoryProfileDraft.CreateDefault();
    private DocumentSeriesDraft SeriesDraft { get; set; } = DocumentSeriesDraft.CreateDefault();
    private int CurrentStep { get; set; } = 1;
    private int HighestAvailableStep { get; set; } = 1;
    private bool IsPreviewLoading { get; set; }
    private bool IsSaving { get; set; }
    private bool IsEditingSwedishSetup { get; set; }
    private string? PreviewError { get; set; }
    private string? ActionError { get; set; }
    private string? SuccessMessage { get; set; }

    private bool CanManageAccounting => FinanceAccess.CanManageAccounting(AccessState.MembershipRole);
    private IReadOnlyList<SetupStep> Steps => IsSwedishExperience ? SwedishSteps : StandardSteps;
    private bool IsSwedishExperience =>
        string.Equals(SelectedPack?.CountryOrRegion, "SE", StringComparison.OrdinalIgnoreCase) ||
        SetupStatus?.MissingLegalFacts.Count > 0 ||
        SetupStatus?.StatutoryProfile?.Exists == true ||
        PolicyPacks.Any(pack => string.Equals(pack.CountryOrRegion, "SE", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(pack.PackKey, SetupStatus?.Configuration?.PolicyPackKey, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(pack.PackVersion, SetupStatus?.Configuration?.PolicyPackVersion, StringComparison.OrdinalIgnoreCase));
    private bool ShowSetupEditor => SetupStatus?.IsConfigured != true || IsEditingSwedishSetup;
    private int AccountingSettingsStep => IsSwedishExperience ? 2 : 1;
    private int ChartStep => IsSwedishExperience ? 3 : 2;
    private int PeriodsOrSeriesStep => IsSwedishExperience ? 5 : 3;
    private bool CanCompleteSetup => SetupStatus?.IsConfigured != true && Preview?.IsValid == true;
    private string DisplayPolicyName => PolicyPacks.FirstOrDefault(pack =>
        string.Equals(pack.PackKey, SetupStatus?.Configuration?.PolicyPackKey, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(pack.PackVersion, SetupStatus?.Configuration?.PolicyPackVersion, StringComparison.OrdinalIgnoreCase))?.DisplayName
        ?? FinanceText["AccountingPolicyUnavailable"];
    private IReadOnlyList<AccountingChartTemplateOptionResponse> SelectedPackTemplates => SelectedPack?.ChartTemplates ?? [];
    private AccountingPolicyPackOptionResponse? SelectedPack
    {
        get
        {
            ParsePackIdentity(Draft.PolicyPackIdentity, out var key, out var version);
            return PolicyPacks.FirstOrDefault(pack => string.Equals(pack.PackKey, key, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(pack.PackVersion, version, StringComparison.OrdinalIgnoreCase));
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();
        if (AccessState.IsAllowed && AccessState.CompanyId is Guid companyId) await LoadAsync(companyId);
    }

    private async Task LoadAsync(Guid companyId)
    {
        ActionError = null;
        try
        {
            var packsTask = FinanceApiClient.GetAccountingPolicyPacksAsync(companyId);
            var statusTask = FinanceApiClient.GetAccountingSetupStatusAsync(companyId);
            var seriesTask = FinanceApiClient.GetStatutoryDocumentSeriesAsync(companyId);
            await Task.WhenAll(packsTask, statusTask, seriesTask);
            PolicyPacks = packsTask.Result;
            SetupStatus = statusTask.Result;
            DocumentSeries = seriesTask.Result;
            if (PolicyPacks.Count == 0) return;

            var configuredPack = PolicyPacks.FirstOrDefault(pack =>
                string.Equals(pack.PackKey, SetupStatus?.Configuration?.PolicyPackKey, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(pack.PackVersion, SetupStatus?.Configuration?.PolicyPackVersion, StringComparison.OrdinalIgnoreCase));
            var selected = configuredPack ??
                (SetupStatus?.MissingLegalFacts.Count > 0
                    ? PolicyPacks.FirstOrDefault(pack => string.Equals(pack.CountryOrRegion, "SE", StringComparison.OrdinalIgnoreCase))
                    : null) ?? PolicyPacks.FirstOrDefault(pack => pack.IsCountryNeutral) ?? PolicyPacks[0];

            Draft.PolicyPackIdentity = PackIdentity(selected);
            Draft.ChartTemplateKey = selected.ChartTemplates.FirstOrDefault()?.TemplateKey ?? string.Empty;
            if (SetupStatus?.Configuration is { } configuration)
            {
                Draft.BaseCurrency = configuration.BaseCurrency;
                Draft.FiscalYearStart = new DateOnly(DateTime.Today.Year, configuration.FiscalYearStartMonth,
                    Math.Min(configuration.FiscalYearStartDay, DateTime.DaysInMonth(DateTime.Today.Year, configuration.FiscalYearStartMonth)));
            }

            ProfileDraft = StatutoryProfileDraft.From(SetupStatus?.StatutoryProfile?.Profile);
            SeriesDraft.SetFiscalYear(Draft.FiscalYearStart);
            if (IsSwedishExperience && SetupStatus?.StatutoryProfile?.IsCompleteForSelectedPolicyPack != true)
                IsEditingSwedishSetup = true;

            if (SetupStatus?.IsConfigured != true) await ReloadPreviewAsync();
            else if (IsEditingSwedishSetup)
            {
                HighestAvailableStep = Steps.Count;
                await ReloadPreviewAsync();
            }
        }
        catch (FinanceApiException exception) { ActionError = exception.Message; }
    }

    private async Task ReloadAsync() { if (AccessState.CompanyId is Guid companyId) await LoadAsync(companyId); }

    private void BeginSwedishEdit()
    {
        IsEditingSwedishSetup = true;
        CurrentStep = SetupStatus?.StatutoryProfile?.IsFormatComplete == true ? 2 : 1;
        HighestAvailableStep = SetupStatus?.IsConfigured == true ? Steps.Count : Math.Max(HighestAvailableStep, CurrentStep);
    }

    private async Task PolicyPackChangedAsync()
    {
        Draft.ChartTemplateKey = SelectedPack?.ChartTemplates.FirstOrDefault()?.TemplateKey ?? string.Empty;
        if (IsSwedishExperience)
        {
            Draft.BaseCurrency = "SEK";
            ProfileDraft.AccountingCurrency = "SEK";
            ProfileDraft.CountryCode = "SE";
            ProfileDraft.RegisteredAddress.CountryCode = "SE";
            SeriesDraft.SetFiscalYear(Draft.FiscalYearStart);
        }
        await ReloadPreviewAsync();
    }

    private async Task ReloadPreviewAsync()
    {
        if (AccessState.CompanyId is not Guid companyId) return;
        IsPreviewLoading = true; PreviewError = null; ActionError = null;
        try
        {
            var selectedPack = SelectedPack ?? PolicyPacks.FirstOrDefault();
            if (selectedPack is null) throw new InvalidOperationException(FinanceText["NoAccountingPolicyAvailable"]);
            if (selectedPack.ChartTemplates.All(template => !string.Equals(template.TemplateKey, Draft.ChartTemplateKey, StringComparison.OrdinalIgnoreCase)))
                Draft.ChartTemplateKey = selectedPack.ChartTemplates.FirstOrDefault()?.TemplateKey ?? string.Empty;
            Preview = await FinanceApiClient.PreviewAccountingSetupAsync(companyId, BuildPreviewRequest(selectedPack));
        }
        catch (Exception exception) when (exception is FinanceApiException or InvalidOperationException)
        { Preview = null; PreviewError = exception.Message; ActionError = exception.Message; }
        finally { IsPreviewLoading = false; }
    }

    private async Task NextStepAsync()
    {
        if (IsSwedishExperience && CurrentStep is 1 or 4 && !await SaveStatutoryProfileAsync()) return;
        if (SetupStatus?.IsConfigured != true && CurrentStep >= AccountingSettingsStep)
        {
            await ReloadPreviewAsync();
            if (Preview is null) return;
        }
        CurrentStep = Math.Min(Steps.Count, CurrentStep + 1);
        HighestAvailableStep = Math.Max(HighestAvailableStep, CurrentStep);
    }

    private void PreviousStep() => CurrentStep = Math.Max(1, CurrentStep - 1);
    private void GoToStep(int step) { if (step <= HighestAvailableStep) CurrentStep = step; }

    private async Task<bool> SaveStatutoryProfileAsync()
    {
        if (!CanManageAccounting || AccessState.CompanyId is not Guid companyId) return false;
        IsSaving = true; ActionError = null; SuccessMessage = null;
        try
        {
            var request = ProfileDraft.ToRequest();
            var result = request.ExpectedVersion.HasValue
                ? await FinanceApiClient.UpdateCompanyStatutoryProfileAsync(companyId, request)
                : await FinanceApiClient.CreateCompanyStatutoryProfileAsync(companyId, request);
            SetupStatus ??= new AccountingSetupStatusResponse { CompanyId = companyId };
            SetupStatus.StatutoryProfile = result;
            SetupStatus.MissingLegalFacts = result.MissingFacts;
            ProfileDraft = StatutoryProfileDraft.From(result.Profile);
            SuccessMessage = FinanceText["BusinessFactsSaved"];
            return true;
        }
        catch (FinanceApiException exception) { ActionError = exception.Message; return false; }
        finally { IsSaving = false; }
    }

    private async Task CreateDocumentSeriesAsync()
    {
        if (!CanManageAccounting || AccessState.CompanyId is not Guid companyId) return;
        IsSaving = true; ActionError = null; SuccessMessage = null;
        try
        {
            await FinanceApiClient.CreateStatutoryDocumentSeriesAsync(companyId, SeriesDraft.ToRequest());
            DocumentSeries = await FinanceApiClient.GetStatutoryDocumentSeriesAsync(companyId);
            SeriesDraft = DocumentSeriesDraft.CreateDefault();
            SeriesDraft.SetFiscalYear(Draft.FiscalYearStart);
            SuccessMessage = FinanceText["DocumentSeriesSaved"];
        }
        catch (FinanceApiException exception) { ActionError = exception.Message; }
        finally { IsSaving = false; }
    }

    private async Task CompleteSetupAsync()
    {
        if (!CanManageAccounting || !CanCompleteSetup || AccessState.CompanyId is not Guid companyId || SelectedPack is not { } pack) return;
        IsSaving = true; ActionError = null; SuccessMessage = null;
        try
        {
            var completion = await FinanceApiClient.CompleteAccountingSetupAsync(companyId, new CompleteAccountingSetupApiRequest
            {
                BaseCurrency = Draft.BaseCurrency, FiscalYearStart = Draft.FiscalYearStart,
                PolicyPackKey = pack.PackKey, PolicyPackVersion = pack.PackVersion,
                ChartTemplateKey = Draft.ChartTemplateKey, IdempotencyKey = Draft.IdempotencyKey
            });
            SetupStatus = completion.SetupStatus;
            IsEditingSwedishSetup = false;
            SuccessMessage = completion.WasAlreadyApplied ? FinanceText["AccountingSetupAlreadyCompleted"] : FinanceText["AccountingSetupCompleted"];
        }
        catch (FinanceApiException exception) { ActionError = exception.Message; }
        finally { IsSaving = false; }
    }

    private PreviewAccountingSetupApiRequest BuildPreviewRequest(AccountingPolicyPackOptionResponse pack) => new()
    { BaseCurrency = Draft.BaseCurrency, FiscalYearStart = Draft.FiscalYearStart, PolicyPackKey = pack.PackKey, PolicyPackVersion = pack.PackVersion, ChartTemplateKey = Draft.ChartTemplateKey };
    private string GetStepClass(int step) => step == CurrentStep ? "accounting-stepper__step active" : step < CurrentStep ? "accounting-stepper__step complete" : "accounting-stepper__step";
    private static string PackIdentity(AccountingPolicyPackOptionResponse pack) => $"{pack.PackKey}|{pack.PackVersion}";
    private static void ParsePackIdentity(string value, out string key, out string version) { var parts = value.Split('|', 2, StringSplitOptions.TrimEntries); key = parts.ElementAtOrDefault(0) ?? string.Empty; version = parts.ElementAtOrDefault(1) ?? string.Empty; }
    private static string FormatFiscalStart(AccountingConfigurationResponse configuration) => new DateOnly(2000, configuration.FiscalYearStartMonth, Math.Min(configuration.FiscalYearStartDay, DateTime.DaysInMonth(2000, configuration.FiscalYearStartMonth))).ToString("MMMM d", CultureInfo.CurrentCulture);

    private sealed record SetupStep(int Number, string ResourceKey);
    private sealed class SetupDraft
    {
        public string BaseCurrency { get; set; } = string.Empty;
        public DateOnly FiscalYearStart { get; set; }
        public string PolicyPackIdentity { get; set; } = string.Empty;
        public string ChartTemplateKey { get; set; } = string.Empty;
        public string IdempotencyKey { get; } = $"accounting-setup:{Guid.NewGuid():N}";
        public static SetupDraft CreateDefault()
        {
            var currency = "USD";
            try { currency = new RegionInfo(CultureInfo.CurrentCulture.Name).ISOCurrencySymbol; } catch (ArgumentException) { }
            return new() { BaseCurrency = currency, FiscalYearStart = new DateOnly(DateTime.Today.Year, 1, 1) };
        }
    }

    private sealed class StatutoryProfileDraft
    {
        public long? ExpectedVersion { get; set; }
        public string? LegalName { get; set; }
        public string? OrganisationNumber { get; set; }
        public string? VatRegistrationNumber { get; set; }
        public string VatRegistrationStatus { get; set; } = "not_registered";
        public StatutoryAddressResponse RegisteredAddress { get; set; } = new();
        public string CountryCode { get; set; } = "SE";
        public string AccountingCurrency { get; set; } = "SEK";
        public string FiscalYearBasis { get; set; } = "calendar_year";
        public string BookkeepingMethod { get; set; } = "accrual";
        public DateOnly? OrganisationRegistrationEffectiveFrom { get; set; }
        public DateOnly? VatRegistrationEffectiveFrom { get; set; }
        public DateOnly? VatRegistrationEffectiveTo { get; set; }
        public bool IsUserAttested { get; set; }
        public string VerificationStatus { get; set; } = "unverified";
        public string SourceKind { get; set; } = "user_entry";
        public string? SourceReference { get; set; }
        public DateTime SourceCapturedUtc { get; set; } = DateTime.UtcNow;
        public string? ExternalVerifier { get; set; }
        public DateTime? ExternallyVerifiedUtc { get; set; }

        public static StatutoryProfileDraft CreateDefault() => new() { RegisteredAddress = new() { CountryCode = "SE" }, OrganisationRegistrationEffectiveFrom = DateOnly.FromDateTime(DateTime.Today) };
        public static StatutoryProfileDraft From(CompanyStatutoryProfileResponse? profile) => profile is null ? CreateDefault() : new()
        {
            ExpectedVersion = profile.Version, LegalName = profile.LegalName, OrganisationNumber = profile.SwedishOrganisationNumber,
            VatRegistrationNumber = profile.VatRegistrationNumber, VatRegistrationStatus = profile.VatRegistrationStatus,
            RegisteredAddress = profile.RegisteredAddress, CountryCode = profile.CountryCode, AccountingCurrency = profile.AccountingCurrency,
            FiscalYearBasis = profile.FiscalYearBasis, BookkeepingMethod = profile.BookkeepingMethod,
            OrganisationRegistrationEffectiveFrom = profile.OrganisationRegistrationEffectiveFrom,
            VatRegistrationEffectiveFrom = profile.VatRegistrationEffectiveFrom, VatRegistrationEffectiveTo = profile.VatRegistrationEffectiveTo,
            IsUserAttested = profile.IsUserAttested, VerificationStatus = profile.VerificationStatus, SourceKind = profile.SourceKind,
            SourceReference = profile.SourceReference, SourceCapturedUtc = profile.SourceCapturedUtc,
            ExternalVerifier = profile.ExternalVerifier, ExternallyVerifiedUtc = profile.ExternallyVerifiedUtc
        };
        public SaveCompanyStatutoryProfileApiRequest ToRequest() => new()
        {
            ExpectedVersion = ExpectedVersion, LegalName = LegalName, SwedishOrganisationNumber = OrganisationNumber,
            VatRegistrationNumber = VatRegistrationNumber, VatRegistrationStatus = VatRegistrationStatus,
            RegisteredAddress = RegisteredAddress, CountryCode = CountryCode, AccountingCurrency = AccountingCurrency,
            FiscalYearBasis = FiscalYearBasis, BookkeepingMethod = BookkeepingMethod,
            OrganisationRegistrationEffectiveFrom = OrganisationRegistrationEffectiveFrom,
            VatRegistrationEffectiveFrom = VatRegistrationEffectiveFrom, VatRegistrationEffectiveTo = VatRegistrationEffectiveTo,
            IsUserAttested = IsUserAttested, VerificationStatus = VerificationStatus, SourceKind = SourceKind,
            SourceReference = SourceReference, SourceCapturedUtc = SourceCapturedUtc,
            ExternalVerifier = ExternalVerifier, ExternallyVerifiedUtc = ExternallyVerifiedUtc
        };
    }

    private sealed class DocumentSeriesDraft
    {
        public string Code { get; set; } = "CUSTOMER";
        public string DocumentType { get; set; } = "customer_invoice";
        public DateOnly FiscalYearStart { get; set; }
        public DateOnly FiscalYearEnd { get; set; }
        public string Prefix { get; set; } = "F";
        public int NumberWidth { get; set; } = 6;
        public long FirstNumber { get; set; } = 1;
        public static DocumentSeriesDraft CreateDefault() => new();
        public void SetFiscalYear(DateOnly start) { FiscalYearStart = start; FiscalYearEnd = start.AddYears(1).AddDays(-1); }
        public CreateStatutoryDocumentSeriesApiRequest ToRequest() => new() { Code = Code, DocumentType = DocumentType, FiscalYearStart = FiscalYearStart, FiscalYearEnd = FiscalYearEnd, Prefix = Prefix, NumberWidth = NumberWidth, FirstNumber = FirstNumber };
    }
}
