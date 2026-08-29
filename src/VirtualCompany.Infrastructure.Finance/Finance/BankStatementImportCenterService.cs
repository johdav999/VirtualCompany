using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class BankStatementImportOptions
{
    public const string SectionName = "BankStatementImports";
    public long MaximumUploadBytes { get; set; } = 20 * 1024 * 1024;
    public int MaximumRows { get; set; } = 100_000;
    public int CommitBatchSize { get; set; } = 250;
}

public sealed class BankStatementImportCenterService : IBankStatementImportCenterService
{
    private readonly VirtualCompanyDbContext _db;
    private readonly IReadOnlyList<IBankStatementFileParser> _parsers;
    private readonly ICompanyDocumentStorage _storage;
    private readonly ICompanyDocumentVirusScanner _scanner;
    private readonly IBankTransactionCommandService _bankTransactions;
    private readonly IAuditEventWriter _audit;
    private readonly ICompanyContextAccessor? _companyContext;
    private readonly BankStatementImportOptions _options;
    private readonly TimeProvider _time;

    public BankStatementImportCenterService(VirtualCompanyDbContext db, IEnumerable<IBankStatementFileParser> parsers,
        ICompanyDocumentStorage storage, ICompanyDocumentVirusScanner scanner,
        IBankTransactionCommandService bankTransactions, IAuditEventWriter audit,
        IOptions<BankStatementImportOptions> options, TimeProvider time,
        ICompanyContextAccessor? companyContext = null)
    {
        _db = db; _parsers = parsers.ToArray(); _storage = storage; _scanner = scanner;
        _bankTransactions = bankTransactions; _audit = audit; _companyContext = companyContext;
        _options = options.Value; _time = time;
    }

