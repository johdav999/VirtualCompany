using System.Globalization;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Web.Components.Finance;
using VirtualCompany.Web.Localization.Formatting;

namespace VirtualCompany.Web.Tests;

public sealed class SwedishAccountingWorkspaceComponentTests
{
    [Fact]
    public void Readiness_keeps_format_attestation_and_independent_review_distinct()
    {
        using var context = CreateContext();
        var cut = context.RenderComponent<StatutoryReadinessSummary>(parameters => parameters
            .Add(x => x.Status, new CompanyStatutoryProfileStatusResponse
            {
                IsFormatComplete = true,
                IsUserAttested = true,
                IsExternallyVerified = false,
                MissingFacts = []
            })
            .Add(x => x.PolicyPackValidationState, "review_pending")
            .Add(x => x.CanEdit, true));

        Assert.Contains("Format complete", cut.Markup);
        Assert.Contains("Confirmed by your team", cut.Markup);
        Assert.Contains("Independent review", cut.Markup);
        Assert.Contains("pending", cut.FindAll("article")[2].ClassList);
        Assert.DoesNotContain("government verified", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Vat_return_actions_follow_server_allowed_actions_and_role()
    {
        using var context = CreateContext();
        var vatReturn = Return("needs_review", ["request_approval"]);

        var manager = RenderVat(context, vatReturn, canManage: true);
        Assert.Single(manager.FindAll("button"), button => button.TextContent.Contains("Request approval", StringComparison.Ordinal));
        Assert.DoesNotContain("Finalize VAT return", manager.Markup);

        manager.Dispose();
        var viewer = RenderVat(context, vatReturn, canManage: false);
        Assert.DoesNotContain("Request approval", viewer.Markup);
        Assert.Contains("VAT box results", viewer.Markup);
        Assert.Contains("accounting administrator", viewer.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Stale_blocked_and_empty_states_are_explicit_and_recoverable()
    {
        using var context = CreateContext();
        var stale = Return("calculated", ["recalculate"]);
        stale.IsStale = true;
        stale.Issues = [new() { IsBlocking = true, Explanation = "One posted VAT source changed." }];
        var cut = RenderVat(context, stale, canManage: true);

        Assert.Contains("out of date", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("One posted VAT source changed", cut.Markup);
        Assert.Contains("Recalculate VAT return", cut.Markup);

        cut.Dispose();
        var empty = RenderVat(context, null, canManage: true);
        Assert.Contains("No VAT return for this period", empty.Markup);
        Assert.Contains("Calculate VAT return", empty.Markup);
    }

    [Fact]
    public void Sources_corrections_and_exports_are_keyboard_operable_views()
    {
        using var context = CreateContext();
        var vatReturn = Return("locked", ["create_correction", "download_package"]);
        vatReturn.CanDownloadPackage = true;
        vatReturn.Contributions = [new() { LedgerEntryId = Guid.NewGuid(), VoucherNumber = "A-42", PostingDate = new(2026, 6, 30), BoxCode = "10", ExactAmount = 250m, Currency = "SEK" }];
        var cut = RenderVat(context, vatReturn, canManage: true,
            exports: [new() { Id = Guid.NewGuid(), ExportType = AccountingExportApiValues.Sie4B, Status = "failed", AttemptCount = 3, RequestedUtc = DateTime.UtcNow, ExpiresUtc = DateTime.UtcNow.AddDays(30) }]);

        cut.FindAll("button").Single(x => x.TextContent.Contains("Sources & reconciliation", StringComparison.Ordinal)).Click();
        Assert.Contains("A-42", cut.Markup);
        Assert.Contains("Open source", cut.Markup);

        cut.FindAll("button").Single(x => x.TextContent.Contains("Corrections", StringComparison.Ordinal)).Click();
        Assert.NotNull(cut.Find("#vat-correction-reason"));

        cut.FindAll("button").Single(x => x.TextContent.Contains("Statutory exports", StringComparison.Ordinal)).Click();
        Assert.Contains("SIE 4B export", cut.Markup);
        Assert.Contains("Try again", cut.Markup);
    }

    [Fact]
    public void Swedish_resources_render_complete_vat_navigation()
    {
        using var context = CreateContext();
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("sv-SE");
            var cut = RenderVat(context, Return("calculated", []), canManage: false);
            Assert.Contains("Momsdeklaration", cut.Markup);
            Assert.Contains("Källor och avstämning", cut.Markup);
            Assert.Contains("Lagstadgade exporter", cut.Markup);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    private static IRenderedComponent<VatReturnWorkspace> RenderVat(TestContext context, VatReturnResponse? vatReturn,
        bool canManage, IReadOnlyList<AccountingExportJobResponse>? exports = null) =>
        context.RenderComponent<VatReturnWorkspace>(parameters => parameters
            .Add(x => x.CompanyId, Guid.NewGuid())
            .Add(x => x.CanManage, canManage)
            .Add(x => x.FilingPeriods, [new() { Id = Guid.NewGuid(), PeriodCode = "2026-Q2", StartDate = new(2026, 4, 1), EndDate = new(2026, 6, 30), Currency = "SEK" }])
            .Add(x => x.SelectedFilingPeriodId, Guid.NewGuid())
            .Add(x => x.CurrentReturn, vatReturn)
            .Add(x => x.RelatedReturns, Array.Empty<VatReturnResponse>())
            .Add(x => x.ControlAccounts, new ControlAccountReconciliationResponse { IsReconciled = true })
            .Add(x => x.Exports, exports ?? [])
            .Add(x => x.PackageDownloadUrl, "/vat-package")
            .Add(x => x.JournalHref, id => $"/journals?entryId={id:D}")
            .Add(x => x.ExportDownloadUrl, id => $"/exports/{id:D}"));

    private static VatReturnResponse Return(string status, IReadOnlyList<string> actions) => new()
    {
        Id = Guid.NewGuid(), FilingPeriodId = Guid.NewGuid(), PeriodCode = "2026-Q2",
        StartDate = new(2026, 4, 1), EndDate = new(2026, 6, 30), Currency = "SEK",
        Status = status, SettlementExact = 1250m, SettlementFilingAmount = 1250,
        IncludedSourceCount = 1, AllowedActions = [.. actions],
        Boxes = [new() { BoxCode = "10", FactType = "output VAT", ExactAmount = 1250m, FilingAmount = 1250, SourceCount = 1 }]
    };

    private static TestContext CreateContext()
    {
        var context = new TestContext();
        context.Services.AddLocalization();
        var presentationContext = new CompanyPresentationContext();
        presentationContext.SetFormattingCulture("en-US");
        context.Services.AddSingleton<ICompanyPresentationContext>(presentationContext);
        context.Services.AddSingleton<ILocalDateTimeFormatter, LocalDateTimeFormatter>();
        context.Services.AddSingleton<INumberFormatter, NumberFormatter>();
        context.Services.AddSingleton<IMoneyFormatter, MoneyFormatter>();
        return context;
    }
}
