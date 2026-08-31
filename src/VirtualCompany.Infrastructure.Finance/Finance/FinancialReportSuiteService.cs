using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Finance;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;
using VirtualCompany.Shared;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FinancialReportSuiteService : IFinancialReportSuiteService
{
    private const long SmallVolumeBudgetMs = 2_000;
    private const long MediumVolumeBudgetMs = 8_000;
    private readonly VirtualCompanyDbContext _db;
    private readonly ICompanyMembershipContextResolver _membership;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditEventWriter _auditWriter;
    private readonly FinancialReportSuiteTelemetry _telemetry;
    private readonly TimeProvider _time;

    public FinancialReportSuiteService(VirtualCompanyDbContext db,
        ICompanyMembershipContextResolver membership, ICurrentUserAccessor currentUser,
        IAuditEventWriter auditWriter, FinancialReportSuiteTelemetry telemetry, TimeProvider time)
    {
        _db = db;
        _membership = membership;
        _currentUser = currentUser;
        _auditWriter = auditWriter;
        _telemetry = telemetry;
        _time = time;
    }

    public async Task<CompleteFinancialReportDto> GetAsync(GetFinancialReportSuiteQuery query, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        await RequireViewAsync(query.CompanyId, cancellationToken);
        ValidateQuery(query);
        var period = await LoadPeriodAsync(query.CompanyId, query.FiscalPeriodId, cancellationToken);
        var definition = await ResolveDefinitionAsync(query, period, cancellationToken);
        query = await ApplyDefinitionComparisonAsync(query with { DefinitionVersionId = definition?.Id }, definition, period, cancellationToken);
        var parametersHash = ParametersHash(query, definition?.DefinitionHash);

        if (period.IsClosed)
        {
            var stored = await _db.FinancialReportSuiteSnapshots.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == query.CompanyId && x.FiscalPeriodId == period.Id &&
                    x.ReportKind == query.ReportKind && x.ParametersHash == parametersHash)
                .OrderByDescending(x => x.CreatedUtc).FirstOrDefaultAsync(cancellationToken);
            if (stored is not null)
            {
                var restored = Deserialize(stored) with { UsedSnapshot = true, SnapshotId = stored.Id,
                    ObservedDurationMilliseconds = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds };
                _telemetry.Generated(restored);
                return restored;
            }
        }

        var report = await GenerateAsync(query, period, parametersHash, definition, cancellationToken);
        report = report with { ObservedDurationMilliseconds = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds };
        _telemetry.Generated(report);
        return report;
    }

    public async Task<FinancialReportSnapshotDto> CaptureSnapshotAsync(CaptureFinancialReportSnapshotCommand command,
        CancellationToken cancellationToken)
    {
        await RequireManageAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        var key = Required(command.IdempotencyKey, nameof(command.IdempotencyKey), 200);
        var existing = await _db.FinancialReportSuiteSnapshots.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.IdempotencyKey == key, cancellationToken);
        if (existing is not null)
        {
            _telemetry.SnapshotCaptured(existing.ReportKind, true);
            return MapSnapshot(existing, true);
        }

        var query = new GetFinancialReportSuiteQuery(command.CompanyId, command.FiscalPeriodId,
            NormalizeReportKind(command.ReportKind), NormalizeMethod(command.CashFlowMethod),
            command.ComparisonFiscalPeriodId, command.RollingPeriodCount, command.AsOfDate,
            command.DimensionTypeId, command.DimensionMemberId, 1, 1_000);
        ValidateQuery(query);
        var period = await LoadPeriodAsync(command.CompanyId, command.FiscalPeriodId, cancellationToken);
        if (!period.IsClosed || !period.IsReportingLocked)
            throw new FinancialReportException("financial_report_period_not_locked",
                "An immutable report snapshot requires a closed and reporting-locked period.", true);

        var definition = await ResolveDefinitionAsync(query, period, cancellationToken);
        query = await ApplyDefinitionComparisonAsync(query with { DefinitionVersionId = definition?.Id }, definition, period, cancellationToken);
        var report = await GenerateAsync(query, period, ParametersHash(query, definition?.DefinitionHash), definition, cancellationToken);
        if (report.Blockers.Count > 0)
            throw new FinancialReportException("financial_report_blocked",
                "The report has unresolved mapping or source blockers and cannot be snapshotted.", true);
        var now = _time.GetUtcNow().UtcDateTime;
        var snapshot = new FinancialReportSuiteSnapshot(Guid.NewGuid(), command.CompanyId, command.FiscalPeriodId,
            report.ReportKind, report.CalculationVersion, report.MappingVersion, report.ParametersHash, report.Checksum,
            JsonSerializer.Serialize(report, JsonOptions), command.ActorUserId, key, now,
            report.ReportDefinitionVersionId, report.ReportDefinitionVersionNumber, report.ReportDefinitionHash);
        _db.FinancialReportSuiteSnapshots.Add(snapshot);
        await _auditWriter.WriteAsync(new AuditEventWriteRequest(
            command.CompanyId,
            AuditActorTypes.User,
            command.ActorUserId,
            AuditEventActions.FinancialReportSnapshotCaptured,
            AuditTargetTypes.FinancialReportSnapshot,
            snapshot.Id.ToString("D"),
            AuditEventOutcomes.Succeeded,
            "Captured an immutable report snapshot from a closed and reporting-locked fiscal period.",
            DataSources: ["general_ledger", "financial_statement_mappings"],
            Metadata: new Dictionary<string, string?>
            {
                ["fiscalPeriodId"] = command.FiscalPeriodId.ToString("D"),
                ["reportKind"] = report.ReportKind,
                ["calculationVersion"] = report.CalculationVersion,
                ["mappingVersion"] = report.MappingVersion,
                ["parametersHash"] = report.ParametersHash,
                ["checksum"] = report.Checksum,
                ["idempotencyKey"] = key
            },
            OccurredUtc: now), cancellationToken);
        try { await _db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            var replay = await _db.FinancialReportSuiteSnapshots.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.IdempotencyKey == key, cancellationToken);
            if (replay is not null)
            {
                _telemetry.SnapshotCaptured(replay.ReportKind, true);
                return MapSnapshot(replay, true);
            }
            throw;
        }
        _telemetry.SnapshotCaptured(snapshot.ReportKind, false);
        return MapSnapshot(snapshot, false);
    }

    public async Task<FinancialReportSnapshotDto> GetSnapshotAsync(Guid companyId, Guid snapshotId,
        CancellationToken cancellationToken)
    {
        await RequireViewAsync(companyId, cancellationToken);
        var snapshot = await _db.FinancialReportSuiteSnapshots.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == snapshotId, cancellationToken)
            ?? throw new KeyNotFoundException("Financial report snapshot was not found in the requested company.");
        return MapSnapshot(snapshot, false);
    }

    public async Task<FinancialReportExportDto> ExportSnapshotAsync(Guid companyId, Guid snapshotId, string format,
        CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(companyId, snapshotId, cancellationToken);
        var normalized = format?.Trim().ToLowerInvariant();
        byte[] content;
        string contentType;
        string extension;
        if (normalized == "json")
        {
            content = JsonSerializer.SerializeToUtf8Bytes(snapshot.Report, JsonOptions);
            contentType = "application/json";
            extension = "json";
        }
        else if (normalized == "csv")
        {
            content = FinancialReportExportFormatter.ToCsv(snapshot.Report);
            contentType = "text/csv; charset=utf-8";
            extension = "csv";
        }
        else throw new FinancialReportException("financial_report_export_format_unsupported", "Export format must be csv or json.");

        var checksum = FinancialReportSuiteCalculator.Sha256(Convert.ToBase64String(content));
        return new($"{snapshot.Report.ReportKind}-{snapshot.Report.FiscalPeriodName}-{snapshot.Id:N}.{extension}",
            contentType, content, checksum, snapshot.Report.ReportDefinitionVersionId,
            snapshot.Report.ReportDefinitionVersionNumber, snapshot.Report.ReportDefinitionHash);
    }

    public async Task<FinancialReportDrilldownDto> GetDrilldownAsync(GetFinancialReportDrilldownQuery query,
        CancellationToken cancellationToken)
    {
        await RequireViewAsync(query.CompanyId, cancellationToken);
        CompleteFinancialReportDto report;
        if (query.SnapshotId.HasValue)
        {
            var snapshot = await GetSnapshotAsync(query.CompanyId, query.SnapshotId.Value, cancellationToken);
            report = snapshot.Report;
            if (report.FiscalPeriodId != query.FiscalPeriodId || report.ReportKind != NormalizeReportKind(query.ReportKind))
                throw new FinancialReportException("financial_report_snapshot_scope_mismatch",
                    "The snapshot does not belong to the requested report and period.");
        }
        else
        {
            report = await GetAsync(new GetFinancialReportSuiteQuery(query.CompanyId, query.FiscalPeriodId,
                NormalizeReportKind(query.ReportKind), Page: 1, PageSize: 1_000), cancellationToken);
        }

        var reportLine = report.Lines.SingleOrDefault(x => x.LineKey == query.LineKey)
            ?? throw new KeyNotFoundException("The report line was not found.");
        var ids = reportLine.Provenance.LedgerEntryLineIds.Distinct().ToArray();
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 25, 1_000);
        var total = ids.LongLength;
        var selectedIds = ids.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        var raw = await _db.LedgerEntryLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && selectedIds.Contains(x.Id))
            .OrderBy(x => x.LedgerEntry.PostingDate).ThenBy(x => x.LedgerEntry.EntryNumber).ThenBy(x => x.Id)
            .Select(x => new DrillRow(x.Id, x.LedgerEntryId, x.LedgerEntry.EntryNumber,
                x.LedgerEntry.PostingDate, x.LedgerEntry.EntryUtc, x.FinanceAccount.Code, x.FinanceAccount.Name,
                x.DebitAmount, x.CreditAmount, x.Currency, x.DocumentDebitAmount, x.DocumentCreditAmount,
                x.DocumentCurrency, x.ExchangeRateIdentity, x.LedgerEntry.SourceType, x.LedgerEntry.SourceId,
                x.LedgerEntry.SourceVersion, x.LedgerEntry.OriginalLedgerEntryId))
            .ToListAsync(cancellationToken);
        var entryIds = raw.Select(x => x.EntryId).Distinct().ToArray();
        var evidenceRows = await _db.LedgerEntryEvidenceLinks.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && entryIds.Contains(x.LedgerEntryId))
            .Select(x => new { x.LedgerEntryId, x.DocumentId }).ToListAsync(cancellationToken);
        var evidence = evidenceRows.GroupBy(x => x.LedgerEntryId)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<Guid>)x.Select(y => y.DocumentId).Distinct().ToArray());
        var dimensionRows = await _db.LedgerEntryLineDimensions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && selectedIds.Contains(x.LedgerEntryLineId))
            .Select(x => new { x.LedgerEntryLineId, x.HierarchyPathSnapshot }).ToListAsync(cancellationToken);
        var dimensions = dimensionRows.GroupBy(x => x.LedgerEntryLineId)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<string>)x.Select(y => y.HierarchyPathSnapshot).Distinct().ToArray());
        var items = raw.Select(x => new FinancialReportDrilldownItemDto(x.LineId, x.EntryId, x.Voucher,
            x.PostingDate ?? DateOnly.FromDateTime(x.EntryUtc), x.AccountCode, x.AccountName,
            x.Debit, x.Credit, x.Currency, x.DocumentDebit, x.DocumentCredit, x.DocumentCurrency,
            x.ExchangeRateIdentity, x.SourceType, x.SourceId, x.SourceVersion, x.OriginalEntryId,
            evidence.GetValueOrDefault(x.EntryId, []), dimensions.GetValueOrDefault(x.LineId, []))).ToArray();
        return new FinancialReportDrilldownDto(query.CompanyId, query.FiscalPeriodId, report.ReportKind,
            query.LineKey, report.Checksum, items, page, pageSize, total, (long)page * pageSize < total);
    }

    private async Task<CompleteFinancialReportDto> GenerateAsync(GetFinancialReportSuiteQuery query, FiscalPeriod period,
        string parametersHash, ReportDefinitionVersion? definition, CancellationToken cancellationToken)
    {
        var generatedUtc = _time.GetUtcNow().UtcDateTime;
        var asOf = query.AsOfDate ?? DateOnly.FromDateTime(period.EndUtc.AddTicks(-1));
        var configuration = await _db.AccountingConfigurations.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId, cancellationToken);
        var currency = configuration?.BaseCurrency ?? "SEK";
        var mappings = await _db.FinancialStatementMappings.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.EffectiveFrom <= asOf &&
                (!x.EffectiveTo.HasValue || asOf < x.EffectiveTo.Value))
            .OrderBy(x => x.FinanceAccountId).ThenBy(x => x.StatementType).ThenBy(x => x.Id)
            .Select(x => new MappingRow(x.Id, x.FinanceAccountId, x.FinanceAccount.Code, x.FinanceAccount.Name,
                x.FinanceAccount.AccountClass, x.StatementType, x.ReportSection, x.LineClassification,
                x.VersionNumber, x.EffectiveFrom, x.EffectiveTo))
            .ToListAsync(cancellationToken);
        var mappingVersion = FinancialReportSuiteCalculator.Sha256(string.Join('\n', mappings.Select(x =>
            $"{x.Id:N}|{x.AccountId:N}|{x.StatementType.ToStorageValue()}|{x.Section.ToStorageValue()}|{x.Classification.ToStorageValue()}|{x.VersionNumber}|{x.EffectiveFrom:yyyy-MM-dd}|{x.EffectiveTo:yyyy-MM-dd}")));
        var comparison = query.ComparisonFiscalPeriodId.HasValue
            ? await LoadPeriodAsync(query.CompanyId, query.ComparisonFiscalPeriodId.Value, cancellationToken) : null;
        var rollingPeriods = await _db.FiscalPeriods.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.EndUtc <= period.EndUtc)
            .OrderByDescending(x => x.EndUtc).Take(Math.Clamp(query.RollingPeriodCount, 1, 24))
            .Select(x => x.Id).ToArrayAsync(cancellationToken);
        var scopeIds = rollingPeriods.Append(period.Id)
            .Concat(comparison is null ? [] : new[] { comparison.Id }).Distinct().ToArray();
        var rows = await _db.LedgerEntryLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && scopeIds.Contains(x.LedgerEntry.FiscalPeriodId) &&
                x.LedgerEntry.Status == LedgerEntryStatuses.Posted)
            .Select(x => new ReportRow(x.Id, x.LedgerEntryId, x.LedgerEntry.FiscalPeriodId,
                x.FinanceAccountId, x.FinanceAccount.Code, x.FinanceAccount.Name, x.FinanceAccount.AccountClass,
                x.DebitAmount, x.CreditAmount, x.Currency, x.DocumentDebitAmount, x.DocumentCreditAmount,
                x.DocumentCurrency, x.ExchangeRateIdentity, x.TaxFactsJson, x.LedgerEntry.EntryNumber,
                x.LedgerEntry.PostingDate, x.LedgerEntry.EntryUtc, x.LedgerEntry.SourceType,
                x.LedgerEntry.SourceId, x.LedgerEntry.SourceVersion, x.LedgerEntry.OriginalLedgerEntryId))
            .ToListAsync(cancellationToken);
        var currentRows = rows.Where(x => x.PeriodId == period.Id).ToArray();
        var compareRows = comparison is null ? [] : rows.Where(x => x.PeriodId == comparison.Id).ToArray();
        var rollingRows = rows.Where(x => rollingPeriods.Contains(x.PeriodId)).ToArray();
        var lineIds = rows.Select(x => x.LineId).ToArray();
        var dimensions = await _db.LedgerEntryLineDimensions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && lineIds.Contains(x.LedgerEntryLineId))
            .Select(x => new DimensionRow(x.LedgerEntryLineId, x.DimensionTypeId, x.DimensionMemberId,
                x.DimensionTypeCodeSnapshot, x.DimensionTypeNameSnapshot, x.MemberCodeSnapshot,
                x.MemberNameSnapshot, x.HierarchyPathSnapshot)).ToListAsync(cancellationToken);
        var entryIds = currentRows.Select(x => x.EntryId).Distinct().ToArray();
        var evidenceRows = await _db.LedgerEntryEvidenceLinks.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && entryIds.Contains(x.LedgerEntryId))
            .Select(x => new { x.LedgerEntryId, x.DocumentId }).ToListAsync(cancellationToken);
        var evidence = evidenceRows.GroupBy(x => x.LedgerEntryId)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<Guid>)x.Select(y => y.DocumentId).Distinct().ToArray());

        var result = definition is not null
            ? BuildCustomDefinition(definition, currentRows, compareRows, rollingRows, evidence, dimensions, currency)
            : query.ReportKind switch
        {
            FinancialReportKinds.CashFlow => BuildMappedStatement(query, mappings, currentRows, compareRows,
                rollingRows, evidence, dimensions, FinancialStatementType.CashFlow, currency),
            FinancialReportKinds.EquityChanges => BuildEquity(mappings, currentRows, compareRows, rollingRows,
                evidence, dimensions, currency),
            FinancialReportKinds.AgedReceivables => await BuildAgingAsync(query, period, asOf, true, evidence, cancellationToken),
            FinancialReportKinds.AgedPayables => await BuildAgingAsync(query, period, asOf, false, evidence, cancellationToken),
            FinancialReportKinds.JournalRegister => BuildJournalRegister(currentRows, evidence, dimensions, currency),
            FinancialReportKinds.FixedAssetRegister => await BuildFixedAssetsAsync(query.CompanyId, asOf, cancellationToken),
            FinancialReportKinds.TaxDetail => BuildTaxDetail(currentRows, evidence, dimensions, currency),
            FinancialReportKinds.Currency => BuildCurrency(currentRows, evidence, dimensions, currency),
            FinancialReportKinds.Dimension => BuildDimension(query, currentRows, dimensions, evidence, currency),
                _ => throw new FinancialReportException("financial_report_kind_unsupported", "The report kind is not supported.")
            };
        var calculated = FinancialReportSuiteCalculator.Calculate(result.Seeds);
        var provenance = result.Provenance;
        var allLines = calculated.Select(x => new FinancialReportLineDto(x.LineKey, x.Section, x.Label, x.Amount,
            comparison is null ? null : x.ComparativeAmount, x.RollingAmount, x.Currency, x.ItemCount,
            provenance.GetValueOrDefault(x.LineKey, EmptyProvenance()))).ToArray();
        var checksum = FinancialReportSuiteCalculator.Sha256(string.Join('\n', new[]
        {
            FinancialReportSuiteCalculator.CalculationVersion, mappingVersion, parametersHash,
            definition?.DefinitionHash ?? "system-layout",
            FinancialReportSuiteCalculator.Checksum(calculated)
        }));
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 25, 1_000);
        var paged = allLines.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        var sourceLineCount = currentRows.LongLength;
        return new CompleteFinancialReportDto(query.CompanyId, period.Id, period.Name, query.ReportKind,
            period.StartUtc, period.EndUtc, asOf, currency, FinancialReportSuiteCalculator.CalculationVersion,
            mappingVersion, parametersHash, checksum, period.IsClosed, period.IsReportingLocked, false, null,
            generatedUtc, result.Blockers, result.ControlTotals, paged, page, pageSize, allLines.LongLength,
            (long)page * pageSize < allLines.LongLength, sourceLineCount <= 25_000 ? SmallVolumeBudgetMs : MediumVolumeBudgetMs, 0,
            definition?.Id, definition?.VersionNumber, definition?.DefinitionHash);
    }

    private static ReportBuild BuildCustomDefinition(ReportDefinitionVersion definition,
        IReadOnlyList<ReportRow> current, IReadOnlyList<ReportRow> comparison, IReadOnlyList<ReportRow> rolling,
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> evidence, IReadOnlyList<DimensionRow> dimensions, string currency)
    {
        var lines = definition.Sections.SelectMany(x => x.Lines).ToDictionary(x => x.Code, StringComparer.Ordinal);
        var currentValues = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var comparisonValues = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var rollingValues = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var itemCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var provenance = new Dictionary<string, FinancialReportProvenanceDto>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var blockers = definition.ValidationResults.OrderByDescending(x => x.ValidatedUtc).FirstOrDefault()?.Issues
            .Select(x => new FinancialReportBlockerDto(x.Code, x.Explanation, x.LineId ?? x.AccountId)).ToList() ?? [];

        decimal Calculate(ReportDefinitionLine line, IReadOnlyList<ReportRow> population,
            IDictionary<string, decimal> values, bool captureProvenance)
        {
            if (values.TryGetValue(line.Code, out var ready)) return ready;
            if (!visiting.Add(line.Code))
            {
                blockers.Add(new("report_definition_formula_cycle", $"Formula cycle includes line {line.Code}.", line.Id));
                return 0m;
            }
            decimal value;
            if (!string.IsNullOrWhiteSpace(line.Formula))
            {
                var analysis = ReportFormulaEngine.Analyze(line.Formula);
                foreach (var reference in analysis.References)
                    if (lines.TryGetValue(reference, out var dependency)) Calculate(dependency, population, values, captureProvenance);
                try { value = ReportFormulaEngine.Evaluate(line.Formula, (IReadOnlyDictionary<string, decimal>)values); }
                catch (Exception ex)
                {
                    blockers.Add(new("report_definition_formula_invalid", $"{line.Label}: {ex.Message}", line.Id)); value = 0m;
                }
            }
            else if (line.LineType == ReportDefinitionLineTypes.Heading) value = 0m;
            else
            {
                var accountIds = line.AccountGroups.SelectMany(x => x.Members).Select(x => x.FinanceAccountId).ToHashSet();
                var selected = population.Where(x => accountIds.Contains(x.AccountId)).ToArray();
                if (line.DimensionTypeId.HasValue)
                {
                    var allowed = dimensions.Where(x => x.TypeId == line.DimensionTypeId &&
                        (!line.DimensionMemberId.HasValue || x.MemberId == line.DimensionMemberId)).Select(x => x.LineId).ToHashSet();
                    selected = selected.Where(x => allowed.Contains(x.LineId)).ToArray();
                }
                value = line.CurrencyMode == ReportDefinitionCurrencyModes.Document
                    ? selected.Sum(x => x.DocumentDebit - x.DocumentCredit)
                    : selected.Sum(x => x.Debit - x.Credit);
                if (captureProvenance)
                {
                    provenance[line.Code] = Provenance(selected, evidence, dimensions, []);
                    itemCounts[line.Code] = selected.Length;
                }
            }
            if (line.SignRule == ReportDefinitionSignRules.Invert) value = -value;
            value = decimal.Round(value / line.Scale, line.Decimals, MidpointRounding.ToEven);
            values[line.Code] = value; visiting.Remove(line.Code);
            if (captureProvenance && !provenance.ContainsKey(line.Code) && !string.IsNullOrWhiteSpace(line.Formula))
            {
                var references = ReportFormulaEngine.Analyze(line.Formula).References;
                provenance[line.Code] = MergeProvenance(references.Select(x => provenance.GetValueOrDefault(x, EmptyProvenance())));
                itemCounts[line.Code] = references.Sum(x => itemCounts.GetValueOrDefault(x));
            }
            return value;
        }

        var seeds = new List<FinancialReportAmountSeed>();
        foreach (var section in definition.Sections.OrderBy(x => x.DisplayOrder).ThenBy(x => x.Code))
        foreach (var line in section.Lines.OrderBy(x => x.DisplayOrder).ThenBy(x => x.Code))
        {
            visiting.Clear(); var amount = Calculate(line, current, currentValues, true);
            visiting.Clear(); var compare = Calculate(line, comparison, comparisonValues, false);
            visiting.Clear(); var roll = Calculate(line, rolling, rollingValues, false);
            if (!line.SuppressZero || amount != 0m || compare != 0m || roll != 0m)
                seeds.Add(new(line.Code, section.Label, line.Label, currency, amount, compare, roll, itemCounts.GetValueOrDefault(line.Code)));
        }
        var mappedAccounts = definition.AccountGroups.SelectMany(x => x.Members).Select(x => x.FinanceAccountId).ToHashSet();
        var source = current.Where(x => mappedAccounts.Contains(x.AccountId)).ToArray();
        decimal RawDetailAmount(ReportDefinitionLine line)
        {
            var accountIds = line.AccountGroups.SelectMany(x => x.Members).Select(x => x.FinanceAccountId).ToHashSet();
            var selected = current.Where(x => accountIds.Contains(x.AccountId)).ToArray();
            if (line.DimensionTypeId.HasValue)
            {
                var allowed = dimensions.Where(x => x.TypeId == line.DimensionTypeId &&
                    (!line.DimensionMemberId.HasValue || x.MemberId == line.DimensionMemberId)).Select(x => x.LineId).ToHashSet();
                selected = selected.Where(x => allowed.Contains(x.LineId)).ToArray();
            }
            return selected.Sum(x => x.Debit - x.Credit);
        }
        var detailTotal = definition.Sections.SelectMany(x => x.Lines)
            .Where(x => string.IsNullOrWhiteSpace(x.Formula) && x.LineType != ReportDefinitionLineTypes.Heading)
            .Sum(RawDetailAmount);
        var sourceTotal = source.Sum(x => x.Debit - x.Credit);
        var controlDifference = FinancialReportSuiteCalculator.Round(detailTotal - sourceTotal);
        if (controlDifference != 0m)
            blockers.Add(new("report_definition_control_imbalance",
                $"The custom layout differs from its mapped journal population by {controlDifference:N2}."));
        return new(seeds, provenance, blockers, new(source.Sum(x => x.Debit), source.Sum(x => x.Credit),
            detailTotal, sourceTotal, controlDifference, blockers.Count == 0 && controlDifference == 0m));
    }

    private static FinancialReportProvenanceDto MergeProvenance(IEnumerable<FinancialReportProvenanceDto> values)
    {
        var items = values.ToArray();
        return new(items.SelectMany(x => x.LedgerEntryIds).Distinct().Order().ToArray(),
            items.SelectMany(x => x.LedgerEntryLineIds).Distinct().Order().ToArray(),
            items.SelectMany(x => x.SourceReferences).Distinct().Order().ToArray(),
            items.SelectMany(x => x.DocumentIds).Distinct().Order().ToArray(),
            items.SelectMany(x => x.SubledgerItemIds).Distinct().Order().ToArray(),
            items.SelectMany(x => x.DimensionPaths).Distinct().Order().ToArray(),
            items.SelectMany(x => x.ExchangeRateIdentities).Distinct().Order().ToArray());
    }

    private static ReportBuild BuildMappedStatement(GetFinancialReportSuiteQuery query, IReadOnlyList<MappingRow> mappings,
        IReadOnlyList<ReportRow> current, IReadOnlyList<ReportRow> comparison, IReadOnlyList<ReportRow> rolling,
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> evidence, IReadOnlyList<DimensionRow> dimensions,
        FinancialStatementType statementType, string currency)
    {
        var selected = mappings.Where(x => x.StatementType == statementType).ToArray();
        var blockers = new List<FinancialReportBlockerDto>();
        if (selected.Length == 0)
            blockers.Add(new("financial_report_mapping_missing",
                "No governed cash-flow mappings are effective for this period."));
        var seeds = new List<FinancialReportAmountSeed>();
        var provenance = new Dictionary<string, FinancialReportProvenanceDto>();
        foreach (var mapping in selected)
        {
            var lineKey = $"account:{mapping.AccountId:N}";
            var currentRows = current.Where(x => x.AccountId == mapping.AccountId).ToArray();
            var compareRows = comparison.Where(x => x.AccountId == mapping.AccountId).ToArray();
            var rollingRows = rolling.Where(x => x.AccountId == mapping.AccountId).ToArray();
            decimal Amount(IEnumerable<ReportRow> values) => values.Sum(x => CashAmount(x, mapping.Classification, query.CashFlowMethod));
            seeds.Add(new(lineKey, mapping.Section.ToStorageValue(), $"{mapping.AccountCode} {mapping.AccountName}", currency,
                Amount(currentRows), Amount(compareRows), Amount(rollingRows), currentRows.Length));
            provenance[lineKey] = Provenance(currentRows, evidence, dimensions, []);
        }
        var mappedIds = selected.Select(x => x.AccountId).ToHashSet();
        foreach (var account in current.Where(x => x.Debit != x.Credit && !mappedIds.Contains(x.AccountId))
                     .GroupBy(x => new { x.AccountId, x.AccountCode, x.AccountName }))
            blockers.Add(new("financial_report_account_unmapped",
                $"Account {account.Key.AccountCode} {account.Key.AccountName} has period activity but no cash-flow classification.",
                account.Key.AccountId));
        var debit = current.Sum(x => x.Debit); var credit = current.Sum(x => x.Credit);
        return new(seeds, provenance, blockers, new(debit, credit, seeds.Sum(x => x.CurrentAmount),
            debit - credit, FinancialReportSuiteCalculator.Round(seeds.Sum(x => x.CurrentAmount) - (debit - credit)), debit == credit));
    }

    private static ReportBuild BuildEquity(IReadOnlyList<MappingRow> mappings, IReadOnlyList<ReportRow> current,
        IReadOnlyList<ReportRow> comparison, IReadOnlyList<ReportRow> rolling,
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> evidence, IReadOnlyList<DimensionRow> dimensions, string currency)
    {
        var equity = mappings.Where(x => x.StatementType == FinancialStatementType.BalanceSheet &&
            x.Classification == FinancialStatementLineClassification.Equity).ToArray();
        var blockers = equity.Length == 0
            ? new List<FinancialReportBlockerDto> { new("financial_report_equity_mapping_missing", "No effective equity mappings exist.") }
            : [];
        var seeds = new List<FinancialReportAmountSeed>();
        var provenance = new Dictionary<string, FinancialReportProvenanceDto>();
        foreach (var mapping in equity)
        {
            var rows = current.Where(x => x.AccountId == mapping.AccountId).ToArray();
            var compare = comparison.Where(x => x.AccountId == mapping.AccountId).ToArray();
            var roll = rolling.Where(x => x.AccountId == mapping.AccountId).ToArray();
            var key = $"equity:{mapping.AccountId:N}";
            seeds.Add(new(key, "equity_movements", $"{mapping.AccountCode} {mapping.AccountName}", currency,
                -rows.Sum(x => x.Debit - x.Credit), -compare.Sum(x => x.Debit - x.Credit),
                -roll.Sum(x => x.Debit - x.Credit), rows.Length));
            provenance[key] = Provenance(rows, evidence, dimensions, []);
        }
        var movements = seeds.Sum(x => x.CurrentAmount);
        return new(seeds, provenance, blockers, new(current.Sum(x => x.Debit), current.Sum(x => x.Credit),
            movements, movements, 0m, blockers.Count == 0));
    }

    private async Task<ReportBuild> BuildAgingAsync(GetFinancialReportSuiteQuery query, FiscalPeriod period,
        DateOnly asOf, bool receivables, IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> evidence,
        CancellationToken cancellationToken)
    {
        var seeds = new List<FinancialReportAmountSeed>();
        var provenance = new Dictionary<string, FinancialReportProvenanceDto>();
        if (receivables)
        {
            var rows = await _db.FinanceInvoices.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == query.CompanyId && x.IssuedUtc < period.EndUtc)
                .Select(x => new { x.Id, x.InvoiceNumber, Counterparty = x.Counterparty.Name, x.DueUtc,
                    x.Amount, x.PaidAmount, x.Currency, x.DocumentId }).ToListAsync(cancellationToken);
            var items = rows.Where(x => x.PaidAmount < Math.Abs(x.Amount)).Select(x => new AgingItem(x.Id,
                x.InvoiceNumber, x.Counterparty, x.DueUtc, Math.Abs(x.Amount) - x.PaidAmount, x.Currency, x.DocumentId));
            AddAging(items, seeds, provenance, asOf, "receivable");
        }
        else
        {
            var rows = await _db.FinanceBills.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == query.CompanyId && x.ReceivedUtc < period.EndUtc)
                .Select(x => new { x.Id, x.BillNumber, Counterparty = x.Counterparty.Name, x.DueUtc,
                    x.Amount, x.PaidAmount, x.Currency, x.DocumentId }).ToListAsync(cancellationToken);
            var items = rows.Where(x => x.PaidAmount < Math.Abs(x.Amount)).Select(x => new AgingItem(x.Id,
                x.BillNumber, x.Counterparty, x.DueUtc, Math.Abs(x.Amount) - x.PaidAmount, x.Currency, x.DocumentId));
            AddAging(items, seeds, provenance, asOf, "payable");
        }
        var total = seeds.Sum(x => x.CurrentAmount);
        return new(seeds, provenance, [], new(0m, 0m, total, total, 0m, true));
    }

    private static void AddAging(IEnumerable<AgingItem> items, ICollection<FinancialReportAmountSeed> seeds,
        IDictionary<string, FinancialReportProvenanceDto> provenance, DateOnly asOf, string type)
    {
        foreach (var item in items.OrderBy(x => x.DueUtc).ThenBy(x => x.Number, StringComparer.Ordinal))
        {
            var due = DateOnly.FromDateTime(item.DueUtc); var bucket = FinancialReportSuiteCalculator.AgingBucket(due, asOf);
            var key = $"{type}:{item.Id:N}";
            seeds.Add(new(key, bucket.Bucket, $"{item.Number} · {item.Counterparty}", item.Currency,
                FinancialReportSuiteCalculator.Round(item.Outstanding), 0m, item.Outstanding, 1));
            provenance[key] = new([], [], [$"{type}:{item.Id:D}"],
                item.DocumentId.HasValue ? [item.DocumentId.Value] : [], [item.Id], [], []);
        }
    }

    private static ReportBuild BuildJournalRegister(IReadOnlyList<ReportRow> rows,
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> evidence, IReadOnlyList<DimensionRow> dimensions, string currency)
    {
        var seeds = new List<FinancialReportAmountSeed>(); var provenance = new Dictionary<string, FinancialReportProvenanceDto>();
        foreach (var group in rows.GroupBy(x => new { x.EntryId, x.Voucher, x.PostingDate, x.EntryUtc, x.SourceType, x.SourceId }))
        {
            var values = group.ToArray(); var key = $"journal:{group.Key.EntryId:N}";
            seeds.Add(new(key, group.Key.SourceType ?? "manual", group.Key.Voucher, currency,
                values.Sum(x => x.Debit - x.Credit), 0m, values.Sum(x => x.Debit - x.Credit), values.Length));
            provenance[key] = Provenance(values, evidence, dimensions, []);
        }
        var debit = rows.Sum(x => x.Debit); var credit = rows.Sum(x => x.Credit);
        return new(seeds, provenance, [], new(debit, credit, debit - credit, 0m, debit - credit, debit == credit));
    }

    private async Task<ReportBuild> BuildFixedAssetsAsync(Guid companyId, DateOnly asOf, CancellationToken cancellationToken)
    {
        var assets = await _db.FixedAssetRegisterItems.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.AcquisitionDate <= asOf)
            .OrderBy(x => x.AssetNumber).ToListAsync(cancellationToken);
        var seeds = new List<FinancialReportAmountSeed>(); var provenance = new Dictionary<string, FinancialReportProvenanceDto>();
        foreach (var asset in assets)
        {
            var key = $"asset:{asset.Id:N}";
            seeds.Add(new(key, asset.Status, $"{asset.AssetNumber} {asset.Name}", asset.Currency,
                asset.NetBookValue, 0m, asset.NetBookValue, 1));
            provenance[key] = new([], [], [$"{asset.SourceType}:{asset.SourceId}:{asset.SourceVersion}"],
                asset.SourceDocumentId.HasValue ? [asset.SourceDocumentId.Value] : [], [asset.Id],
                ParseDimensionPaths(asset.DimensionSnapshotJson), []);
        }
        var total = seeds.Sum(x => x.CurrentAmount);
        return new(seeds, provenance, [], new(0m, 0m, total, total, 0m, true));
    }

    private static ReportBuild BuildTaxDetail(IReadOnlyList<ReportRow> rows,
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> evidence, IReadOnlyList<DimensionRow> dimensions, string currency)
    {
        var seeds = new List<FinancialReportAmountSeed>(); var provenance = new Dictionary<string, FinancialReportProvenanceDto>();
        foreach (var row in rows.Where(x => !string.IsNullOrWhiteSpace(x.TaxFactsJson)))
        {
            var tax = ParseTax(row.TaxFactsJson!); var key = $"tax:{row.LineId:N}";
            seeds.Add(new(key, tax.Treatment, $"{tax.Rule} · {row.AccountCode} {row.AccountName}", row.Currency,
                tax.TaxAmount, 0m, tax.TaxAmount, 1));
            provenance[key] = Provenance([row], evidence, dimensions, []);
        }
        var total = seeds.Sum(x => x.CurrentAmount);
        return new(seeds, provenance, [], new(rows.Sum(x => x.Debit), rows.Sum(x => x.Credit), total, total, 0m, true));
    }

    private static ReportBuild BuildCurrency(IReadOnlyList<ReportRow> rows,
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> evidence, IReadOnlyList<DimensionRow> dimensions, string currency)
    {
        var seeds = new List<FinancialReportAmountSeed>(); var provenance = new Dictionary<string, FinancialReportProvenanceDto>();
        foreach (var group in rows.GroupBy(x => x.DocumentCurrency))
        {
            var values = group.ToArray(); var key = $"currency:{group.Key}";
            seeds.Add(new(key, "currency_exposure", group.Key, currency, values.Sum(x => x.Debit - x.Credit),
                0m, values.Sum(x => x.Debit - x.Credit), values.Length));
            provenance[key] = Provenance(values, evidence, dimensions, []);
        }
        var debit = rows.Sum(x => x.Debit); var credit = rows.Sum(x => x.Credit);
        return new(seeds, provenance, [], new(debit, credit, debit - credit, debit - credit, 0m, debit == credit));
    }

    private static ReportBuild BuildDimension(GetFinancialReportSuiteQuery query, IReadOnlyList<ReportRow> rows,
        IReadOnlyList<DimensionRow> dimensions, IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> evidence, string currency)
    {
        var selected = dimensions.Where(x => (!query.DimensionTypeId.HasValue || x.TypeId == query.DimensionTypeId) &&
            (!query.DimensionMemberId.HasValue || x.MemberId == query.DimensionMemberId)).ToArray();
        var byLine = rows.ToDictionary(x => x.LineId);
        var seeds = new List<FinancialReportAmountSeed>(); var provenance = new Dictionary<string, FinancialReportProvenanceDto>();
        foreach (var group in selected.GroupBy(x => new { x.TypeId, x.MemberId, x.TypeCode, x.TypeName, x.MemberCode, x.MemberName, x.Path }))
        {
            var values = group.Select(x => byLine.GetValueOrDefault(x.LineId)).Where(x => x is not null).Cast<ReportRow>().ToArray();
            var key = $"dimension:{group.Key.TypeId:N}:{group.Key.MemberId:N}";
            seeds.Add(new(key, group.Key.TypeCode, $"{group.Key.MemberCode} {group.Key.MemberName}", currency,
                values.Sum(x => x.Debit - x.Credit), 0m, values.Sum(x => x.Debit - x.Credit), values.Length));
            provenance[key] = Provenance(values, evidence, dimensions, []);
        }
        var assignedIds = selected.Select(x => x.LineId).Distinct().ToHashSet();
        var source = rows.Where(x => assignedIds.Contains(x.LineId)).ToArray();
        var amount = seeds.Sum(x => x.CurrentAmount);
        return new(seeds, provenance, [], new(source.Sum(x => x.Debit), source.Sum(x => x.Credit), amount, amount, 0m, true));
    }

    private static decimal CashAmount(ReportRow row, FinancialStatementLineClassification classification, string method)
    {
        var signed = row.Debit - row.Credit;
        if (method == CashFlowMethods.Direct)
            return classification is FinancialStatementLineClassification.CashDisbursement or
                FinancialStatementLineClassification.InvestingCashOutflow or FinancialStatementLineClassification.FinancingCashOutflow
                ? -Math.Abs(signed) : Math.Abs(signed);
        return classification is FinancialStatementLineClassification.Revenue or
            FinancialStatementLineClassification.NonOperatingIncome or FinancialStatementLineClassification.FinancingCashInflow or
            FinancialStatementLineClassification.InvestingCashInflow ? -signed : signed;
    }

    private static FinancialReportProvenanceDto Provenance(IEnumerable<ReportRow> values,
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> evidence, IReadOnlyList<DimensionRow> dimensions,
        IReadOnlyList<Guid> subledgerIds)
    {
        var rows = values.ToArray(); var lineIds = rows.Select(x => x.LineId).ToHashSet();
        return new(rows.Select(x => x.EntryId).Distinct().Order().ToArray(), rows.Select(x => x.LineId).Distinct().Order().ToArray(),
            rows.Where(x => x.SourceType is not null).Select(x => $"{x.SourceType}:{x.SourceId}:{x.SourceVersion}").Distinct().Order().ToArray(),
            rows.SelectMany(x => evidence.GetValueOrDefault(x.EntryId, [])).Distinct().Order().ToArray(), subledgerIds,
            dimensions.Where(x => lineIds.Contains(x.LineId)).Select(x => x.Path).Distinct().Order().ToArray(),
            rows.Where(x => x.ExchangeRateIdentity is not null).Select(x => x.ExchangeRateIdentity!).Distinct().Order().ToArray());
    }

    private static FinancialReportProvenanceDto EmptyProvenance() => new([], [], [], [], [], [], []);
    private static IReadOnlyList<string> ParseDimensionPaths(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                ? doc.RootElement.EnumerateObject().Select(x => $"{x.Name}:{x.Value}").Order().ToArray() : [];
        }
        catch (JsonException) { return ["unreadable_dimension_snapshot"]; }
    }

    private static (string Rule, string Treatment, decimal TaxAmount) ParseTax(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
            var rule = String(root, "taxRuleKey") ?? "unclassified";
            var treatment = String(root, "taxTreatment") ?? "unclassified";
            return (rule, treatment, Decimal(root, "documentTaxAmount") ?? Decimal(root, "taxAmount") ?? 0m);
        }
        catch (JsonException) { return ("invalid", "invalid", 0m); }
    }

    private static string? String(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static decimal? Decimal(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.TryGetDecimal(out var number) ? number : null;
    private static string ParametersHash(GetFinancialReportSuiteQuery q, string? definitionHash) => FinancialReportSuiteCalculator.Sha256(
        $"{q.CompanyId:N}|{q.FiscalPeriodId:N}|{q.ReportKind}|{q.CashFlowMethod}|{q.ComparisonFiscalPeriodId:N}|{q.RollingPeriodCount}|{q.AsOfDate:yyyy-MM-dd}|{q.DimensionTypeId:N}|{q.DimensionMemberId:N}|{q.DefinitionVersionId:N}|{definitionHash}");

    private async Task<ReportDefinitionVersion?> ResolveDefinitionAsync(GetFinancialReportSuiteQuery query,
        FiscalPeriod period, CancellationToken cancellationToken)
    {
        var asOf = query.AsOfDate ?? DateOnly.FromDateTime(period.EndUtc.AddTicks(-1));
        IQueryable<ReportDefinitionVersion> versions = _db.ReportDefinitionVersions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.ReportKind == query.ReportKind)
            .Include(x => x.Sections).ThenInclude(x => x.Lines).ThenInclude(x => x.AccountGroups).ThenInclude(x => x.Members)
            .Include(x => x.AccountGroups).ThenInclude(x => x.Members)
            .Include(x => x.ValidationResults).ThenInclude(x => x.Issues)
            .Include(x => x.Comparison).Include(x => x.Approvals);
        ReportDefinitionVersion? result;
        if (query.DefinitionVersionId.HasValue)
        {
            result = await versions.SingleOrDefaultAsync(x => x.Id == query.DefinitionVersionId.Value, cancellationToken)
                ?? throw new KeyNotFoundException("The report definition version was not found in the requested company.");
            if (!query.PreviewDefinition && !result.IsEffectiveOn(asOf))
                throw new FinancialReportException("financial_report_definition_not_effective",
                    "The selected report definition is not active for this fiscal period.", true);
        }
        else
        {
            result = await versions.Where(x =>
                    (x.Status == ReportDefinitionVersionStatuses.Active || x.Status == ReportDefinitionVersionStatuses.Retired) &&
                    x.EffectiveFrom <= asOf && (!x.EffectiveTo.HasValue || asOf < x.EffectiveTo.Value))
                .OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.VersionNumber)
                .FirstOrDefaultAsync(cancellationToken);
        }
        if (result is not null && result.ReportKind != query.ReportKind)
            throw new FinancialReportException("financial_report_definition_kind_mismatch",
                "The report definition belongs to another report type.", true);
        return result;
    }

    private async Task<GetFinancialReportSuiteQuery> ApplyDefinitionComparisonAsync(GetFinancialReportSuiteQuery query,
        ReportDefinitionVersion? definition, FiscalPeriod period, CancellationToken cancellationToken)
    {
        if (definition?.Comparison is null) return query;
        var comparison = definition.Comparison;
        Guid? comparisonPeriodId = query.ComparisonFiscalPeriodId;
        if (comparison.Mode == "none") comparisonPeriodId = null;
        else if (!comparisonPeriodId.HasValue && comparison.Mode is "prior_period" or "prior_year")
        {
            var candidates = _db.FiscalPeriods.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == query.CompanyId && x.EndUtc <= period.StartUtc);
            if (comparison.Mode == "prior_year")
            {
                var target = period.EndUtc.AddYears(-1);
                comparisonPeriodId = await candidates.Where(x => x.EndUtc <= target.AddMonths(6)).OrderByDescending(x => x.EndUtc)
                    .Select(x => (Guid?)x.Id).FirstOrDefaultAsync(cancellationToken);
            }
            else comparisonPeriodId = await candidates.OrderByDescending(x => x.EndUtc)
                .Select(x => (Guid?)x.Id).FirstOrDefaultAsync(cancellationToken);
        }
        return query with { ComparisonFiscalPeriodId = comparisonPeriodId, RollingPeriodCount = comparison.PeriodCount };
    }
    private static void ValidateQuery(GetFinancialReportSuiteQuery q)
    {
        if (q.CompanyId == Guid.Empty || q.FiscalPeriodId == Guid.Empty) throw new ArgumentException("Company and fiscal period are required.");
        if (!FinancialReportKinds.Supported.Contains(q.ReportKind)) throw new FinancialReportException("financial_report_kind_unsupported", "The report kind is not supported.");
        _ = NormalizeMethod(q.CashFlowMethod);
        if (q.RollingPeriodCount is < 1 or > 24) throw new FinancialReportException("financial_report_rolling_period_invalid", "Rolling period count must be from 1 through 24.");
    }
    private static string NormalizeReportKind(string value)
    {
        var normalized = Required(value, nameof(value), 64).Replace('-', '_').ToLowerInvariant();
        return FinancialReportKinds.Supported.Contains(normalized) ? normalized : throw new FinancialReportException("financial_report_kind_unsupported", "The report kind is not supported.");
    }
    private static string NormalizeMethod(string value) => value?.Trim().ToLowerInvariant() switch
    {
        CashFlowMethods.Indirect => CashFlowMethods.Indirect,
        CashFlowMethods.Direct => CashFlowMethods.Direct,
        _ => throw new FinancialReportException("financial_report_cash_flow_method_unsupported", "Cash-flow method must be direct or indirect.")
    };
    private static string Required(string value, string name, int max) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException($"{name} is required.", name) : value.Trim().Length <= max ? value.Trim() : throw new ArgumentOutOfRangeException(name);

    private async Task<FiscalPeriod> LoadPeriodAsync(Guid companyId, Guid periodId, CancellationToken cancellationToken) =>
        await _db.FiscalPeriods.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == periodId, cancellationToken)
        ?? throw new KeyNotFoundException("Fiscal period was not found in the requested company.");
    private async Task RequireViewAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var membership = await _membership.ResolveAsync(companyId, cancellationToken) ?? throw new UnauthorizedAccessException();
        if (!FinanceAccess.CanViewAccounting(membership.MembershipRole.ToStorageValue())) throw new UnauthorizedAccessException();
    }
    private async Task RequireManageAsync(Guid companyId, Guid actorUserId, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId != actorUserId) throw new UnauthorizedAccessException();
        var membership = await _membership.ResolveAsync(companyId, cancellationToken) ?? throw new UnauthorizedAccessException();
        if (!FinanceAccess.CanManageAccounting(membership.MembershipRole.ToStorageValue())) throw new UnauthorizedAccessException();
    }
    private FinancialReportSnapshotDto MapSnapshot(FinancialReportSuiteSnapshot x, bool replay) => new(x.Id, x.CompanyId,
        x.FiscalPeriodId, x.ReportKind, x.CalculationVersion, x.MappingVersion, x.ParametersHash, x.Checksum,
        x.CreatedByUserId, x.CreatedUtc, Deserialize(x) with { UsedSnapshot = true, SnapshotId = x.Id }, replay);
    private static CompleteFinancialReportDto Deserialize(FinancialReportSuiteSnapshot snapshot) =>
        JsonSerializer.Deserialize<CompleteFinancialReportDto>(snapshot.ReportJson, JsonOptions)
        ?? throw new InvalidOperationException("Stored financial report snapshot content is invalid.");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record MappingRow(Guid Id, Guid AccountId, string AccountCode, string AccountName, string? AccountClass,
        FinancialStatementType StatementType, FinancialStatementReportSection Section,
        FinancialStatementLineClassification Classification, long VersionNumber,
        DateOnly EffectiveFrom, DateOnly? EffectiveTo);
    private sealed record ReportRow(Guid LineId, Guid EntryId, Guid PeriodId, Guid AccountId, string AccountCode,
        string AccountName, string? AccountClass, decimal Debit, decimal Credit, string Currency,
        decimal DocumentDebit, decimal DocumentCredit, string DocumentCurrency, string? ExchangeRateIdentity,
        string? TaxFactsJson, string Voucher, DateOnly? PostingDate, DateTime EntryUtc, string? SourceType,
        string? SourceId, string? SourceVersion, Guid? OriginalEntryId);
    private sealed record DimensionRow(Guid LineId, Guid TypeId, Guid MemberId, string TypeCode, string TypeName,
        string MemberCode, string MemberName, string Path);
    private sealed record DrillRow(Guid LineId, Guid EntryId, string Voucher, DateOnly? PostingDate, DateTime EntryUtc,
        string AccountCode, string AccountName, decimal Debit, decimal Credit, string Currency,
        decimal DocumentDebit, decimal DocumentCredit, string DocumentCurrency, string? ExchangeRateIdentity,
        string? SourceType, string? SourceId, string? SourceVersion, Guid? OriginalEntryId);
    private sealed record AgingItem(Guid Id, string Number, string Counterparty, DateTime DueUtc, decimal Outstanding,
        string Currency, Guid? DocumentId);
    private sealed record ReportBuild(IReadOnlyList<FinancialReportAmountSeed> Seeds,
        IReadOnlyDictionary<string, FinancialReportProvenanceDto> Provenance,
        IReadOnlyList<FinancialReportBlockerDto> Blockers, FinancialReportControlTotalsDto ControlTotals);
}

public static class FinancialReportExportFormatter
{
    public static byte[] ToCsv(CompleteFinancialReportDto report)
    {
        var output = new StringBuilder();
        output.AppendLine($"# report_checksum,{Csv(report.Checksum)}");
        output.AppendLine($"# definition_version_id,{Csv(report.ReportDefinitionVersionId?.ToString("D") ?? string.Empty)}");
        output.AppendLine($"# definition_version_number,{Csv(report.ReportDefinitionVersionNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty)}");
        output.AppendLine($"# definition_hash,{Csv(report.ReportDefinitionHash ?? string.Empty)}");
        output.AppendLine("section,line_key,label,amount,comparative_amount,rolling_amount,currency,item_count");
        foreach (var line in report.Lines)
            output.AppendLine(string.Join(',', Csv(line.Section), Csv(line.LineKey), Csv(line.Label),
                line.Amount.ToString(CultureInfo.InvariantCulture),
                line.ComparativeAmount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                line.RollingAmount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                Csv(line.Currency), line.ItemCount.ToString(CultureInfo.InvariantCulture)));
        return new UTF8Encoding(true).GetBytes(output.ToString());
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