    public async Task<BankStatementImportWorkspaceDto> GetWorkspaceAsync(Guid companyId, CancellationToken cancellationToken)
    {
        EnsureTenant(companyId);
        var accounts = await _db.CompanyBankAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.IsActive).OrderByDescending(x => x.IsPrimary).ThenBy(x => x.DisplayName)
            .Select(x => new BankStatementImportAccountDto(x.Id, x.DisplayName, x.BankName, x.MaskedAccountNumber, x.Currency))
            .ToArrayAsync(cancellationToken);
        var profiles = await _db.BankStatementCsvMappingProfiles.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.Versions).Where(x => x.CompanyId == companyId && x.IsActive)
            .OrderBy(x => x.Name).ToArrayAsync(cancellationToken);
        var jobs = await _db.BankStatementImportJobs.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.BankAccount).Include(x => x.Issues)
            .Where(x => x.CompanyId == companyId).OrderByDescending(x => x.UpdatedUtc).Take(20)
            .ToArrayAsync(cancellationToken);
        return new(accounts, profiles.Select(MapProfile).ToArray(), jobs.Select(x => MapJob(x, [])).ToArray());
    }

    public async Task<BankStatementImportJobDto?> GetJobAsync(Guid companyId, Guid jobId, CancellationToken cancellationToken)
    {
        EnsureTenant(companyId);
        var job = await _db.BankStatementImportJobs.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.BankAccount).Include(x => x.Issues).Include(x => x.Rows)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == jobId, cancellationToken);
        return job is null ? null : MapJob(job, job.Rows.OrderBy(x => x.RowNumber).Take(500));
    }

    public async Task<BankStatementImportJobDto> PreviewAsync(PreviewBankStatementImportCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        await EnsureActiveMemberAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        if (command.ContentLength <= 0 || command.ContentLength > _options.MaximumUploadBytes)
            throw new BankStatementImportOperationException(BankStatementImportReasonCodes.FileTooLarge,
                $"The file must be between 1 byte and {_options.MaximumUploadBytes / (1024 * 1024)} MB.");
        var account = await _db.CompanyBankAccounts.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.BankAccountId && x.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException("The selected bank account was not found.");
        var fileName = NormalizeFileName(command.OriginalFileName);
        await using var buffered = await ReadBoundedAsync(command.Content, command.ContentLength, cancellationToken);
        var checksum = Sha256(buffered.ToArray());
        var duplicate = await _db.BankStatementImportJobs.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && x.Checksum == checksum && x.Status != BankStatementImportJobStatuses.Failed)
            .OrderByDescending(x => x.CreatedUtc).FirstOrDefaultAsync(cancellationToken);
        if (duplicate is not null)
            throw new BankStatementImportOperationException(BankStatementImportReasonCodes.DuplicateFile,
                $"This file was already previewed as import {duplicate.Id:D}.", true);

        var now = _time.GetUtcNow().UtcDateTime;
        var jobId = Guid.NewGuid();
        var extension = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        var storageKey = $"companies/{command.CompanyId:N}/finance/statement-imports/{jobId:N}/source{(extension.Length > 0 ? "." + extension : string.Empty)}";
        var job = new BankStatementImportJob(jobId, command.CompanyId, account.Id, fileName, command.ContentType,
            command.ContentLength, storageKey, checksum, command.ActorUserId, now);
        _db.BankStatementImportJobs.Add(job);
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            buffered.Position = 0;
            var write = await _storage.WriteAsync(new DocumentStorageWriteRequest(command.CompanyId, jobId, storageKey,
                fileName, command.ContentType, buffered), cancellationToken);
            var scan = await _scanner.ScanAsync(new CompanyDocumentVirusScanRequest(command.CompanyId, jobId,
                write.StorageKey, write.StorageUrl, fileName, command.ContentType, command.ContentLength,
                new Dictionary<string, JsonNode?> { ["purpose"] = JsonValue.Create("bank_statement_import"), ["checksum"] = JsonValue.Create(checksum) }), cancellationToken);
            if (scan.Outcome == CompanyDocumentVirusScanOutcome.Blocked)
                throw new BankStatementImportOperationException(BankStatementImportReasonCodes.MalwareBlocked,
                    "The file was blocked by content security checks and was not parsed.");
            if (scan.Outcome == CompanyDocumentVirusScanOutcome.Error)
                throw new BankStatementImportOperationException(BankStatementImportReasonCodes.ScanUnavailable,
                    "The file could not be checked safely. Try again after content scanning is available.");

            var parser = _parsers.FirstOrDefault(x => x.Supports(fileName, command.ContentType)) ??
                throw new BankStatementImportOperationException(BankStatementImportReasonCodes.UnsupportedFormat,
                    "Only supported ISO 20022 XML and mapped CSV files can be imported.");
            BankStatementCsvMappingProfileDto? profile = null;
            if (parser is CsvBankStatementParser)
                profile = await ResolveProfileAsync(command.CompanyId, command.CsvMappingProfileId,
                    command.CsvMappingProfileVersion, cancellationToken);
            await using var source = await _storage.OpenReadAsync(storageKey, cancellationToken);
            var parsed = await parser.ParseAsync(new BankStatementParseRequest(fileName, command.ContentType, profile,
                account.Currency, account.MaskedAccountNumber, account.ExternalCode), source, cancellationToken);
            if (parsed.Rows.Count > _options.MaximumRows)
                throw new BankStatementImportOperationException(BankStatementImportReasonCodes.FileTooLarge,
                    $"The file contains more than the supported {_options.MaximumRows:N0} rows.");
            await PersistPreviewAsync(job, account, parsed, profile, command, now, cancellationToken);
            return (await GetJobAsync(command.CompanyId, job.Id, cancellationToken))!;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _db.ChangeTracker.Clear();
            var failed = await _db.BankStatementImportJobs.IgnoreQueryFilters()
                .SingleAsync(x => x.CompanyId == command.CompanyId && x.Id == jobId, cancellationToken);
            var operation = exception as BankStatementImportOperationException;
            failed.Fail(operation?.ReasonCode ?? BankStatementImportReasonCodes.MalformedFile,
                operation?.SafeMessage ?? "The file could not be parsed safely.", _time.GetUtcNow().UtcDateTime);
            await WriteAuditAsync(command.CompanyId, command.ActorUserId, "accounting.bank_statement_preview.failed",
                failed.Id, AuditEventOutcomes.Failed, failed.FailureSummary!, command.CorrelationId,
                new() { ["reasonCode"] = failed.FailureCode, ["checksum"] = failed.Checksum }, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            throw operation ?? new BankStatementImportOperationException(BankStatementImportReasonCodes.MalformedFile,
                "The file could not be parsed safely.", false, exception);
        }
    }

    public async Task<BankStatementImportJobDto> CommitAsync(CommitBankStatementImportCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        await EnsureActiveMemberAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        var job = await _db.BankStatementImportJobs.IgnoreQueryFilters().Include(x => x.BankAccount)
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.JobId, cancellationToken)
            ?? throw new KeyNotFoundException("The statement import job was not found.");
        job.EnsureVersion(command.ExpectedVersion);
        if (job.ErrorRowCount > 0)
            throw new BankStatementImportOperationException(BankStatementImportReasonCodes.RowInvalid,
                "Resolve or skip every blocking issue before importing this statement.");
        var rows = await _db.BankStatementImportJobRows.IgnoreQueryFilters()
            .Where(x => x.CompanyId == command.CompanyId && x.JobId == job.Id &&
                x.Outcome == BankStatementImportRowOutcomes.Accepted && x.ProcessedUtc == null)
            .OrderBy(x => x.RowNumber).Take(_options.CommitBatchSize).ToArrayAsync(cancellationToken);
        var now = _time.GetUtcNow().UtcDateTime;
        if (rows.Length == 0)
        {
            if (job.Status != BankStatementImportJobStatuses.Completed)
            {
                job.BeginCommit(command.ExpectedVersion, now);
                job.RecordCommittedChunk(job.LastCommittedRowNumber, 0, true, command.ActorUserId, now);
                await _db.SaveChangesAsync(cancellationToken);
            }
            return (await GetJobAsync(command.CompanyId, job.Id, cancellationToken))!;
        }
        job.BeginCommit(command.ExpectedVersion, now);
        var sourceKey = SourceKey(job.Format);
        var batchHash = Sha256(Encoding.UTF8.GetBytes(string.Join('|', rows.Select(x => x.RowHash))));
        var importRows = rows.Select(x => new ImportBankStatementRowDto(x.RowIdentity, x.BookingDateUtc!.Value,
            x.ValueDateUtc ?? x.BookingDateUtc.Value, x.Amount!.Value, x.Currency!, x.ReferenceText ?? string.Empty,
            x.Counterparty ?? string.Empty, x.ExternalReference)).ToArray();
        var result = await _bankTransactions.ImportStatementAsync(new ImportBankStatementCommand(command.CompanyId,
            job.BankAccountId, sourceKey, $"{job.Id:N}:{rows[0].RowNumber}-{rows[^1].RowNumber}", batchHash,
            importRows, command.ActorUserId, command.CorrelationId), cancellationToken);
        var conflicts = result.ConflictRowIdentities.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var importedLookup = await _db.BankTransactions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && x.BankAccountId == job.BankAccountId &&
                x.ImportSource == sourceKey && rows.Select(r => r.RowIdentity).Contains(x.RowIdentity!))
            .Select(x => new { x.RowIdentity, x.Id }).ToDictionaryAsync(x => x.RowIdentity!, x => x.Id,
                StringComparer.OrdinalIgnoreCase, cancellationToken);
        var processed = 0;
        foreach (var row in rows)
        {
            if (conflicts.Contains(row.RowIdentity)) row.MarkConflict("A transaction with this identity already exists with different source content.", now);
            else { row.MarkImported(importedLookup.GetValueOrDefault(row.RowIdentity), now); processed++; }
        }
        var anyRemaining = await _db.BankStatementImportJobRows.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.CompanyId == command.CompanyId && x.JobId == job.Id &&
                x.Outcome == BankStatementImportRowOutcomes.Accepted && x.ProcessedUtc == null && !rows.Select(r => r.Id).Contains(x.Id), cancellationToken);
        job.RecordCommittedChunk(rows[^1].RowNumber, processed, !anyRemaining && conflicts.Count == 0,
            command.ActorUserId, now);
        if (conflicts.Count > 0) job.RecordCommitConflicts(conflicts.Count, now);
        await WriteAuditAsync(command.CompanyId, command.ActorUserId, "accounting.bank_statement_import.chunk_committed",
            job.Id, conflicts.Count == 0 ? AuditEventOutcomes.Succeeded : AuditEventOutcomes.Pending,
            conflicts.Count == 0 ? "A validated statement import chunk was committed through the bank transaction boundary." :
                "A statement import chunk was committed with explicit row conflicts requiring review.", command.CorrelationId,
            new() { ["processed"] = processed.ToString(CultureInfo.InvariantCulture),
                ["conflicts"] = conflicts.Count.ToString(CultureInfo.InvariantCulture), ["checksum"] = job.Checksum }, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return (await GetJobAsync(command.CompanyId, job.Id, cancellationToken))!;
    }

    public async Task<BankStatementImportJobDto> DecideConflictAsync(DecideBankStatementImportConflictCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        await EnsureActiveMemberAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        if (!string.Equals(command.Decision, BankStatementImportConflictDecisions.Skip, StringComparison.OrdinalIgnoreCase))
            throw new BankStatementImportOperationException(BankStatementImportReasonCodes.InvalidMapping,
                "The supported decision for an invalid or conflicting row is skip.");
        var job = await _db.BankStatementImportJobs.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.JobId, cancellationToken)
            ?? throw new KeyNotFoundException("The statement import job was not found.");
        job.EnsureVersion(command.ExpectedVersion);
        var row = await _db.BankStatementImportJobRows.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.JobId == job.Id && x.Id == command.RowId, cancellationToken)
            ?? throw new KeyNotFoundException("The statement row was not found.");
        if (row.Outcome is not (BankStatementImportRowOutcomes.Error or BankStatementImportRowOutcomes.Duplicate))
            throw new BankStatementImportOperationException(BankStatementImportReasonCodes.RowInvalid,
                "Only a duplicate or blocking row can be skipped.");
        var now = _time.GetUtcNow().UtcDateTime;
        row.Skip(command.Reason, now);
        var remainingCandidates = await _db.BankStatementImportJobRows.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.CompanyId == command.CompanyId && x.JobId == job.Id &&
                x.Outcome == BankStatementImportRowOutcomes.Accepted && x.ProcessedUtc == null, cancellationToken);
        job.ResolveIssueRow(!remainingCandidates, command.ActorUserId, now);
        await WriteAuditAsync(command.CompanyId, command.ActorUserId, "accounting.bank_statement_import.row_skipped",
            job.Id, AuditEventOutcomes.Succeeded, "A blocking statement row was explicitly skipped with a retained reason.",
            command.CorrelationId, new() { ["rowId"] = row.Id.ToString("N"), ["reason"] = command.Reason }, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return (await GetJobAsync(command.CompanyId, job.Id, cancellationToken))!;
    }

    public async Task<BankStatementCsvMappingProfileDto> CreateCsvProfileAsync(
        CreateBankStatementCsvMappingProfileCommand command, CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        await EnsureActiveMemberAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        var now = _time.GetUtcNow().UtcDateTime;
        var profile = new BankStatementCsvMappingProfile(Guid.NewGuid(), command.CompanyId, command.Name,
            command.ActorUserId, now);
        var version = new BankStatementCsvMappingProfileVersion(Guid.NewGuid(), command.CompanyId, profile.Id, 1,
            command.Delimiter, command.CultureName, command.DateFormat, command.HasHeader, command.BookingDateColumn,
            command.ValueDateColumn, command.AmountColumn, command.DebitColumn, command.CreditColumn,
            command.CurrencyColumn, command.ReferenceColumn, command.CounterpartyColumn,
            command.ExternalReferenceColumn, command.AccountIdentifierColumn, command.DefaultCurrency,
            command.ActorUserId, now);
        _db.BankStatementCsvMappingProfiles.Add(profile);
        _db.BankStatementCsvMappingProfileVersions.Add(version);
        await WriteAuditAsync(command.CompanyId, command.ActorUserId, "accounting.bank_statement_csv_profile.created",
            profile.Id, AuditEventOutcomes.Succeeded, "A reusable, versioned CSV statement mapping profile was created.",
            command.CorrelationId, new() { ["version"] = "1", ["culture"] = command.CultureName }, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return MapProfile(profile, version);
    }

    public async Task<BankStatementCsvMappingProfileDto> CreateCsvProfileVersionAsync(
        CreateBankStatementCsvMappingProfileVersionCommand command, CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        await EnsureActiveMemberAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        var profile = await _db.BankStatementCsvMappingProfiles.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.ProfileId && x.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException("The CSV mapping profile was not found.");
        if (profile.CurrentVersion != command.ExpectedCurrentVersion)
            throw new BankStatementImportOperationException(BankStatementImportReasonCodes.VersionConflict,
                "The CSV mapping profile changed after it was loaded. Refresh and try again.", true);
        var now = _time.GetUtcNow().UtcDateTime;
        profile.AdvanceVersion(now);
        var version = new BankStatementCsvMappingProfileVersion(Guid.NewGuid(), command.CompanyId, profile.Id,
            profile.CurrentVersion, command.Delimiter, command.CultureName, command.DateFormat, command.HasHeader,
            command.BookingDateColumn, command.ValueDateColumn, command.AmountColumn, command.DebitColumn,
            command.CreditColumn, command.CurrencyColumn, command.ReferenceColumn, command.CounterpartyColumn,
            command.ExternalReferenceColumn, command.AccountIdentifierColumn, command.DefaultCurrency,
            command.ActorUserId, now);
        _db.BankStatementCsvMappingProfileVersions.Add(version);
        await WriteAuditAsync(command.CompanyId, command.ActorUserId, "accounting.bank_statement_csv_profile.version_created",
            profile.Id, AuditEventOutcomes.Succeeded, "A new immutable CSV mapping profile version was created.",
            command.CorrelationId, new() { ["version"] = profile.CurrentVersion.ToString(CultureInfo.InvariantCulture),
                ["culture"] = command.CultureName }, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return MapProfile(profile, version);
    }

    private async Task PersistPreviewAsync(BankStatementImportJob job, CompanyBankAccount account,
        ParsedBankStatement parsed, BankStatementCsvMappingProfileDto? profile,
        PreviewBankStatementImportCommand command, DateTime now, CancellationToken cancellationToken)
    {
        var fileIssues = parsed.FileIssues.ToList();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sourceKey = SourceKey(parsed.Format);
        var identities = parsed.Rows.Select(x => x.RowIdentity).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var existing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in identities.Chunk(500))
        {
            var found = await _db.BankTransactions.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == command.CompanyId && x.BankAccountId == account.Id &&
                    x.ImportSource == sourceKey && chunk.Contains(x.RowIdentity!))
                .Select(x => new { x.RowIdentity, x.RowContentHash }).ToArrayAsync(cancellationToken);
            foreach (var item in found) existing[item.RowIdentity!] = item.RowContentHash!;
        }
        var accepted = 0; var duplicates = 0; var rowErrors = 0; var debit = 0m; var credit = 0m;
        foreach (var parsedRow in parsed.Rows)
        {
            var rowHash = ComputeRowHash(parsedRow);
            var issues = parsedRow.Issues.ToList();
            var outcome = parsed.IsPaymentStatusMessage ? BankStatementImportRowOutcomes.PaymentStatus :
                issues.Any(x => x.Severity == BankStatementImportIssueSeverities.Error) ? BankStatementImportRowOutcomes.Error :
                BankStatementImportRowOutcomes.Accepted;
            if (!seen.Add(parsedRow.RowIdentity))
            {
                outcome = BankStatementImportRowOutcomes.Duplicate;
                issues.Add(new(BankStatementImportReasonCodes.DuplicateFile, BankStatementImportIssueSeverities.Warning,
                    "This row identity appears more than once in the file.", parsedRow.RowNumber));
            }
            else if (existing.TryGetValue(parsedRow.RowIdentity, out var existingHash))
            {
                if (string.Equals(existingHash, rowHash, StringComparison.OrdinalIgnoreCase))
                {
                    outcome = BankStatementImportRowOutcomes.Duplicate;
                    issues.Add(new(BankStatementImportReasonCodes.DuplicateFile, BankStatementImportIssueSeverities.Warning,
                        "This transaction was already imported.", parsedRow.RowNumber));
                }
                else
                {
                    outcome = BankStatementImportRowOutcomes.Error;
                    issues.Add(new(BankReconciliationReasonCodes.RowIdentityConflict, BankStatementImportIssueSeverities.Error,
                        "This transaction identity already exists with different source content.", parsedRow.RowNumber));
                }
            }
            var primary = issues.OrderBy(x => x.Severity == BankStatementImportIssueSeverities.Error ? 0 : 1).FirstOrDefault();
            var entity = new BankStatementImportJobRow(Guid.NewGuid(), command.CompanyId, job.Id, parsedRow.RowNumber,
                parsedRow.RowIdentity, rowHash, parsedRow.BookingDateUtc, parsedRow.ValueDateUtc, parsedRow.Amount,
                parsedRow.Currency, parsedRow.ReferenceText, parsedRow.Counterparty, parsedRow.ExternalReference,
                outcome, primary?.Code, primary?.Severity, primary?.Message, parsedRow.PaymentStatus, now);
            _db.BankStatementImportJobRows.Add(entity);
            foreach (var issue in issues) _db.BankStatementImportJobIssues.Add(new BankStatementImportJobIssue(Guid.NewGuid(),
                command.CompanyId, job.Id, issue.Code, issue.Severity, issue.Message, issue.RowNumber, now));
            if (outcome == BankStatementImportRowOutcomes.Accepted) accepted++;
            else if (outcome == BankStatementImportRowOutcomes.Duplicate) duplicates++;
            else if (outcome == BankStatementImportRowOutcomes.Error) rowErrors++;
            if (parsedRow.Amount > 0) credit += parsedRow.Amount.Value;
            if (parsedRow.Amount < 0) debit += Math.Abs(parsedRow.Amount.Value);
        }
        foreach (var issue in fileIssues) _db.BankStatementImportJobIssues.Add(new BankStatementImportJobIssue(Guid.NewGuid(),
            command.CompanyId, job.Id, issue.Code, issue.Severity, issue.Message, issue.RowNumber, now));
        var fileErrors = fileIssues.Count(x => x.Severity == BankStatementImportIssueSeverities.Error);
        job.CompletePreview(parsed.Format, parsed.MessageVersion, parsed.ParserVersion, parsed.StatementIdentity,
            parsed.SourceAccountIdentifier, parsed.Currency, parsed.OpeningBalance, parsed.ClosingBalance, debit, credit,
            parsed.Rows.Count, accepted, duplicates, rowErrors + fileErrors, profile?.Id, profile?.Version,
            parsed.IsPaymentStatusMessage, now);
        await WriteAuditAsync(command.CompanyId, command.ActorUserId, "accounting.bank_statement_preview.completed",
            job.Id, rowErrors + fileErrors == 0 ? AuditEventOutcomes.Succeeded : AuditEventOutcomes.Pending,
            parsed.IsPaymentStatusMessage ? "An ISO 20022 payment-status file was validated without importing transactions." :
                "A bank statement was previewed and validated without changing authoritative transactions.", command.CorrelationId,
            new() { ["format"] = parsed.Format, ["messageVersion"] = parsed.MessageVersion,
                ["rows"] = parsed.Rows.Count.ToString(CultureInfo.InvariantCulture), ["accepted"] = accepted.ToString(CultureInfo.InvariantCulture),
                ["duplicates"] = duplicates.ToString(CultureInfo.InvariantCulture), ["errors"] = (rowErrors + fileErrors).ToString(CultureInfo.InvariantCulture),
                ["checksum"] = job.Checksum }, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<BankStatementCsvMappingProfileDto> ResolveProfileAsync(Guid companyId, Guid? profileId,
        int? versionNumber, CancellationToken cancellationToken)
    {
        if (profileId is not Guid id || id == Guid.Empty)
            throw new BankStatementImportOperationException(BankStatementImportReasonCodes.MissingMappingProfile,
                "Select a CSV mapping profile before previewing this file.");
        var profile = await _db.BankStatementCsvMappingProfiles.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id && x.IsActive, cancellationToken)
            ?? throw new BankStatementImportOperationException(BankStatementImportReasonCodes.MissingMappingProfile,
                "The selected CSV mapping profile was not found.");
        var number = versionNumber ?? profile.CurrentVersion;
        var version = await _db.BankStatementCsvMappingProfileVersions.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.ProfileId == id && x.Version == number, cancellationToken)
            ?? throw new BankStatementImportOperationException(BankStatementImportReasonCodes.MissingMappingProfile,
                "The selected CSV mapping profile version was not found.");
        return MapProfile(profile, version);
    }

    private static BankStatementImportJobDto MapJob(BankStatementImportJob job,
        IEnumerable<BankStatementImportJobRow> rows) => new(job.Id, job.BankAccountId, job.BankAccount.DisplayName,
        job.OriginalFileName, job.ContentLength, job.Checksum, job.Status, job.Format, job.MessageVersion,
        job.ParserVersion, job.StatementIdentity, MaskAccountIdentifier(job.SourceAccountIdentifier), job.Currency, job.OpeningBalance,
        job.ClosingBalance, job.DebitTotal, job.CreditTotal, job.CalculatedClosingBalance, job.TotalRowCount,
        job.AcceptedRowCount, job.DuplicateRowCount, job.ErrorRowCount, job.ImportedRowCount,
        job.LastCommittedRowNumber, job.FailureCode, job.FailureSummary, job.Version, job.CreatedUtc, job.UpdatedUtc,
        job.CompletedUtc, job.Issues.OrderBy(x => x.RowNumber).ThenBy(x => x.Severity)
            .Select(x => new BankStatementImportIssueDto(x.Code, x.Severity, x.Message, x.RowNumber)).ToArray(),
        rows.Select(x => new BankStatementImportRowDto(x.Id, x.RowNumber, x.RowIdentity, x.Outcome,
            x.BookingDateUtc, x.ValueDateUtc, x.Amount, x.Currency, x.ReferenceText, x.Counterparty,
            x.ExternalReference, x.IssueCode, x.IssueSeverity, x.IssueMessage, x.PaymentStatus,
            x.ConflictDecision, x.ImportedBankTransactionId)).ToArray());

    private static BankStatementCsvMappingProfileDto MapProfile(BankStatementCsvMappingProfile profile)
    {
        var version = profile.Versions.Single(x => x.Version == profile.CurrentVersion);
        return MapProfile(profile, version);
    }
    private static BankStatementCsvMappingProfileDto MapProfile(BankStatementCsvMappingProfile profile,
        BankStatementCsvMappingProfileVersion version) => new(profile.Id, profile.Name, version.Version,
        version.Delimiter, version.CultureName, version.DateFormat, version.HasHeader, version.BookingDateColumn,
        version.ValueDateColumn, version.AmountColumn, version.DebitColumn, version.CreditColumn,
        version.CurrencyColumn, version.ReferenceColumn, version.CounterpartyColumn,
        version.ExternalReferenceColumn, version.AccountIdentifierColumn, version.DefaultCurrency, version.CreatedUtc);

    private async Task WriteAuditAsync(Guid companyId, Guid actorId, string action, Guid jobId, string outcome,
        string rationale, string? correlationId, Dictionary<string, string?> metadata, CancellationToken cancellationToken) =>
        await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.User, actorId, action,
            "bank_statement_import_job", jobId.ToString("N"), outcome, rationale,
            ["bank_statement_source", "bank_transaction"], metadata, correlationId, _time.GetUtcNow().UtcDateTime), cancellationToken);

    private async Task EnsureActiveMemberAsync(Guid companyId, Guid actorId, CancellationToken cancellationToken)
    {
        if (actorId == Guid.Empty || !await _db.CompanyMemberships.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId && x.UserId == actorId && x.Status == CompanyMembershipStatus.Active, cancellationToken))
            throw new UnauthorizedAccessException("An active company member is required for statement imports.");
    }
    private void EnsureTenant(Guid companyId)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("Company id is required.", nameof(companyId));
        if (_companyContext?.CompanyId is Guid current && current != companyId)
            throw new UnauthorizedAccessException("Statement imports are scoped to the active company context.");
    }
    private async Task<MemoryStream> ReadBoundedAsync(Stream source, long declaredLength, CancellationToken cancellationToken)
    {
        var result = new MemoryStream((int)Math.Min(declaredLength, int.MaxValue));
        var buffer = new byte[81920]; long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken); if (read == 0) break;
            total += read; if (total > _options.MaximumUploadBytes) { await result.DisposeAsync();
                throw new BankStatementImportOperationException(BankStatementImportReasonCodes.FileTooLarge, "The file exceeds the configured upload limit."); }
            await result.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        if (total != declaredLength) { await result.DisposeAsync();
            throw new BankStatementImportOperationException(BankStatementImportReasonCodes.MalformedFile, "The uploaded file length did not match the received content."); }
        result.Position = 0; return result;
    }
    private static string NormalizeFileName(string fileName)
    {
        var normalized = Path.GetFileName(fileName ?? string.Empty).Trim();
        if (normalized.Length is 0 or > 255) throw new BankStatementImportOperationException(
            BankStatementImportReasonCodes.UnsupportedFormat, "A valid file name is required.");
        return normalized;
    }
    private static string? MaskAccountIdentifier(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier)) return null;
        var compact = new string(identifier.Where(char.IsLetterOrDigit).ToArray());
        return compact.Length <= 4 ? $"•••• {compact}" : $"•••• {compact[^4..]}";
    }
    private static string SourceKey(string? format) => $"manual-{format ?? "statement"}".ToLowerInvariant();
    private static string ComputeRowHash(ParsedBankStatementRow row)
    {
        var canonical = string.Join("|", NormalizeUtc(row.BookingDateUtc!.Value).ToString("O"),
            NormalizeUtc(row.ValueDateUtc ?? row.BookingDateUtc.Value).ToString("O"),
            row.Amount!.Value.ToString("0.00", CultureInfo.InvariantCulture), row.Currency!.Trim().ToUpperInvariant(),
            (row.ReferenceText ?? string.Empty).Trim(), (row.Counterparty ?? string.Empty).Trim(),
            row.ExternalReference?.Trim() ?? string.Empty);
        return Sha256(Encoding.UTF8.GetBytes(canonical));
    }
    private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
    { DateTimeKind.Utc => value, DateTimeKind.Local => value.ToUniversalTime(), _ => DateTime.SpecifyKind(value, DateTimeKind.Utc) };
    private static string Sha256(byte[] content) => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
}
