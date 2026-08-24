using System.Globalization;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;
using VirtualCompany.Shared;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingReportingService : IAccountingReportingService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyMembershipContextResolver _membershipResolver;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAccountingPolicyPackResolver _packResolver;
    private readonly IAuditEventWriter _auditWriter;
    private readonly TimeProvider _timeProvider;
    private readonly AccountingOperationsTelemetry? _telemetry;

    public AccountingReportingService(
        VirtualCompanyDbContext dbContext,
        ICompanyMembershipContextResolver membershipResolver,
        ICurrentUserAccessor currentUser,
        IAccountingPolicyPackResolver packResolver,
        IAuditEventWriter auditWriter,
        TimeProvider timeProvider,
        AccountingOperationsTelemetry? telemetry = null)
    {
        _dbContext = dbContext;
        _membershipResolver = membershipResolver;
        _currentUser = currentUser;
        _packResolver = packResolver;
        _auditWriter = auditWriter;
        _timeProvider = timeProvider;
        _telemetry = telemetry;
    }

    public async Task<GeneralLedgerReportDto> GetGeneralLedgerAsync(GetGeneralLedgerQuery query, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var outcome = "succeeded";
        try
        {
            await RequireViewAsync(query.CompanyId, cancellationToken);
            var period = await LoadPeriodAsync(query.CompanyId, query.FiscalPeriodId, cancellationToken);
            var page = Math.Max(1, query.Page);
            var pageSize = Math.Clamp(query.PageSize, 25, 1_000);
            var accountSeeds = await _dbContext.FinanceAccounts.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == query.CompanyId && (!query.FinanceAccountId.HasValue || x.Id == query.FinanceAccountId.Value))
                .OrderBy(x => x.Code)
                .Select(x => new AccountSeed(x.Id, x.Code, x.Name, x.AccountClass, x.Currency, x.OpeningBalance))
                .ToListAsync(cancellationToken);

            var priorMovements = await _dbContext.LedgerEntryLines.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == query.CompanyId && x.LedgerEntry.Status == LedgerEntryStatuses.Posted &&
                    x.LedgerEntry.FiscalPeriod.StartUtc < period.StartUtc &&
                    (!query.FinanceAccountId.HasValue || x.FinanceAccountId == query.FinanceAccountId.Value))
                .GroupBy(x => x.FinanceAccountId)
                .Select(x => new AccountMovement(x.Key, x.Sum(y => y.DebitAmount), x.Sum(y => y.CreditAmount), x.Count()))
                .ToListAsync(cancellationToken);
            var periodQuery = _dbContext.LedgerEntryLines.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == query.CompanyId && x.LedgerEntry.Status == LedgerEntryStatuses.Posted &&
                    x.LedgerEntry.FiscalPeriodId == period.Id &&
                    (!query.FinanceAccountId.HasValue || x.FinanceAccountId == query.FinanceAccountId.Value));
            var periodMovements = await periodQuery.GroupBy(x => x.FinanceAccountId)
                .Select(x => new AccountMovement(x.Key, x.Sum(y => y.DebitAmount), x.Sum(y => y.CreditAmount), x.Count()))
                .ToListAsync(cancellationToken);
            var totalLineCount = periodMovements.Sum(x => (long)x.Count);

            var skip = (page - 1) * pageSize;
            var ordered = query.FinanceAccountId.HasValue
                ? periodQuery.OrderBy(x => x.LedgerEntry.PostingDate)
                    .ThenBy(x => x.LedgerEntry.EntryNumber).ThenBy(x => x.Id)
                : periodQuery.OrderBy(x => x.FinanceAccount.Code)
                    .ThenBy(x => x.LedgerEntry.PostingDate)
                    .ThenBy(x => x.LedgerEntry.EntryNumber).ThenBy(x => x.Id);
            var priorPageMovements = skip == 0
                ? new Dictionary<Guid, decimal>()
                : (await ordered.Take(skip)
                    .GroupBy(x => x.FinanceAccountId)
                    .Select(x => new { AccountId = x.Key, Movement = x.Sum(y => y.DebitAmount - y.CreditAmount) })
                    .ToListAsync(cancellationToken))
                    .ToDictionary(x => x.AccountId, x => x.Movement);
            var rawRows = await ordered.Skip(skip).Take(pageSize)
                .Select(x => new RawLedgerRow(x.Id, x.LedgerEntryId, x.LedgerEntry.FiscalPeriodId,
                    x.FinanceAccountId, x.FinanceAccount.Code, x.FinanceAccount.Name,
                    x.FinanceAccount.AccountClass, x.FinanceAccount.Currency, x.FinanceAccount.OpeningBalance,
                    x.LedgerEntry.EntryNumber, x.LedgerEntry.PostingDate, x.LedgerEntry.EntryUtc,
                    x.Description ?? x.LedgerEntry.Description, x.DebitAmount, x.CreditAmount, x.Currency,
                    x.LedgerEntry.SourceType, x.LedgerEntry.SourceId, x.LedgerEntry.SourceVersion,
                    x.LedgerEntry.OriginalLedgerEntryId))
                .ToListAsync(cancellationToken);
            var rows = rawRows.Select(ToLedgerRow).ToList();

            var evidence = await LoadEvidenceAsync(query.CompanyId,
                rows.Select(x => x.EntryId).Distinct().ToArray(), cancellationToken);
            var priorLookup = priorMovements.ToDictionary(x => x.AccountId);
            var periodLookup = periodMovements.ToDictionary(x => x.AccountId);
            var accounts = accountSeeds.Select(account =>
            {
                var prior = priorLookup.GetValueOrDefault(account.Id);
                var movement = periodLookup.GetValueOrDefault(account.Id);
                var opening = account.OpeningBalance + (prior?.Debit ?? 0m) - (prior?.Credit ?? 0m);
                var running = opening + priorPageMovements.GetValueOrDefault(account.Id);
                var lines = rows.Where(x => x.AccountId == account.Id).Select(row =>
                {
                    running += row.Debit - row.Credit;
                    return new GeneralLedgerLineDto(row.LineId, row.EntryId, row.VoucherNumber, row.PostingDate,
                        row.Description, row.Debit, row.Credit, running, row.LineCurrency, row.SourceType,
                        row.SourceId, row.SourceVersion, row.OriginalEntryId,
                        evidence.GetValueOrDefault(row.EntryId, []));
                }).ToArray();
                var debit = movement?.Debit ?? 0m;
                var credit = movement?.Credit ?? 0m;
                return new GeneralLedgerAccountDto(account.Id, account.Code, account.Name,
                    account.AccountClass ?? "unclassified", account.Currency, opening, debit, credit,
                    opening + debit - credit, movement?.Count ?? 0, lines);
            }).ToArray();

            return new GeneralLedgerReportDto(query.CompanyId, period.Id, period.Name, period.StartUtc, period.EndUtc,
                period.IsClosed, period.IsReportingLocked, period.IsClosed ? "stored-period-evidence" : "posted-journals",
                accounts, page, pageSize, totalLineCount,
                query.FinanceAccountId.HasValue && (long)page * pageSize < totalLineCount);
        }
        catch
        {
            outcome = "failed";
            throw;
        }
        finally
        {
            _telemetry?.OperationCompleted(query.CompanyId, "general_ledger_page",
                Stopwatch.GetElapsedTime(started), 1_200, outcome);
        }
    }

    public async Task<TrialBalanceReportDto> GetTrialBalanceAsync(GetTrialBalanceQuery query, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var outcome = "completed";
        try
        {
            var ledger = await GetGeneralLedgerAsync(new GetGeneralLedgerQuery(query.CompanyId, query.FiscalPeriodId), cancellationToken);
            var accounts = ledger.Accounts.Select(x => new TrialBalanceAccountDto(x.AccountId, x.AccountCode, x.AccountName,
                x.AccountClass, x.Currency, x.OpeningBalance, x.Debit, x.Credit, x.ClosingBalance, x.TotalLineCount)).ToArray();
            var checksum = Checksum(accounts.OrderBy(x => x.AccountCode).Select(x =>
                $"{x.AccountId:N}|{Amount(x.OpeningBalance)}|{Amount(x.Debit)}|{Amount(x.Credit)}|{Amount(x.ClosingBalance)}|{x.Currency}"));
            var openingDebits = accounts.Where(x => x.OpeningBalance >= 0).Sum(x => x.OpeningBalance);
            var openingCredits = accounts.Where(x => x.OpeningBalance < 0).Sum(x => -x.OpeningBalance);
            var closingDebits = accounts.Where(x => x.ClosingBalance >= 0).Sum(x => x.ClosingBalance);
            var closingCredits = accounts.Where(x => x.ClosingBalance < 0).Sum(x => -x.ClosingBalance);
            var debits = accounts.Sum(x => x.Debit);
            var credits = accounts.Sum(x => x.Credit);
            return new TrialBalanceReportDto(ledger.CompanyId, ledger.FiscalPeriodId, ledger.FiscalPeriodName,
                ledger.PeriodStartUtc, ledger.PeriodEndUtc, ledger.IsClosed, ledger.IsReportingLocked, ledger.SourceMode,
                checksum, openingDebits, openingCredits, debits, credits, closingDebits, closingCredits,
                decimal.Round(debits - credits, 2) == 0m && decimal.Round(closingDebits - closingCredits, 2) == 0m, accounts);
        }
        catch
        {
            outcome = "failed";
            throw;
        }
        finally
        {
            _telemetry?.OperationCompleted(query.CompanyId, "trial_balance",
                Stopwatch.GetElapsedTime(started), 1_500, outcome);
        }
    }

    public async Task<AccountingTaxSummaryDto> GetTaxSummaryAsync(GetAccountingTaxSummaryQuery query, CancellationToken cancellationToken)
    {
        await RequireViewAsync(query.CompanyId, cancellationToken);
        var period = await LoadPeriodAsync(query.CompanyId, query.FiscalPeriodId, cancellationToken);
        var rows = await _dbContext.LedgerEntryLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.LedgerEntry.FiscalPeriodId == period.Id &&
                        x.LedgerEntry.Status == LedgerEntryStatuses.Posted && x.TaxFactsJson != null)
            .Select(x => new TaxFactRow(x.LedgerEntryId, x.LedgerEntry.PolicyPackKey, x.LedgerEntry.PolicyPackVersion,
                x.Currency, x.DebitAmount, x.CreditAmount, x.TaxFactsJson!))
            .ToListAsync(cancellationToken);

        var parsed = rows.Select(ParseTaxFact).Where(x => x is not null).Cast<ParsedTaxFact>().ToArray();
        var lines = parsed.GroupBy(x => new { x.PolicyPackKey, x.PolicyPackVersion, x.TaxRuleKey, x.TaxTreatment, x.Currency })
            .OrderBy(x => x.Key.PolicyPackKey).ThenBy(x => x.Key.PolicyPackVersion).ThenBy(x => x.Key.TaxRuleKey)
            .Select(x => new AccountingTaxSummaryLineDto(x.Key.PolicyPackKey, x.Key.PolicyPackVersion,
                x.Key.TaxRuleKey, x.Key.TaxTreatment, x.Sum(y => y.TaxableAmount), x.Sum(y => y.TaxAmount),
                x.Key.Currency, x.Count(), x.Select(y => y.EntryId).Distinct().Order().ToArray())).ToArray();
        var checksum = Checksum(lines.Select(x =>
            $"{x.PolicyPackKey}|{x.PolicyPackVersion}|{x.TaxRuleKey}|{x.TaxTreatment}|{Amount(x.TaxableAmount)}|{Amount(x.TaxAmount)}|{x.Currency}"));

        var configuration = await _dbContext.AccountingConfigurations.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId, cancellationToken);
        var currentPack = configuration is null ? null : _packResolver.TryResolve(configuration.PolicyPackKey, configuration.PolicyPackVersion, out var pack) ? pack : null;
        var countryNeutral = currentPack?.Definition.IsCountryNeutral ?? true;
        var statutory = currentPack?.Definition.IsStatutoryComplianceValidated == true &&
                        currentPack.Definition.SupportedCapabilities.Contains(AccountingPolicyCapabilityKeys.CountrySpecificTax, StringComparer.OrdinalIgnoreCase);
        var review = await _dbContext.AccountingTaxReviews.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId && x.FiscalPeriodId == period.Id, cancellationToken);
        var reviewed = review is not null && string.Equals(review.Checksum, checksum, StringComparison.OrdinalIgnoreCase);
        return new AccountingTaxSummaryDto(query.CompanyId, period.Id, period.Name, countryNeutral, statutory,
            statutory ? "Validated tax summary" : "Country-neutral bookkeeping tax summary",
            statutory ? currentPack!.Definition.ComplianceNotice : "Bookkeeping information only — not a statutory return.",
            checksum, reviewed, reviewed ? review!.ReviewedByUserId : null, reviewed ? review!.ReviewedUtc : null, lines);
    }

    public async Task<AccountingTaxSummaryDto> ReviewTaxSummaryAsync(ReviewAccountingTaxSummaryCommand command, CancellationToken cancellationToken)
    {
        await RequireManageAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        var summary = await GetTaxSummaryAsync(new GetAccountingTaxSummaryQuery(command.CompanyId, command.FiscalPeriodId), cancellationToken);
        var review = await _dbContext.AccountingTaxReviews.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.FiscalPeriodId == command.FiscalPeriodId, cancellationToken);
        if (review is not null && string.Equals(review.Checksum, summary.Checksum, StringComparison.OrdinalIgnoreCase))
        {
            return summary with { IsReviewed = true, ReviewedByUserId = review.ReviewedByUserId, ReviewedUtc = review.ReviewedUtc };
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var json = JsonSerializer.Serialize(summary with { IsReviewed = true, ReviewedByUserId = command.ActorUserId, ReviewedUtc = now });
        if (review is null)
        {
            _dbContext.AccountingTaxReviews.Add(new AccountingTaxReview(Guid.NewGuid(), command.CompanyId,
                command.FiscalPeriodId, json, summary.Checksum, command.ActorUserId, now));
        }
        else
        {
            review.Replace(json, summary.Checksum, command.ActorUserId, now);
        }

        await _auditWriter.WriteAsync(new AuditEventWriteRequest(
            command.CompanyId,
            AuditActorTypes.User,
            command.ActorUserId,
            AuditEventActions.AccountingTaxSummaryReviewed,
            AuditTargetTypes.FiscalPeriod,
            command.FiscalPeriodId.ToString("D"),
            AuditEventOutcomes.Succeeded,
            "Reviewed the period tax summary against its current posted-journal checksum.",
            Metadata: new Dictionary<string, string?>
            {
                ["checksum"] = summary.Checksum,
                ["isCountryNeutral"] = summary.IsCountryNeutral ? "true" : "false",
                ["isStatutoryComplianceValidated"] = summary.IsStatutoryComplianceValidated ? "true" : "false"
            },
            OccurredUtc: now), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return summary with { IsReviewed = true, ReviewedByUserId = command.ActorUserId, ReviewedUtc = now };
    }

    public async Task<ControlAccountReconciliationDto> GetControlAccountReconciliationAsync(GetControlAccountReconciliationQuery query, CancellationToken cancellationToken)
    {
        await RequireViewAsync(query.CompanyId, cancellationToken);
        var period = await LoadPeriodAsync(query.CompanyId, query.FiscalPeriodId, cancellationToken);
        var roles = await _dbContext.AccountingConfigurationAccountRoles.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && (x.RoleKey == AccountingAccountRoleKeys.AccountsReceivable ||
                x.RoleKey == AccountingAccountRoleKeys.AccountsPayable || x.RoleKey == AccountingAccountRoleKeys.Bank))
            .Select(x => new { x.RoleKey, x.FinanceAccountId, x.FinanceAccount.Code, x.FinanceAccount.Name, x.FinanceAccount.Currency, x.FinanceAccount.OpeningBalance })
            .ToListAsync(cancellationToken);
        var accountIds = roles.Select(x => x.FinanceAccountId).ToArray();
        var postings = await _dbContext.LedgerEntryLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && accountIds.Contains(x.FinanceAccountId) &&
                        x.LedgerEntry.Status == LedgerEntryStatuses.Posted &&
                        x.LedgerEntry.FiscalPeriod.StartUtc < period.EndUtc)
            .Select(x => new { x.FinanceAccountId, x.LedgerEntryId, x.LedgerEntry.SourceType, Signed = x.DebitAmount - x.CreditAmount })
            .ToListAsync(cancellationToken);
        var result = roles.Select(role =>
        {
            var accountRows = postings.Where(x => x.FinanceAccountId == role.FinanceAccountId).ToArray();
            var acceptedSources = role.RoleKey switch
            {
                AccountingAccountRoleKeys.AccountsReceivable => new[] { "customer_invoice", "customer_credit_note", "payment", "payment_allocation", "payment_settlement" },
                AccountingAccountRoleKeys.AccountsPayable => new[] { "supplier_bill", "supplier_credit_note", "payment", "payment_allocation", "payment_settlement" },
                _ => new[] { "bank_transaction", "payment", "payment_allocation", "payment_settlement", "cash_settlement" }
            };
            var ledger = role.OpeningBalance + accountRows.Sum(x => x.Signed);
            var source = role.OpeningBalance + accountRows.Where(x => x.SourceType is not null && acceptedSources.Contains(x.SourceType, StringComparer.OrdinalIgnoreCase)).Sum(x => x.Signed);
            var difference = decimal.Round(ledger - source, 2);
            return new ControlAccountReconciliationLineDto(role.RoleKey, role.FinanceAccountId, role.Code, role.Name,
                role.Currency, ledger, source, difference, difference == 0m,
                accountRows.Where(x => x.SourceType is null || !acceptedSources.Contains(x.SourceType, StringComparer.OrdinalIgnoreCase)).Select(x => x.LedgerEntryId).Distinct().ToArray());
        }).ToArray();
        return new ControlAccountReconciliationDto(query.CompanyId, period.Id, result.All(x => x.IsReconciled), result);
    }

    public async Task<IReadOnlyList<AccountingPeriodHistoryDto>> GetPeriodHistoryAsync(Guid companyId, Guid fiscalPeriodId, CancellationToken cancellationToken)
    {
        await RequireViewAsync(companyId, cancellationToken);
        return await _dbContext.AccountingPeriodHistory.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.FiscalPeriodId == fiscalPeriodId)
            .OrderByDescending(x => x.OccurredUtc)
            .Select(x => new AccountingPeriodHistoryDto(x.Id, x.Action, x.ActorUserId, x.Reason, x.SnapshotChecksum, x.OccurredUtc))
            .Take(200)
            .ToListAsync(cancellationToken);
    }

    public async Task<AccountingExportJobDto> RequestExportAsync(RequestAccountingExportCommand command, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var outcome = "completed";
        try
        {
            await RequireManageAsync(command.CompanyId, command.ActorUserId, cancellationToken);
            _ = await LoadPeriodAsync(command.CompanyId, command.FiscalPeriodId, cancellationToken);
            var key = Required(command.IdempotencyKey, nameof(command.IdempotencyKey), 200);
            var existing = await _dbContext.AccountingExportJobs.IgnoreQueryFilters()
                .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.IdempotencyKey == key, cancellationToken);
            if (existing is not null)
            {
                outcome = "replayed";
                return MapExport(existing);
            }
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var job = new AccountingExportJob(Guid.NewGuid(), command.CompanyId, command.FiscalPeriodId,
                command.ActorUserId, key, now, now.AddDays(30));
            _dbContext.AccountingExportJobs.Add(job);

            await _auditWriter.WriteAsync(new AuditEventWriteRequest(
                command.CompanyId,
                AuditActorTypes.User,
                command.ActorUserId,
                AuditEventActions.AccountingExportRequested,
                AuditTargetTypes.AccountingExport,
                job.Id.ToString("D"),
                AuditEventOutcomes.Succeeded,
                "Requested a durable country-neutral accounting export.",
                Metadata: new Dictionary<string, string?>
                {
                    ["fiscalPeriodId"] = command.FiscalPeriodId.ToString("D"),
                    ["retentionExpiresUtc"] = job.ExpiresUtc.ToString("O")
                },
                OccurredUtc: now), cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return MapExport(job);
        }
        catch
        {
            outcome = "failed";
            throw;
        }
        finally
        {
            _telemetry?.OperationCompleted(command.CompanyId, "export_request",
                Stopwatch.GetElapsedTime(started), 500, outcome);
        }
    }

    public async Task<IReadOnlyList<AccountingExportJobDto>> ListExportsAsync(ListAccountingExportsQuery query, CancellationToken cancellationToken)
    {
        await RequireViewAsync(query.CompanyId, cancellationToken);
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var jobs = await _dbContext.AccountingExportJobs.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && (!query.FiscalPeriodId.HasValue || x.FiscalPeriodId == query.FiscalPeriodId))
            .OrderByDescending(x => x.RequestedUtc).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return jobs.Select(MapExport).ToArray();
    }

    public async Task<AccountingExportDownloadDto> DownloadExportAsync(GetAccountingExportQuery query, CancellationToken cancellationToken)
    {
        await RequireViewAsync(query.CompanyId, cancellationToken);
        var job = await _dbContext.AccountingExportJobs.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId && x.Id == query.ExportId, cancellationToken)
            ?? throw new KeyNotFoundException("Accounting export was not found in the requested company.");
        if (job.Status != AccountingExportStatuses.Completed || job.Content is null || job.ExpiresUtc <= _timeProvider.GetUtcNow().UtcDateTime)
            throw new InvalidOperationException("The accounting export is not available for download.");
        return new AccountingExportDownloadDto(job.FileName!, job.MediaType!, job.Content, job.Checksum!);
    }

    public async Task<int> RunDueExportsAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var jobs = await _dbContext.AccountingExportJobs.IgnoreQueryFilters()
            .Where(x => x.Status == AccountingExportStatuses.Queued && (!x.NextAttemptUtc.HasValue || x.NextAttemptUtc <= now))
            .OrderBy(x => x.RequestedUtc).Take(5).ToListAsync(cancellationToken);
        foreach (var job in jobs)
        {
            var started = Stopwatch.GetTimestamp();
            var outcome = "completed";
            job.Start(now);
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                outcome = "claim_conflict";
                _dbContext.Entry(job).State = EntityState.Detached;
                _telemetry?.OperationCompleted(job.CompanyId, "export_claim",
                    Stopwatch.GetElapsedTime(started), 500, outcome);
                continue;
            }
            try
            {
                var content = await BuildExportAsync(job.CompanyId, job.FiscalPeriodId, job.RequestedUtc, cancellationToken);
                var checksum = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
                job.Complete(content, checksum, $"accounting-export-{job.FiscalPeriodId:N}.json", _timeProvider.GetUtcNow().UtcDateTime);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                outcome = job.AttemptCount < 3 ? "retry_scheduled" : "failed";
                var failedUtc = _timeProvider.GetUtcNow().UtcDateTime;
                if (job.AttemptCount < 3) job.Retry("export_generation_failed", "The export could not be generated. It will be retried.", failedUtc.AddMinutes(job.AttemptCount), failedUtc);
                else job.Fail("export_generation_failed", "The export could not be generated after three attempts.", failedUtc);
            }
            await _dbContext.SaveChangesAsync(cancellationToken);
            _telemetry?.OperationCompleted(job.CompanyId, "export_completion",
                _timeProvider.GetUtcNow().UtcDateTime - job.RequestedUtc,
                TimeSpan.FromMinutes(5).TotalMilliseconds, outcome);
        }
        return jobs.Count;
    }

    private async Task<byte[]> BuildExportAsync(Guid companyId, Guid fiscalPeriodId, DateTime generatedAtUtc, CancellationToken cancellationToken)
    {
        var company = await _dbContext.Companies.AsNoTracking().SingleAsync(x => x.Id == companyId, cancellationToken);
        var configuration = await _dbContext.AccountingConfigurations.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.CompanyId == companyId, cancellationToken);
        var selections = await _dbContext.AccountingPolicyPackSelections.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId).OrderBy(x => x.EffectiveFrom).ToListAsync(cancellationToken);
        var periods = await _dbContext.FiscalPeriods.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId).OrderBy(x => x.StartUtc).ToListAsync(cancellationToken);
        var accounts = await _dbContext.FinanceAccounts.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId).OrderBy(x => x.Code).ToListAsync(cancellationToken);
        var entries = await _dbContext.LedgerEntries.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.FiscalPeriodId == fiscalPeriodId && x.Status == LedgerEntryStatuses.Posted).OrderBy(x => x.EntryUtc).ThenBy(x => x.EntryNumber).ToListAsync(cancellationToken);
        var entryIds = entries.Select(x => x.Id).ToArray();
        var lines = await _dbContext.LedgerEntryLines.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && entryIds.Contains(x.LedgerEntryId)).OrderBy(x => x.LedgerEntryId).ThenBy(x => x.Id).ToListAsync(cancellationToken);
        var parties = await _dbContext.FinanceCounterparties.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId).OrderBy(x => x.Name).ToListAsync(cancellationToken);
        var attachments = await _dbContext.LedgerEntryEvidenceLinks.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && entryIds.Contains(x.LedgerEntryId)).OrderBy(x => x.LedgerEntryId).ThenBy(x => x.DocumentId).ToListAsync(cancellationToken);
        var export = new
        {
            schema = "virtual-company.country-neutral-accounting-export",
            version = "1.0",
            generatedAtUtc,
            company = new { company.Id, company.Name },
            configuration = new { configuration.BaseCurrency, configuration.Authority, configuration.PolicyPackKey, configuration.PolicyPackVersion, configuration.PolicyPackEffectiveFrom, configuration.RoundingPrecision, configuration.RoundingMode },
            policyPackHistory = selections.Select(x => new { x.PackKey, x.PackVersion, x.DefinitionHash, x.EffectiveFrom, x.EffectiveTo }),
            accounts = accounts.Select(x => new { x.Id, x.Code, x.Name, x.AccountClass, x.NormalBalance, x.Currency, x.ControlAccountRole, x.EffectiveFrom, x.EffectiveTo }),
            periods = periods.Select(x => new { x.Id, x.Name, x.StartUtc, x.EndUtc, x.IsClosed, x.IsReportingLocked }),
            vouchers = entries.Select(x => new { x.Id, x.EntryNumber, x.DocumentDate, x.PostingDate, x.BaseCurrency, x.PostingType, x.SourceType, x.SourceId, x.SourceVersion, x.PolicyPackKey, x.PolicyPackVersion, x.OriginalLedgerEntryId, x.CorrectionReason }),
            lines = lines.Select(x => new { x.Id, x.LedgerEntryId, x.FinanceAccountId, x.DebitAmount, x.CreditAmount, x.Currency, x.Description, taxFacts = ParseJson(x.TaxFactsJson), dimensionFacts = ParseJson(x.DimensionFactsJson) }),
            parties = parties.Select(x => new { x.Id, x.Name, x.CounterpartyType, x.TaxId }),
            attachmentManifest = attachments.Select(x => new { x.LedgerEntryId, x.DocumentId, x.Title, x.ContentHash })
        };
        return JsonSerializer.SerializeToUtf8Bytes(export, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    private static LedgerRow ToLedgerRow(RawLedgerRow row) =>
        new(row.LineId, row.EntryId, row.FiscalPeriodId, row.AccountId, row.AccountCode, row.AccountName,
            row.AccountClass, row.Currency, row.AccountOpeningBalance, row.VoucherNumber,
            row.PostingDate ?? DateOnly.FromDateTime(row.EntryUtc), row.EntryUtc, row.Description,
            row.Debit, row.Credit, row.LineCurrency, row.SourceType, row.SourceId, row.SourceVersion,
            row.OriginalEntryId);

    private async Task<Dictionary<Guid, IReadOnlyList<AccountingEvidenceReferenceDto>>> LoadEvidenceAsync(Guid companyId, Guid[] entryIds, CancellationToken cancellationToken) =>
        (await _dbContext.LedgerEntryEvidenceLinks.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && entryIds.Contains(x.LedgerEntryId))
            .Select(x => new { x.LedgerEntryId, Item = new AccountingEvidenceReferenceDto(x.DocumentId, x.Title, x.ContentHash) })
            .ToListAsync(cancellationToken)).GroupBy(x => x.LedgerEntryId).ToDictionary(x => x.Key, x => (IReadOnlyList<AccountingEvidenceReferenceDto>)x.Select(y => y.Item).ToArray());

    private async Task<FiscalPeriod> LoadPeriodAsync(Guid companyId, Guid periodId, CancellationToken cancellationToken) =>
        await _dbContext.FiscalPeriods.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == periodId, cancellationToken)
        ?? throw new KeyNotFoundException("Fiscal period was not found in the requested company.");

    private async Task RequireViewAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var membership = await _membershipResolver.ResolveAsync(companyId, cancellationToken) ?? throw new UnauthorizedAccessException();
        if (!FinanceAccess.CanViewAccounting(membership.MembershipRole.ToStorageValue())) throw new UnauthorizedAccessException();
    }

    private async Task RequireManageAsync(Guid companyId, Guid actorUserId, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId != actorUserId) throw new UnauthorizedAccessException();
        var membership = await _membershipResolver.ResolveAsync(companyId, cancellationToken) ?? throw new UnauthorizedAccessException();
        if (!FinanceAccess.CanManageAccounting(membership.MembershipRole.ToStorageValue())) throw new UnauthorizedAccessException();
    }

    private static ParsedTaxFact? ParseTaxFact(TaxFactRow row)
    {
        try
        {
            using var document = JsonDocument.Parse(row.Json);
            var root = document.RootElement;
            var rule = Text(root, "taxRuleKey") ?? "unclassified";
            var treatment = Text(root, "taxTreatment") ?? "unclassified";
            var taxable = Number(root, "taxableAmount") ?? Number(root, "netAmount") ?? Math.Abs(row.Debit - row.Credit);
            var tax = Number(root, "documentTaxAmount") ?? Number(root, "taxAmount") ?? 0m;
            return new ParsedTaxFact(row.EntryId, row.PolicyPackKey ?? "unknown", row.PolicyPackVersion ?? "unknown", rule, treatment, taxable, tax, row.Currency);
        }
        catch (JsonException) { return null; }
    }

    private static string? Text(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static decimal? Number(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)) return number;
        return value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out number) ? number : null;
    }
    private static JsonElement? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<JsonElement>(json); } catch (JsonException) { return null; }
    }
    private static string Checksum(IEnumerable<string> lines) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines)))).ToLowerInvariant();
    private static string Amount(decimal value) => value.ToString("0.00####", CultureInfo.InvariantCulture);
    private static string Required(string value, string name, int maxLength) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value.Trim().Length <= maxLength ? value.Trim() : throw new ArgumentOutOfRangeException(name);
    private AccountingExportJobDto MapExport(AccountingExportJob x) => new(x.Id, x.CompanyId, x.FiscalPeriodId, x.Status, x.AttemptCount, x.RequestedUtc, x.StartedUtc, x.CompletedUtc, x.ExpiresUtc, x.Checksum, x.FileName, x.MediaType, x.ContentLength, x.FailureCode, x.FailureSummary, x.Status == AccountingExportStatuses.Completed && x.ExpiresUtc > _timeProvider.GetUtcNow().UtcDateTime);

    private sealed record RawLedgerRow(Guid LineId, Guid EntryId, Guid FiscalPeriodId, Guid AccountId, string AccountCode, string AccountName, string? AccountClass, string Currency, decimal AccountOpeningBalance, string VoucherNumber, DateOnly? PostingDate, DateTime EntryUtc, string? Description, decimal Debit, decimal Credit, string LineCurrency, string? SourceType, string? SourceId, string? SourceVersion, Guid? OriginalEntryId);
    private sealed record AccountSeed(Guid Id, string Code, string Name, string? AccountClass, string Currency, decimal OpeningBalance);
    private sealed record AccountMovement(Guid AccountId, decimal Debit, decimal Credit, int Count);
    private sealed record LedgerRow(Guid LineId, Guid EntryId, Guid FiscalPeriodId, Guid AccountId, string AccountCode, string AccountName, string? AccountClass, string Currency, decimal AccountOpeningBalance, string VoucherNumber, DateOnly PostingDate, DateTime EntryUtc, string? Description, decimal Debit, decimal Credit, string LineCurrency, string? SourceType, string? SourceId, string? SourceVersion, Guid? OriginalEntryId);
    private sealed record TaxFactRow(Guid EntryId, string? PolicyPackKey, string? PolicyPackVersion, string Currency, decimal Debit, decimal Credit, string Json);
    private sealed record ParsedTaxFact(Guid EntryId, string PolicyPackKey, string PolicyPackVersion, string TaxRuleKey, string TaxTreatment, decimal TaxableAmount, decimal TaxAmount, string Currency);
}
