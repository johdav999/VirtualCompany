using Microsoft.AspNetCore.Components;
using VirtualCompany.Shared;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class AuditPackagesPage : FinancePageBase
{
    [Inject] private FinanceApiClient FinanceApi { get; set; } = default!;
    private AuditPackageWorkspaceResponse? Workspace;
    private AuditPackageResponse? Selected;
    private List<AccountingPeriodResponse> ClosedPeriods = [];
    private Guid SelectedPeriodId;
    private Guid? LoadedCompany;
    private string? WorkspaceMessage;
    private bool HasError;
    private bool IsWorking;
    private Guid? CurrentUserId => CurrentUserContext?.User.Id;
    private bool CanManage => FinanceAccess.CanManageAccounting(AccessState.MembershipRole);
    private bool CanApprove => FinanceAccess.CanApproveInvoices(AccessState.MembershipRole);
    private AuditPackageResponse? CurrentPackage => Workspace?.Packages.FirstOrDefault(x => x.FiscalPeriodId == SelectedPeriodId && x.Status is not "cancelled" and not "expired");
    private int ArtifactCount => Selected?.Artifacts.Count ?? 0;
    private int IncludedCount => Selected?.Artifacts.Count(x => x.Status == "included") ?? 0;
    private int MissingCount => Selected?.Artifacts.Count(x => x.Status == "missing") ?? 0;
    private int InaccessibleCount => Selected?.Artifacts.Count(x => x.Status == "inaccessible") ?? 0;
    private int CorruptCount => Selected?.Artifacts.Count(x => x.Status == "corrupt") ?? 0;
    private int EvidencePercentage => ArtifactCount == 0 ? 0 : (int)Math.Round(100m * IncludedCount / ArtifactCount);
    private bool HasBlockingEvidence => Selected?.Artifacts.Any(x => x.IsRequired && x.Status != "included") == true;
    private string ReadinessLabel => Selected is null ? "Not requested" : Selected.IsFinal ? "Final" : Label(Selected.Status);
    private string ReadinessClass => Selected?.IsFinal == true ? "success" : Selected?.Status == "incomplete" ? "danger" : "warning";
    private string RequiredEvidenceSummary => Selected is null ? "Choose a closed period" : $"{Selected.Artifacts.Count(x => x.IsRequired && x.Status == "included")} of {Selected.Artifacts.Count(x => x.IsRequired)} required";
    private string RetentionLabel => Selected is null ? "—" : Selected.RetainUntilUtc > DateTime.UtcNow ? "Active" : "Expired";
    private string RetentionDetail => Selected is null ? "Seven-year default" : $"Until {Selected.RetainUntilUtc.ToLocalTime():d}";
    private string LastVerificationLabel => Selected?.Verifications.FirstOrDefault()?.IsValid == true ? "Verified" : Selected?.Verifications.Count > 0 ? "Failed" : "Not run";
    private string LastVerificationDetail => Selected?.Verifications.FirstOrDefault() is { } result ? result.VerifiedUtc.ToLocalTime().ToString("g") : "Independent hash check pending";
    private string ActionHeading => HasBlockingEvidence ? "This package is incomplete and cannot be labeled final." : CurrentPackage is null ? "Request a frozen evidence snapshot." : Selected?.Status == "pending_approval" ? "Independent approval is required before assembly." : "Background assembly is bounded and retry-safe.";
    private string ActionExplanation => HasBlockingEvidence ? "Resolve missing, inaccessible, or corrupt required evidence, then request a new scope version." : "Repeated requests for the same scope and snapshot return one logical package with the same manifest checksum.";
    private string InsightHeading => Selected?.IsFinal == true ? "Package is ready for external review" : Selected?.Status == "incomplete" ? "Evidence action is required" : "Generation state is controlled";
    private string InsightText => Selected?.IsFinal == true ? "The manifest and every included item have SHA-256 evidence. Run verification again after restore." : Selected?.Status == "incomplete" ? $"{MissingCount + InaccessibleCount + CorruptCount} evidence item(s) block a final label." : "The package remains non-final until approval, assembly, and evidence checks complete.";

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();
        if (!AccessState.IsAllowed || AccessState.CompanyId is not Guid company || company == LoadedCompany) return;
        LoadedCompany = company;
        await LoadPeriodsAsync(company);
        await LoadAsync(company);
    }

    private async Task LoadPeriodsAsync(Guid company)
    {
        var years = await FinanceApi.GetAccountingFiscalYearsAsync(company);
        ClosedPeriods = years.SelectMany(x => x.Periods).Where(x => x.IsClosed).OrderByDescending(x => x.StartDate).ToList();
        if (SelectedPeriodId == Guid.Empty) SelectedPeriodId = ClosedPeriods.FirstOrDefault()?.Id ?? Guid.Empty;
    }

    private async Task LoadAsync(Guid company)
    {
        try
        {
            HasError = false;
            Workspace = await FinanceApi.GetAuditPackagesAsync(company, SelectedPeriodId == Guid.Empty ? null : SelectedPeriodId);
            Selected = Workspace?.Packages.FirstOrDefault(x => x.Id == Selected?.Id) ?? Workspace?.Packages.FirstOrDefault();
        }
        catch (FinanceApiException ex) { HasError = true; WorkspaceMessage = ex.Message; }
    }

    private Task PeriodChangedAsync() => AccessState.CompanyId is Guid company ? LoadAsync(company) : Task.CompletedTask;
    private void Select(AuditPackageResponse package) => Selected = package;
    private Task RefreshAsync() => Work(async company => await LoadAsync(company), reload: false);
    private Task RequestAsync() => Work(async company =>
    {
        Selected = await FinanceApi.RequestAuditPackageAsync(company, SelectedPeriodId, $"audit-package-ui-{SelectedPeriodId:N}-{Guid.NewGuid():N}");
        WorkspaceMessage = "Package request created. A separate authorized reviewer must approve the frozen scope before assembly.";
    });
    private Task ApproveAsync(AuditPackageResponse package) => Work(async company =>
    {
        Selected = await FinanceApi.ApproveAuditPackageAsync(company, package.Id, package.Version, "Approved for bounded evidence assembly.");
        WorkspaceMessage = "Package approved and queued for bounded background assembly.";
    });
    private Task CancelAsync(AuditPackageResponse package) => Work(async company =>
    {
        Selected = await FinanceApi.CancelAuditPackageAsync(company, package.Id, package.Version);
        WorkspaceMessage = "Cancellation was recorded before finalization.";
    });
    private Task VerifyAsync(AuditPackageResponse package) => Work(async company =>
    {
        var result = await FinanceApi.VerifyAuditPackageAsync(company, package.Id);
        WorkspaceMessage = result.SafeSummary;
    });
    private Task DownloadAsync(AuditPackageResponse package) => Work(async company =>
    {
        var authorization = await FinanceApi.AuthorizeAuditPackageDownloadAsync(company, package.Id);
        Navigation.NavigateTo(authorization.DownloadPath, forceLoad: true);
        WorkspaceMessage = $"One-time download authorization issued until {authorization.ExpiresUtc.ToLocalTime():t}.";
    }, reload: false);

    private async Task Work(Func<Guid, Task> operation, bool reload = true)
    {
        if (AccessState.CompanyId is not Guid company) return;
        IsWorking = true; HasError = false; WorkspaceMessage = null;
        try { await operation(company); if (reload) await LoadAsync(company); }
        catch (FinanceApiException ex) { HasError = true; WorkspaceMessage = ex.Message; }
        finally { IsWorking = false; }
    }

    private static string Label(string value) => string.Join(' ', value.Split('_', StringSplitOptions.RemoveEmptyEntries)).Replace("vat", "VAT", StringComparison.OrdinalIgnoreCase);
    private static string StatusClass(string value) => value switch { "final" or "included" => "green", "incomplete" or "corrupt" or "failed" => "red", "missing" or "pending_approval" or "retry_scheduled" => "amber", "inaccessible" => "red", "queued" or "generating" => "blue", _ => "neutral" };
    private static string ShortHash(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value.Length > 20 ? value[..20] + "…" : value;
}
