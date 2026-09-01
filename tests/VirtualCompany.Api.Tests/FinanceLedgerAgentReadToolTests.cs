using System.Reflection;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Shared;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceLedgerAgentReadToolTests
{
    [Fact]
    public void Manifest_registers_every_P2_tool_as_bounded_read_only_FinanceView_authority()
    {
        var registry = new StaticCompanyToolRegistry();
        var ledgerCapability = Assert.Single(FinanceAgentCoverageCatalogue.Manifests,
            x => x.Id == FinanceAgentCoverageCapabilityIds.LedgerAndFinancialReporting);

        Assert.Equal(FinanceLedgerAgentReadToolIds.All.Count, ledgerCapability.Operations.Count);
        foreach (var toolName in FinanceLedgerAgentReadToolIds.All)
        {
            var registration = Assert.Single(registry.ListTools(), x => x.ToolName == toolName);
            var definition = Assert.Single(registry.ListToolDefinitions(), x => x.ToolName == toolName);
            var operation = Assert.Single(ledgerCapability.Operations, x => x.ToolName == toolName);

            Assert.Equal(new[] { ToolActionType.Read }, registration.SupportedActions);
            Assert.Equal(ToolActionType.Read, definition.ActionType);
            Assert.False(definition.SensitiveAction);
            Assert.Equal(FinancePermissions.View, operation.RequiredPermission);
            Assert.Equal(FinanceAgentCoverageSupportStates.ImplementedRead, operation.SupportState);
            Assert.NotNull(definition.SelectionMetadata);
            Assert.Contains("finance", registration.Scopes);
        }

        var ledger = registry.ListToolDefinitions().Single(x => x.ToolName == FinanceLedgerAgentReadToolIds.ReadGeneralLedger);
        Assert.Equal(200, ledger.InputSchema["properties"]!["pageSize"]!["maximum"]!.GetValue<int>());
        Assert.Contains(FinancePlanningReferenceTypes.Account, ledger.SelectionMetadata!.TargetEntityTypes);
        Assert.Contains(FinancePlanningReferenceTypes.FiscalPeriod, ledger.SelectionMetadata.TargetEntityTypes);
        Assert.Contains(ledger.SelectionMetadata.NaturalLanguageExamples,
            example => example.Contains("6540", StringComparison.Ordinal) && example.Contains("August", StringComparison.Ordinal));
        var journals = registry.ListToolDefinitions().Single(x => x.ToolName == FinanceLedgerAgentReadToolIds.SearchJournals);
        Assert.Equal(100, journals.InputSchema["properties"]!["take"]!["maximum"]!.GetValue<int>());
        var definitions = registry.ListToolDefinitions().Single(x => x.ToolName == FinanceLedgerAgentReadToolIds.ReadReportDefinitions);
        Assert.Equal(FinanceLedgerAgentReadContract.MaximumLookupPageSize,
            definitions.InputSchema["properties"]!["take"]!["maximum"]!.GetValue<int>());
        Assert.DoesNotContain(registry.ListToolDefinitions(), x => x.ToolName.Contains("export", StringComparison.OrdinalIgnoreCase) &&
                                                                  x.ToolName.StartsWith("finance.ledger", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task General_ledger_preserves_period_account_and_pagination_and_returns_sources()
    {
        var companyId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        GetGeneralLedgerQuery? captured = null;
        var report = new GeneralLedgerReportDto(companyId, periodId, "August 2026",
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            true, true, "immutable_journal", [new GeneralLedgerAccountDto(accountId, "6540", "IT services", "expense", "SEK",
                0m, 500m, 0m, 500m, 2, [new GeneralLedgerLineDto(Guid.NewGuid(), entryId, "A-42", new DateOnly(2026, 8, 15),
                    "Hosting", 500m, 0m, 500m, "SEK", "supplier_bill", "bill-42", "3", null, [])])],
            2, 1, 2, true);
        var service = CreateService(
            administration: InitializedAdministration(companyId, periodId),
            accountingReports: Proxy<IAccountingReportingService>((method, args) =>
            {
                if (method.Name == nameof(IAccountingReportingService.GetGeneralLedgerAsync))
                {
                    captured = (GetGeneralLedgerQuery)args![0]!;
                    return Task.FromResult(report);
                }
                return Unexpected(method);
            }));

        var response = await service.ExecuteAsync(Request(FinanceLedgerAgentReadToolIds.ReadGeneralLedger, companyId,
            ("fiscalPeriodId", JsonValue.Create(periodId.ToString())), ("accountId", JsonValue.Create(accountId.ToString())),
            ("page", JsonValue.Create(2)), ("pageSize", JsonValue.Create(1))), default);

        Assert.True(response.Success);
        Assert.Equal(periodId, captured!.FiscalPeriodId);
        Assert.Equal(accountId, captured.FinanceAccountId);
        Assert.Equal(2, captured.Page);
        Assert.Equal(1, captured.PageSize);
        Assert.True(response.Metadata["truncated"]!.GetValue<bool>());
        var sources = response.Metadata["sourceIds"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray();
        Assert.Contains("finance_account:" + accountId, sources);
        Assert.Contains("ledger_entry:" + entryId, sources);
        Assert.Contains("fiscal_period:" + periodId, sources);
        Assert.NotNull(response.Metadata["controlTotals"]);
    }

    [Fact]
    public async Task Closed_snapshot_reads_are_stable_and_stale_checksum_is_rejected_without_refresh()
    {
        var companyId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var snapshotId = Guid.NewGuid();
        var definitionVersionId = Guid.NewGuid();
        var report = CompleteReport(companyId, periodId, snapshotId, definitionVersionId, "checksum-closed-v1");
        var snapshot = new FinancialReportSnapshotDto(snapshotId, companyId, periodId, FinancialReportKinds.CashFlow,
            report.CalculationVersion, report.MappingVersion, report.ParametersHash, report.Checksum, Guid.NewGuid(),
            new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc), report, false);
        var reads = 0;
        var suite = Proxy<IFinancialReportSuiteService>((method, _) =>
        {
            if (method.Name == nameof(IFinancialReportSuiteService.GetSnapshotAsync))
            {
                reads++;
                return Task.FromResult(snapshot);
            }
            return Unexpected(method);
        });
        var service = CreateService(reportSuite: suite);
        var payload = new[]
        {
            ("snapshotId", (JsonNode?)JsonValue.Create(snapshotId.ToString())),
            ("expectedChecksum", (JsonNode?)JsonValue.Create("checksum-closed-v1")),
            ("definitionVersionId", (JsonNode?)JsonValue.Create(definitionVersionId.ToString()))
        };

        var first = await service.ExecuteAsync(Request(FinanceLedgerAgentReadToolIds.ReadReportSnapshot, companyId, payload), default);
        var second = await service.ExecuteAsync(Request(FinanceLedgerAgentReadToolIds.ReadReportSnapshot, companyId, payload), default);
        var stale = await service.ExecuteAsync(Request(FinanceLedgerAgentReadToolIds.ReadReportSnapshot, companyId,
            ("snapshotId", JsonValue.Create(snapshotId.ToString())), ("expectedChecksum", JsonValue.Create("old"))), default);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(first.Data["reportSnapshot"]!.ToJsonString(), second.Data["reportSnapshot"]!.ToJsonString());
        Assert.Equal("immutable_snapshot", first.Metadata["freshness"]!.GetValue<string>());
        Assert.Equal("map-v1", first.Metadata["mappingVersion"]!.GetValue<string>());
        Assert.Equal(definitionVersionId, first.Metadata["definitionVersionId"]!.GetValue<Guid>());
        Assert.Equal("checksum-closed-v1", first.Metadata["checksum"]!.GetValue<string>());
        Assert.NotNull(first.Metadata["controlTotals"]);
        Assert.Contains("report_definition_version:" + definitionVersionId,
            first.Metadata["sourceIds"]!.AsArray().Select(node => node!.GetValue<string>()));
        Assert.False(first.Metadata["sourceIdsTruncated"]!.GetValue<bool>());
        Assert.False(stale.Success);
        Assert.Equal("stale_report_snapshot", stale.ErrorCode);
        Assert.Equal(3, reads);
    }

    [Fact]
    public async Task Ambiguous_accounts_unsupported_variants_and_cross_company_ids_are_actionable()
    {
        var companyId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var administration = Proxy<IAccountingAdministrationService>((method, _) => method.Name switch
        {
            nameof(IAccountingAdministrationService.GetFiscalYearsAsync) => Task.FromResult<IReadOnlyList<AccountingFiscalYearDto>>(
                [FiscalYear(periodId)]),
            nameof(IAccountingAdministrationService.GetAccountsAsync) => Task.FromResult<IReadOnlyList<AccountingAccountListItemDto>>(
                [Account("6540", "IT services"), Account("6540-A", "IT services auxiliary")]),
            _ => Unexpected(method)
        });
        var suite = Proxy<IFinancialReportSuiteService>((method, _) => method.Name == nameof(IFinancialReportSuiteService.GetSnapshotAsync)
            ? Task.FromException<FinancialReportSnapshotDto>(new KeyNotFoundException()) : Unexpected(method));
        var service = CreateService(administration: administration, reportSuite: suite);

        var ambiguous = await service.ExecuteAsync(Request(FinanceLedgerAgentReadToolIds.LookupAccounts, companyId,
            ("search", JsonValue.Create("6540")), ("requireUnique", JsonValue.Create(true))), default);
        var unsupported = await service.ExecuteAsync(Request(FinanceLedgerAgentReadToolIds.ReadStatement, companyId,
            ("fiscalPeriodId", JsonValue.Create(periodId.ToString())), ("reportKind", JsonValue.Create("invented_statement"))), default);
        var crossCompany = await service.ExecuteAsync(Request(FinanceLedgerAgentReadToolIds.ReadReportSnapshot, companyId,
            ("snapshotId", JsonValue.Create(Guid.NewGuid().ToString()))), default);

        Assert.Equal("needs_clarification", ambiguous.Status);
        Assert.Equal("account_reference_ambiguous", ambiguous.ErrorCode);
        Assert.Equal("unsupported_report_variant", unsupported.ErrorCode);
        Assert.Equal("finance_source_not_found", crossCompany.ErrorCode);
    }

    [Fact]
    public async Task Account_and_report_definition_searches_apply_declared_bounded_pages()
    {
        var companyId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var accounts = new[] { Account("1000", "Cash"), Account("2000", "Payables"), Account("3000", "Revenue") };
        var definitions = Enumerable.Range(1, 3).Select(index => new ReportDefinitionSummaryDto(
            Guid.NewGuid(), $"REPORT_{index}", $"Report {index}", "cash_flow", "template", index, "active",
            Guid.NewGuid(), null, null, index)).ToArray();
        var administration = Proxy<IAccountingAdministrationService>((method, _) => method.Name switch
        {
            nameof(IAccountingAdministrationService.GetFiscalYearsAsync) =>
                Task.FromResult<IReadOnlyList<AccountingFiscalYearDto>>([FiscalYear(periodId)]),
            nameof(IAccountingAdministrationService.GetAccountsAsync) =>
                Task.FromResult<IReadOnlyList<AccountingAccountListItemDto>>(accounts),
            _ => Unexpected(method)
        });
        var reportDefinitions = Proxy<IReportDefinitionService>((method, _) => method.Name == nameof(IReportDefinitionService.ListAsync)
            ? Task.FromResult<IReadOnlyList<ReportDefinitionSummaryDto>>(definitions)
            : Unexpected(method));
        var service = CreateService(administration: administration, reportDefinitions: reportDefinitions);

        var accountPage = await service.ExecuteAsync(Request(FinanceLedgerAgentReadToolIds.LookupAccounts, companyId,
            ("skip", JsonValue.Create(1)), ("take", JsonValue.Create(1))), default);
        var definitionPage = await service.ExecuteAsync(Request(FinanceLedgerAgentReadToolIds.ReadReportDefinitions, companyId,
            ("skip", JsonValue.Create(1)), ("take", JsonValue.Create(1))), default);

        Assert.True(accountPage.Success);
        Assert.Single(accountPage.Data["accounts"]!.AsArray());
        Assert.Equal("2000", accountPage.Data["accounts"]![0]!["code"]!.GetValue<string>());
        Assert.True(accountPage.Metadata["truncated"]!.GetValue<bool>());
        Assert.Equal(3L, accountPage.Metadata["totalCount"]!.GetValue<long>());
        Assert.Equal(1L, accountPage.Metadata["skip"]!.GetValue<long>());

        Assert.True(definitionPage.Success);
        Assert.Single(definitionPage.Data["reportDefinitions"]!.AsArray());
        Assert.Equal("REPORT_2", definitionPage.Data["reportDefinitions"]![0]!["code"]!.GetValue<string>());
        Assert.True(definitionPage.Metadata["truncated"]!.GetValue<bool>());
        Assert.Equal(3L, definitionPage.Metadata["totalCount"]!.GetValue<long>());
    }

    [Fact]
    public async Task Native_statement_pagination_preserves_totals_and_snapshot_freshness_and_rejects_ignored_parameters()
    {
        var companyId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var snapshot = new FinancialStatementSnapshotMetadataDto(Guid.NewGuid(), 4, "balances-v4",
            new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), "SEK");
        var report = new ProfitAndLossReportDto(companyId, periodId, "August 2026",
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), true, true, "SEK",
            [StatementLine("3000", "Revenue", 1_000m), StatementLine("3010", "Services", 500m)],
            [StatementLine("6540", "IT services", -300m)], 1_500m, 300m, 1_200m, snapshot);
        var reads = 0;
        var financeReads = Proxy<IFinanceReadService>((method, _) =>
        {
            if (method.Name == nameof(IFinanceReadService.GetProfitAndLossReportAsync))
            {
                reads++;
                return Task.FromResult(report);
            }
            return Unexpected(method);
        });
        var service = CreateService(administration: InitializedAdministration(companyId, periodId), financeReads: financeReads);

        var page = await service.ExecuteAsync(Request(FinanceLedgerAgentReadToolIds.ReadStatement, companyId,
            ("fiscalPeriodId", JsonValue.Create(periodId.ToString())), ("reportKind", JsonValue.Create("profit_and_loss")),
            ("page", JsonValue.Create(2)), ("pageSize", JsonValue.Create(1))), default);
        var unsupported = await service.ExecuteAsync(Request(FinanceLedgerAgentReadToolIds.ReadStatement, companyId,
            ("fiscalPeriodId", JsonValue.Create(periodId.ToString())), ("reportKind", JsonValue.Create("profit_and_loss")),
            ("asOfDate", JsonValue.Create("2026-08-15"))), default);

        Assert.True(page.Success);
        Assert.Equal("immutable_snapshot", page.Metadata["freshness"]!.GetValue<string>());
        Assert.Equal(3L, page.Metadata["totalCount"]!.GetValue<long>());
        Assert.True(page.Metadata["truncated"]!.GetValue<bool>());
        Assert.NotNull(page.Metadata["controlTotals"]);
        Assert.Equal(1_200m, page.Metadata["controlTotals"]!["netIncome"]!.GetValue<decimal>());
        Assert.Single(page.Data["statement"]!["revenueLines"]!.AsArray());
        Assert.Empty(page.Data["statement"]!["expenseLines"]!.AsArray());
        Assert.Equal("3010", page.Data["statement"]!["revenueLines"]![0]!["accountCode"]!.GetValue<string>());

        Assert.False(unsupported.Success);
        Assert.Equal("unsupported_native_statement_parameters", unsupported.ErrorCode);
        Assert.Contains("asOfDate", unsupported.UserSafeSummary, StringComparison.Ordinal);
        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task Suite_statement_forwards_versioned_parameters_and_bounds_provenance_metadata()
    {
        var companyId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var comparisonPeriodId = Guid.NewGuid();
        var dimensionTypeId = Guid.NewGuid();
        var dimensionMemberId = Guid.NewGuid();
        var definitionVersionId = Guid.NewGuid();
        var snapshotId = Guid.NewGuid();
        var ledgerEntryId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var asOfDate = new DateOnly(2026, 8, 20);
        var sourceReferences = Enumerable.Range(0, FinanceLedgerAgentReadContract.MaximumSourceIds + 5)
            .Select(index => $"source-{index:0000}").ToArray();
        var provenance = new FinancialReportProvenanceDto([ledgerEntryId], [], sourceReferences, [documentId], [], [], []);
        var line = new FinancialReportLineDto("dimension:consulting", "dimensions", "Consulting", 250m, 200m, 1_200m,
            "SEK", 1, provenance);
        var report = CompleteReport(companyId, periodId, snapshotId, definitionVersionId, "dimension-v1") with
        {
            ReportKind = FinancialReportKinds.Dimension,
            AsOfDate = asOfDate,
            Lines = [line],
            Page = 3,
            PageSize = 25,
            TotalLineCount = 51,
            HasMore = true
        };
        GetFinancialReportSuiteQuery? captured = null;
        var suite = Proxy<IFinancialReportSuiteService>((method, args) =>
        {
            if (method.Name == nameof(IFinancialReportSuiteService.GetAsync))
            {
                captured = (GetFinancialReportSuiteQuery)args![0]!;
                return Task.FromResult(report);
            }
            return Unexpected(method);
        });
        var service = CreateService(administration: InitializedAdministration(companyId, periodId), reportSuite: suite);

        var response = await service.ExecuteAsync(Request(FinanceLedgerAgentReadToolIds.ReadStatement, companyId,
            ("fiscalPeriodId", JsonValue.Create(periodId.ToString())), ("reportKind", JsonValue.Create("dimension")),
            ("cashFlowMethod", JsonValue.Create("direct")),
            ("comparisonFiscalPeriodId", JsonValue.Create(comparisonPeriodId.ToString())),
            ("rollingPeriodCount", JsonValue.Create(6)), ("asOfDate", JsonValue.Create("2026-08-20")),
            ("dimensionTypeId", JsonValue.Create(dimensionTypeId.ToString())),
            ("dimensionMemberId", JsonValue.Create(dimensionMemberId.ToString())),
            ("definitionVersionId", JsonValue.Create(definitionVersionId.ToString())),
            ("page", JsonValue.Create(3)), ("pageSize", JsonValue.Create(25))), default);

        Assert.True(response.Success);
        Assert.Equal(periodId, captured!.FiscalPeriodId);
        Assert.Equal(FinancialReportKinds.Dimension, captured.ReportKind);
        Assert.Equal(CashFlowMethods.Direct, captured.CashFlowMethod);
        Assert.Equal(comparisonPeriodId, captured.ComparisonFiscalPeriodId);
        Assert.Equal(6, captured.RollingPeriodCount);
        Assert.Equal(asOfDate, captured.AsOfDate);
        Assert.Equal(dimensionTypeId, captured.DimensionTypeId);
        Assert.Equal(dimensionMemberId, captured.DimensionMemberId);
        Assert.Equal(definitionVersionId, captured.DefinitionVersionId);
        Assert.Equal(3, captured.Page);
        Assert.Equal(25, captured.PageSize);
        Assert.False(captured.PreviewDefinition);
        Assert.True(response.Metadata["truncated"]!.GetValue<bool>());
        Assert.Equal(50L, response.Metadata["skip"]!.GetValue<long>());
        Assert.Equal(25, response.Metadata["take"]!.GetValue<int>());
        Assert.Equal(51L, response.Metadata["totalCount"]!.GetValue<long>());
        Assert.Equal(FinanceLedgerAgentReadContract.MaximumSourceIds,
            response.Metadata["sourceIds"]!.AsArray().Count);
        Assert.Equal(FinanceLedgerAgentReadContract.MaximumSourceIds + 10,
            response.Metadata["sourceIdCount"]!.GetValue<int>());
        Assert.True(response.Metadata["sourceIdsTruncated"]!.GetValue<bool>());
        Assert.Equal("immutable_snapshot", response.Metadata["freshness"]!.GetValue<string>());
    }

    [Fact]
    public async Task Uninitialized_accounting_and_non_read_actions_are_rejected_before_authoritative_reads()
    {
        var companyId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var administration = Proxy<IAccountingAdministrationService>((method, _) =>
            method.Name == nameof(IAccountingAdministrationService.GetFiscalYearsAsync)
                ? Task.FromResult<IReadOnlyList<AccountingFiscalYearDto>>([])
                : Unexpected(method));
        var service = CreateService(administration: administration);

        var uninitialized = await service.ExecuteAsync(Request(FinanceLedgerAgentReadToolIds.ReadTrialBalance, companyId,
            ("fiscalPeriodId", JsonValue.Create(periodId.ToString()))), default);
        var nonRead = await service.ExecuteAsync(new InternalToolExecutionRequest(
            FinanceLedgerAgentReadToolIds.ReadTrialBalance,
            new(companyId, Guid.NewGuid(), Guid.NewGuid(), ToolActionType.Execute, "finance"),
            new Dictionary<string, JsonNode?>()), default);

        Assert.False(uninitialized.Success);
        Assert.Equal("accounting_not_initialized", uninitialized.ErrorCode);
        Assert.False(nonRead.Success);
        Assert.Equal("finance_ledger_read_only", nonRead.ErrorCode);
    }

    private static FinanceLedgerAgentReadService CreateService(
        IAccountingAdministrationService? administration = null,
        IAccountingJournalReadService? journals = null,
        IAccountingReportingService? accountingReports = null,
        IFinanceReadService? financeReads = null,
        IFinancialReportSuiteService? reportSuite = null,
        IReportDefinitionService? reportDefinitions = null) => new(
            administration ?? Proxy<IAccountingAdministrationService>((m, _) => Unexpected(m)),
            journals ?? Proxy<IAccountingJournalReadService>((m, _) => Unexpected(m)),
            accountingReports ?? Proxy<IAccountingReportingService>((m, _) => Unexpected(m)),
            financeReads ?? Proxy<IFinanceReadService>((m, _) => Unexpected(m)),
            reportSuite ?? Proxy<IFinancialReportSuiteService>((m, _) => Unexpected(m)),
            reportDefinitions ?? Proxy<IReportDefinitionService>((m, _) => Unexpected(m)));

    private static IAccountingAdministrationService InitializedAdministration(Guid companyId, Guid periodId) =>
        Proxy<IAccountingAdministrationService>((method, _) => method.Name == nameof(IAccountingAdministrationService.GetFiscalYearsAsync)
            ? Task.FromResult<IReadOnlyList<AccountingFiscalYearDto>>([FiscalYear(periodId)]) : Unexpected(method));

    private static AccountingFiscalYearDto FiscalYear(Guid periodId)
    {
        var period = new AccountingPeriodDto(periodId, "August 2026", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31),
            true, true, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow);
        return new AccountingFiscalYearDto(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 0, 1, 1, [period]);
    }

    private static AccountingAccountListItemDto Account(string code, string name) => new(Guid.NewGuid(), code, name,
        "expense", "debit", "SEK", null, null, true, true, false, null, null, "profit_and_loss", DateTime.UtcNow);

    private static FinanceStatementLineDto StatementLine(string code, string name, decimal amount) =>
        new(Guid.NewGuid(), code, name, "operating", "account", amount, "SEK");

    private static CompleteFinancialReportDto CompleteReport(Guid companyId, Guid periodId, Guid snapshotId,
        Guid definitionVersionId, string checksum) => new(companyId, periodId, "August 2026", FinancialReportKinds.CashFlow,
        new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        new DateOnly(2026, 8, 31), "SEK", "calc-v1", "map-v1", "parameters-v1", checksum, true, true, true,
        snapshotId, new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc), [], new(100m, 100m, 0m, 0m, 0m, true),
        [], 1, 200, 0, false, 1_000, 10, definitionVersionId, 3, "definition-hash-v3");

    private static InternalToolExecutionRequest Request(string toolName, Guid companyId,
        params (string Key, JsonNode? Value)[] payload) => new(toolName,
        new(companyId, Guid.NewGuid(), Guid.NewGuid(), ToolActionType.Read, "finance"),
        payload.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase));

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, HandlerProxy>();
        ((HandlerProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private static object Unexpected(MethodInfo method) =>
        throw new InvalidOperationException("Unexpected call to " + method.Name);

    public class HandlerProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
    }
}
