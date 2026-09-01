using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FinanceLedgerAgentReadService(
    IAccountingAdministrationService administration,
    IAccountingJournalReadService journals,
    IAccountingReportingService accountingReports,
    IFinanceReadService financeReads,
    IFinancialReportSuiteService reportSuite,
    IReportDefinitionService reportDefinitions) : IFinanceLedgerAgentReadService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Meter Meter = new("VirtualCompany.Finance.LedgerAgentReads", "1.0.0");
    private static readonly Counter<long> Requests = Meter.CreateCounter<long>("finance.ledger_agent_read.requests");
    private static readonly Counter<long> Rejections = Meter.CreateCounter<long>("finance.ledger_agent_read.rejections");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("finance.ledger_agent_read.duration", "ms");

    public async Task<InternalToolExecutionResponse> ExecuteAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var toolName = request.ToolName.Trim().ToLowerInvariant();
        Requests.Add(1, Tags(toolName, "requested"));

        try
        {
            if (!FinanceLedgerAgentReadToolIds.Contains(toolName))
            {
                return Reject(toolName, "unsupported_finance_ledger_read", "This ledger read is not supported.");
            }
            if (request.ActionKind != ToolActionType.Read)
            {
                return Reject(toolName, "finance_ledger_read_only", "Ledger and report agent tools are read-only.");
            }

            var response = toolName switch
            {
                FinanceLedgerAgentReadToolIds.LookupAccounts => await LookupAccountsAsync(request, cancellationToken),
                FinanceLedgerAgentReadToolIds.ReadFiscalPeriods => await ReadPeriodsAsync(request, cancellationToken),
                FinanceLedgerAgentReadToolIds.SearchJournals => await SearchJournalsAsync(request, cancellationToken),
                FinanceLedgerAgentReadToolIds.ReadGeneralLedger => await ReadGeneralLedgerAsync(request, cancellationToken),
                FinanceLedgerAgentReadToolIds.ReadTrialBalance => await ReadTrialBalanceAsync(request, cancellationToken),
                FinanceLedgerAgentReadToolIds.ReadStatement => await ReadStatementAsync(request, cancellationToken),
                FinanceLedgerAgentReadToolIds.ReadReportDefinitions => await ReadReportDefinitionsAsync(request, cancellationToken),
                FinanceLedgerAgentReadToolIds.ReadReportSnapshot => await ReadSnapshotAsync(request, cancellationToken),
                FinanceLedgerAgentReadToolIds.ReadSourceDrilldown => await ReadDrilldownAsync(request, cancellationToken),
                _ => throw new UnreachableException()
            };
            Requests.Add(1, Tags(toolName, response.Success ? "succeeded" : response.Status));
            return response;
        }
        catch (FinancialReportException ex)
        {
            return Reject(toolName, ex.ReasonCode, SafeMessage(ex.Message, "The requested report variant is unavailable."));
        }
        catch (ReportDefinitionException ex)
        {
            return Reject(toolName, ex.ReasonCode, SafeMessage(ex.Message, "The requested report definition is unavailable."));
        }
        catch (AccountingNotInitializedException ex)
        {
            return Reject(toolName, "accounting_not_initialized", ex.Message);
        }
        catch (KeyNotFoundException)
        {
            return Reject(toolName, "finance_source_not_found", "The requested Finance source was not found in this company. Check the account, period, journal, definition, or snapshot identifier.");
        }
        catch (ArgumentException ex)
        {
            return Reject(toolName, "finance_ledger_read_validation_failed", SafeMessage(ex.Message, "The ledger read request was not valid."));
        }
        finally
        {
            Duration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds, Tags(toolName, "completed"));
        }
    }

    private async Task<InternalToolExecutionResponse> LookupAccountsAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        if (TryGuid(request, "accountId", out var accountId))
        {
            var account = await administration.GetAccountAsync(new(request.CompanyId, accountId), ct);
            return Success("accounts", account, [Source("finance_account", account.Id)], false, ["read_general_ledger", "read_trial_balance"]);
        }

        var catalogKey = Text(request, "catalogKey", 100);
        var catalogVersion = Text(request, "catalogVersion", 100);
        if (catalogKey is not null || catalogVersion is not null)
        {
            if (catalogKey is null || catalogVersion is null)
            {
                throw new ArgumentException("catalogKey and catalogVersion must be supplied together.");
            }
            var skip = Integer(request, "skip", 0, 0, 10_000);
            var take = Integer(request, "take", 100, 1, FinanceLedgerAgentReadContract.MaximumJournalPageSize);
            var page = await administration.GetChartCatalogAsync(new(
                request.CompanyId, catalogKey, catalogVersion, Text(request, "search", 128),
                Text(request, "groupCode", 50), Boolean(request, "k2Only"), Boolean(request, "excludeExisting"), skip, take), ct);
            return Success("accounts", page, ["accounting_chart_catalog:" + page.CatalogKey + ":" + page.CatalogVersion],
                page.Skip + page.Accounts.Count < page.MatchedAccountCount, ["lookup_account"]);
        }

        await EnsureInitializedAsync(request.CompanyId, ct);
        var accounts = await administration.GetAccountsAsync(new(
            request.CompanyId, Text(request, "search", 128), Text(request, "accountClass", 50), Text(request, "status", 30)), ct);
        if (Boolean(request, "requireUnique") && accounts.Count != 1)
        {
            return Clarify("account_reference_ambiguous", accounts.Select(x => new { x.Id, x.Code, x.Name }).Take(20));
        }
        return Success("accounts", accounts, accounts.Select(x => Source("finance_account", x.Id)), false,
            ["read_general_ledger", "read_trial_balance"]);
    }

    private async Task<InternalToolExecutionResponse> ReadPeriodsAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        if (TryGuid(request, "fiscalPeriodId", out var periodId))
        {
            var period = await administration.GetPeriodAsync(new(request.CompanyId, periodId), ct);
            return Success("fiscalYears", period, [Source("fiscal_period", period.Id)], false, ["read_ledger", "read_reports"]);
        }

        var years = await administration.GetFiscalYearsAsync(new(request.CompanyId), ct);
        if (years.Count == 0)
        {
            return Reject(request.ToolName, "accounting_not_initialized", "Accounting has not been initialized. Complete accounting setup before reading periods or ledger reports.");
        }
        var reference = Text(request, "reference", 128);
        var periods = years.SelectMany(x => x.Periods)
            .Where(x => reference is null || x.Name.Contains(reference, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (reference is not null && periods.Length != 1)
        {
            return Clarify(periods.Length == 0 ? "fiscal_period_not_found" : "fiscal_period_ambiguous",
                periods.Select(x => new { x.Id, x.Name, x.StartDate, x.EndDate }).Take(20));
        }
        return Success("fiscalYears", years, periods.Select(x => Source("fiscal_period", x.Id)), false, ["read_ledger", "read_reports"]);
    }

    private async Task<InternalToolExecutionResponse> SearchJournalsAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        await EnsureInitializedAsync(request.CompanyId, ct);
        if (TryGuid(request, "ledgerEntryId", out var ledgerEntryId))
        {
            var journal = await journals.GetAsync(new(request.CompanyId, ledgerEntryId), ct);
            return Success("journals", journal, JournalSources(journal), false, ["read_source_drilldown"]);
        }

        var sourceType = Text(request, "sourceType", 80);
        var sourceId = Text(request, "sourceId", 128);
        if (sourceType is not null || sourceId is not null)
        {
            if (sourceType is null || sourceId is null)
            {
                throw new ArgumentException("sourceType and sourceId must be supplied together.");
            }
            var journal = await journals.GetBySourceAsync(new(request.CompanyId, sourceType, sourceId, Text(request, "sourceVersion", 80)), ct);
            if (journal is null)
            {
                return Reject(request.ToolName, "journal_source_not_found", "No journal was found for that source reference in this company.");
            }
            return Success("journals", journal, JournalSources(journal), false, ["read_source_drilldown"]);
        }

        var skip = Integer(request, "skip", 0, 0, 100_000);
        var take = Integer(request, "take", 100, 1, FinanceLedgerAgentReadContract.MaximumJournalPageSize);
        var result = await journals.ListAsync(new(request.CompanyId, Date(request, "from"), Date(request, "to"), skip, take,
            Text(request, "search", 128), sourceType, Text(request, "postingType", 80), Text(request, "voucherSeriesCode", 20)), ct);
        return Success("journals", result, result.Items.SelectMany(JournalSources), result.Skip + result.Items.Count < result.TotalCount,
            ["read_journal", "read_source_drilldown"]);
    }

    private async Task<InternalToolExecutionResponse> ReadGeneralLedgerAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        var periodId = RequiredGuid(request, "fiscalPeriodId");
        await EnsureInitializedAsync(request.CompanyId, ct);
        var report = await accountingReports.GetGeneralLedgerAsync(new(request.CompanyId, periodId,
            OptionalGuid(request, "accountId"), Integer(request, "page", 1, 1, 100_000),
            Integer(request, "pageSize", 200, 1, FinanceLedgerAgentReadContract.MaximumPageSize)), ct);
        var sources = report.Accounts.Select(x => Source("finance_account", x.AccountId))
            .Concat(report.Accounts.SelectMany(x => x.Lines).Select(x => Source("ledger_entry", x.LedgerEntryId)))
            .Concat(report.Accounts.SelectMany(x => x.Lines).SelectMany(x => x.Evidence).Select(x => Source("document", x.DocumentId)));
        return Success("generalLedger", report, sources, report.HasMore, ["read_journal", "read_source_drilldown"]);
    }

    private async Task<InternalToolExecutionResponse> ReadTrialBalanceAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        var periodId = RequiredGuid(request, "fiscalPeriodId");
        await EnsureInitializedAsync(request.CompanyId, ct);
        var report = await accountingReports.GetTrialBalanceAsync(new(request.CompanyId, periodId), ct);
        return Success("trialBalance", report,
            report.Accounts.Select(x => Source("finance_account", x.AccountId)).Append(Source("fiscal_period", periodId)),
            false, ["read_general_ledger", "read_statement"]);
    }

    private async Task<InternalToolExecutionResponse> ReadStatementAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        var periodId = RequiredGuid(request, "fiscalPeriodId");
        var kind = RequiredText(request, "reportKind", 80).ToLowerInvariant();
        await EnsureInitializedAsync(request.CompanyId, ct);
        if (kind == "profit_and_loss")
        {
            var report = await financeReads.GetProfitAndLossReportAsync(new(request.CompanyId, periodId), ct);
            return Success("statement", report, StatementSources(report.RevenueLines.Concat(report.ExpenseLines), periodId), false,
                ["read_source_drilldown"]);
        }
        if (kind == "balance_sheet")
        {
            var report = await financeReads.GetBalanceSheetReportAsync(new(request.CompanyId, periodId), ct);
            return Success("statement", report, StatementSources(report.AssetLines.Concat(report.LiabilityLines).Concat(report.EquityLines), periodId), false,
                ["read_source_drilldown"]);
        }
        if (!FinancialReportKinds.Supported.Contains(kind))
        {
            return Reject(request.ToolName, "unsupported_report_variant",
                "That report variant is not supported. Use profit_and_loss, balance_sheet, cash_flow, equity_changes, aged_receivables, aged_payables, journal_register, fixed_asset_register, tax_detail, currency, or dimension.");
        }
        var suiteReport = await reportSuite.GetAsync(new(request.CompanyId, periodId, kind,
            Text(request, "cashFlowMethod", 20) ?? CashFlowMethods.Indirect,
            OptionalGuid(request, "comparisonFiscalPeriodId"), Integer(request, "rollingPeriodCount", 12, 1, 60),
            Date(request, "asOfDate"), OptionalGuid(request, "dimensionTypeId"), OptionalGuid(request, "dimensionMemberId"),
            Integer(request, "page", 1, 1, 100_000), Integer(request, "pageSize", 200, 1, FinanceLedgerAgentReadContract.MaximumPageSize),
            OptionalGuid(request, "definitionVersionId"), false), ct);
        return Success("statement", suiteReport,
            suiteReport.Lines.SelectMany(x => x.Provenance.LedgerEntryIds).Select(x => Source("ledger_entry", x))
                .Concat(suiteReport.Lines.SelectMany(x => x.Provenance.DocumentIds).Select(x => Source("document", x)))
                .Concat(suiteReport.Lines.SelectMany(x => x.Provenance.SourceReferences).Select(x => "source_reference:" + x))
                .Append(Source("fiscal_period", periodId)), suiteReport.HasMore, ["read_source_drilldown"]);
    }

    private async Task<InternalToolExecutionResponse> ReadReportDefinitionsAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        await EnsureInitializedAsync(request.CompanyId, ct);
        if (TryGuid(request, "definitionVersionId", out var versionId))
        {
            var version = await reportDefinitions.GetAsync(request.CompanyId, versionId, ct);
            return Success("reportDefinitions", version,
                [Source("report_definition", version.DefinitionId), Source("report_definition_version", version.VersionId)], false,
                ["read_statement"]);
        }
        var definitions = await reportDefinitions.ListAsync(request.CompanyId, ct);
        var reference = Text(request, "reference", 128);
        var matches = definitions.Where(x => reference is null || x.Code.Contains(reference, StringComparison.OrdinalIgnoreCase) ||
            x.Name.Contains(reference, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (reference is not null && Boolean(request, "requireUnique") && matches.Length != 1)
        {
            return Clarify(matches.Length == 0 ? "report_definition_not_found" : "report_definition_ambiguous",
                matches.Select(x => new { x.Id, x.LatestVersionId, x.Code, x.Name, x.LatestVersionNumber }).Take(20));
        }
        return Success("reportDefinitions", matches,
            matches.SelectMany(x => new[] { Source("report_definition", x.Id), Source("report_definition_version", x.LatestVersionId) }),
            false, ["read_report_definition", "read_statement"]);
    }

    private async Task<InternalToolExecutionResponse> ReadSnapshotAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        var snapshotId = RequiredGuid(request, "snapshotId");
        var snapshot = await reportSuite.GetSnapshotAsync(request.CompanyId, snapshotId, ct);
        var expectedChecksum = Text(request, "expectedChecksum", 128);
        if (expectedChecksum is not null && !string.Equals(expectedChecksum, snapshot.Checksum, StringComparison.OrdinalIgnoreCase))
        {
            return Reject(request.ToolName, "stale_report_snapshot",
                "The supplied snapshot checksum is stale. Read the current snapshot identity before continuing.",
                new Dictionary<string, JsonNode?> { ["currentChecksum"] = JsonValue.Create(snapshot.Checksum), ["snapshotId"] = JsonValue.Create(snapshot.Id) });
        }
        var expectedDefinition = OptionalGuid(request, "definitionVersionId");
        if (expectedDefinition.HasValue && snapshot.Report.ReportDefinitionVersionId != expectedDefinition)
        {
            return Reject(request.ToolName, "stale_report_definition_version",
                "The snapshot was produced from a different report definition version.");
        }
        return Success("reportSnapshot", snapshot,
            [Source("financial_report_snapshot", snapshot.Id), Source("fiscal_period", snapshot.FiscalPeriodId)], false,
            ["read_source_drilldown"], "immutable_snapshot");
    }

    private async Task<InternalToolExecutionResponse> ReadDrilldownAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        var periodId = RequiredGuid(request, "fiscalPeriodId");
        var reportKind = RequiredText(request, "reportKind", 80).ToLowerInvariant();
        var lineKey = RequiredText(request, "lineKey", 128);
        if (reportKind is "profit_and_loss" or "balance_sheet")
        {
            var page = Integer(request, "page", 1, 1, 100_000);
            var pageSize = Integer(request, "pageSize", 200, 1, FinanceLedgerAgentReadContract.MaximumPageSize);
            var statementType = reportKind == "profit_and_loss"
                ? FinancialStatementType.ProfitAndLoss
                : FinancialStatementType.BalanceSheet;
            var source = await financeReads.GetFinancialStatementDrilldownAsync(new(
                request.CompanyId, periodId, statementType, lineKey, null, OptionalGuid(request, "snapshotId")), ct);
            var offset = (page - 1) * pageSize;
            var entries = source.JournalEntries.Skip(offset).Take(pageSize).ToArray();
            var bounded = source with { JournalEntries = entries };
            return Success("drilldown", bounded,
                entries.Select(x => Source("ledger_entry", x.LedgerEntryId)), offset + entries.Length < source.JournalEntries.Count,
                ["read_journal", "read_evidence"]);
        }
        if (!FinancialReportKinds.Supported.Contains(reportKind))
        {
            return Reject(request.ToolName, "unsupported_report_drilldown",
                "Source drill-down is supported for the versioned financial report suite. Use its report kind and line key.");
        }
        var drilldown = await reportSuite.GetDrilldownAsync(new(request.CompanyId, periodId, reportKind, lineKey,
            OptionalGuid(request, "snapshotId"), Integer(request, "page", 1, 1, 100_000),
            Integer(request, "pageSize", 200, 1, FinanceLedgerAgentReadContract.MaximumPageSize)), ct);
        return Success("drilldown", drilldown,
            drilldown.Items.Select(x => Source("ledger_entry", x.LedgerEntryId))
                .Concat(drilldown.Items.SelectMany(x => x.DocumentIds).Select(x => Source("document", x))), drilldown.HasMore,
            ["read_journal", "read_evidence"]);
    }

    private async Task EnsureInitializedAsync(Guid companyId, CancellationToken ct)
    {
        var years = await administration.GetFiscalYearsAsync(new(companyId), ct);
        if (years.Count == 0)
        {
            throw new AccountingNotInitializedException();
        }
    }

    private static InternalToolExecutionResponse Success<T>(string property, T value, IEnumerable<string> sourceIds,
        bool truncated, IEnumerable<string> allowedActions, string freshness = "authoritative_live")
    {
        var metadata = Metadata(sourceIds, truncated, allowedActions, freshness);
        AddNativeReportMetadata(metadata, value);
        return InternalToolExecutionResponse.Succeeded("Authoritative Finance read completed.",
            new Dictionary<string, JsonNode?> { [property] = JsonSerializer.SerializeToNode(value, JsonOptions) }, metadata);
    }

    private static void AddNativeReportMetadata<T>(Dictionary<string, JsonNode?> metadata, T value)
    {
        switch (value)
        {
            case TrialBalanceReportDto trial:
                metadata["checksum"] = JsonValue.Create(trial.Checksum);
                metadata["sourceMode"] = JsonValue.Create(trial.SourceMode);
                metadata["controlTotals"] = JsonSerializer.SerializeToNode(new
                {
                    trial.TotalOpeningDebits, trial.TotalOpeningCredits, trial.TotalDebits, trial.TotalCredits,
                    trial.TotalClosingDebits, trial.TotalClosingCredits, trial.IsBalanced
                }, JsonOptions);
                break;
            case CompleteFinancialReportDto report:
                AddCompleteReportMetadata(metadata, report);
                break;
            case FinancialReportSnapshotDto snapshot:
                AddCompleteReportMetadata(metadata, snapshot.Report);
                metadata["snapshotId"] = JsonValue.Create(snapshot.Id);
                metadata["checksum"] = JsonValue.Create(snapshot.Checksum);
                metadata["sourceGeneratedUtc"] = JsonValue.Create(snapshot.CreatedUtc);
                break;
            case FinancialReportDrilldownDto drilldown:
                metadata["checksum"] = JsonValue.Create(drilldown.ReportChecksum);
                break;
            case ProfitAndLossReportDto profitAndLoss:
                AddStatementSnapshotMetadata(metadata, profitAndLoss.FiscalPeriodId, profitAndLoss.Currency, profitAndLoss.Snapshot);
                break;
            case BalanceSheetReportDto balanceSheet:
                AddStatementSnapshotMetadata(metadata, balanceSheet.FiscalPeriodId, balanceSheet.Currency, balanceSheet.Snapshot);
                break;
            case FinancialStatementDrilldownDto statementDrilldown:
                AddStatementSnapshotMetadata(metadata, statementDrilldown.FiscalPeriodId,
                    statementDrilldown.SelectedLine.Currency, statementDrilldown.Snapshot);
                metadata["controlTotals"] = JsonSerializer.SerializeToNode(new
                {
                    statementDrilldown.OpeningBalanceAdjustment, statementDrilldown.JournalLineTotal,
                    statementDrilldown.ReconciliationTotal, statementDrilldown.ReconciliationDelta
                }, JsonOptions);
                break;
            case ReportDefinitionVersionDto definition:
                metadata["definitionVersionId"] = JsonValue.Create(definition.VersionId);
                metadata["definitionVersionNumber"] = JsonValue.Create(definition.VersionNumber);
                metadata["definitionHash"] = JsonValue.Create(definition.DefinitionHash);
                break;
        }
    }

    private static void AddCompleteReportMetadata(Dictionary<string, JsonNode?> metadata, CompleteFinancialReportDto report)
    {
        metadata["calculationVersion"] = JsonValue.Create(report.CalculationVersion);
        metadata["mappingVersion"] = JsonValue.Create(report.MappingVersion);
        metadata["parametersHash"] = JsonValue.Create(report.ParametersHash);
        metadata["checksum"] = JsonValue.Create(report.Checksum);
        metadata["snapshotId"] = report.SnapshotId.HasValue ? JsonValue.Create(report.SnapshotId.Value) : null;
        metadata["definitionVersionId"] = report.ReportDefinitionVersionId.HasValue ? JsonValue.Create(report.ReportDefinitionVersionId.Value) : null;
        metadata["definitionVersionNumber"] = report.ReportDefinitionVersionNumber.HasValue ? JsonValue.Create(report.ReportDefinitionVersionNumber.Value) : null;
        metadata["definitionHash"] = JsonValue.Create(report.ReportDefinitionHash);
        metadata["sourceGeneratedUtc"] = JsonValue.Create(report.GeneratedUtc);
        metadata["currency"] = JsonValue.Create(report.Currency);
        metadata["asOfDate"] = JsonValue.Create(report.AsOfDate);
        metadata["controlTotals"] = JsonSerializer.SerializeToNode(report.ControlTotals, JsonOptions);
    }

    private static void AddStatementSnapshotMetadata(Dictionary<string, JsonNode?> metadata, Guid fiscalPeriodId,
        string currency, FinancialStatementSnapshotMetadataDto? snapshot)
    {
        metadata["fiscalPeriodId"] = JsonValue.Create(fiscalPeriodId);
        metadata["currency"] = JsonValue.Create(currency);
        if (snapshot is null) return;
        metadata["snapshotId"] = JsonValue.Create(snapshot.SnapshotId);
        metadata["snapshotVersion"] = JsonValue.Create(snapshot.VersionNumber);
        metadata["checksum"] = JsonValue.Create(snapshot.BalancesChecksum);
        metadata["sourceGeneratedUtc"] = JsonValue.Create(snapshot.GeneratedAtUtc);
    }

    private static Dictionary<string, JsonNode?> Metadata(IEnumerable<string> sourceIds, bool truncated,
        IEnumerable<string> allowedActions, string freshness) => new(StringComparer.OrdinalIgnoreCase)
        {
            ["contractVersion"] = JsonValue.Create(FinanceLedgerAgentReadContract.Version),
            ["generatedUtc"] = JsonValue.Create(DateTime.UtcNow),
            ["freshness"] = JsonValue.Create(freshness),
            ["truncated"] = JsonValue.Create(truncated),
            ["sourceIds"] = new JsonArray(sourceIds.Distinct(StringComparer.OrdinalIgnoreCase).Take(2_000).Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
            ["allowedActions"] = new JsonArray(allowedActions.Distinct(StringComparer.OrdinalIgnoreCase).Select(x => (JsonNode?)JsonValue.Create(x)).ToArray())
        };

    private static InternalToolExecutionResponse Clarify<T>(string code, IEnumerable<T> candidates) =>
        InternalToolExecutionResponse.Failed("needs_clarification", code,
            "The reference did not resolve uniquely. Choose one of the bounded candidates.",
            new Dictionary<string, JsonNode?> { ["candidates"] = JsonSerializer.SerializeToNode(candidates, JsonOptions) },
            Metadata([], false, ["clarify_reference"], "authoritative_live"));

    private static InternalToolExecutionResponse Reject(string toolName, string code, string summary,
        Dictionary<string, JsonNode?>? data = null)
    {
        Rejections.Add(1, Tags(toolName, code));
        return InternalToolExecutionResponse.Failed("blocked", code, summary, data,
            Metadata([], false, ["correct_request", "review_accounting_setup"], "unavailable"));
    }

    private static TagList Tags(string toolName, string outcome) => new()
    {
        { "tool.name", toolName },
        { "outcome", outcome }
    };

    private static IEnumerable<string> JournalSources(AccountingJournalDto journal) =>
        new[] { Source("ledger_entry", journal.Id) }
            .Concat(journal.Evidence?.Select(x => Source("document", x.DocumentId)) ?? []);

    private static IEnumerable<string> StatementSources(IEnumerable<FinanceStatementLineDto> lines, Guid periodId) =>
        lines.Where(x => x.FinanceAccountId.HasValue).Select(x => Source("finance_account", x.FinanceAccountId!.Value))
            .Append(Source("fiscal_period", periodId));

    private static string Source(string type, Guid id) => type + ":" + id;

    private static string? Text(InternalToolExecutionRequest request, string key, int maxLength)
    {
        if (!request.Payload.TryGetValue(key, out var node) || node is null) return null;
        var value = node.GetValue<string>().Trim();
        if (value.Length == 0) return null;
        if (value.Length > maxLength) throw new ArgumentException($"{key} exceeds {maxLength} characters.");
        return value;
    }

    private static string RequiredText(InternalToolExecutionRequest request, string key, int maxLength) =>
        Text(request, key, maxLength) ?? throw new ArgumentException($"{key} is required.");

    private static Guid RequiredGuid(InternalToolExecutionRequest request, string key) =>
        OptionalGuid(request, key) ?? throw new ArgumentException($"{key} is required.");

    private static Guid? OptionalGuid(InternalToolExecutionRequest request, string key) =>
        TryGuid(request, key, out var value) ? value : null;

    private static bool TryGuid(InternalToolExecutionRequest request, string key, out Guid value)
    {
        value = Guid.Empty;
        if (!request.Payload.TryGetValue(key, out var node) || node is null) return false;
        if (!Guid.TryParse(node.GetValue<string>(), out value) || value == Guid.Empty)
            throw new ArgumentException($"{key} must be a non-empty UUID.");
        return true;
    }

    private static int Integer(InternalToolExecutionRequest request, string key, int fallback, int min, int max)
    {
        if (!request.Payload.TryGetValue(key, out var node) || node is null) return fallback;
        var value = node.GetValue<int>();
        if (value < min || value > max) throw new ArgumentOutOfRangeException(key, $"{key} must be between {min} and {max}.");
        return value;
    }

    private static bool Boolean(InternalToolExecutionRequest request, string key) =>
        request.Payload.TryGetValue(key, out var node) && node is not null && node.GetValue<bool>();

    private static DateOnly? Date(InternalToolExecutionRequest request, string key)
    {
        var value = Text(request, key, 10);
        if (value is null) return null;
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new ArgumentException($"{key} must use yyyy-MM-dd.");
        return date;
    }

    private static string SafeMessage(string message, string fallback) =>
        string.IsNullOrWhiteSpace(message) || message.Length > 500 ? fallback : message;

    private sealed class AccountingNotInitializedException : ArgumentException
    {
        public AccountingNotInitializedException() : base("Accounting has not been initialized. Complete accounting setup before reading ledger or report data.") { }
    }
}
