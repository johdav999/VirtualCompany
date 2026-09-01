using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;
using VirtualCompany.Shared;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AuditPackageOptions
{
    public const string SectionName = "AuditPackages";
    public int PollIntervalSeconds { get; set; } = 15;
    public int ClaimBatchSize { get; set; } = 2;
    public int MaximumAttempts { get; set; } = 4;
    public int BaseRetryDelaySeconds { get; set; } = 15;
    public int LeaseSeconds { get; set; } = 300;
    public int RetentionYears { get; set; } = 7;
    public int DownloadAuthorizationMinutes { get; set; } = 10;
    public int MaximumGeneralLedgerPages { get; set; } = 50;
    public int MaximumDocumentCount { get; set; } = 500;
    public long MaximumDocumentBytes { get; set; } = 25 * 1024 * 1024;
    public long MaximumPackageBytes { get; set; } = 500 * 1024 * 1024;
}

public sealed class AuditPackageTelemetry
{
    private readonly Counter<long> _requests;
    private readonly Counter<long> _generations;
    private readonly Counter<long> _verifications;
    private readonly Histogram<double> _duration;

    public AuditPackageTelemetry(IMeterFactory meters)
    {
        var meter = meters.Create("VirtualCompany.Finance.AuditPackages");
        _requests = meter.CreateCounter<long>("audit_packages.requests");
        _generations = meter.CreateCounter<long>("audit_packages.generations");
        _verifications = meter.CreateCounter<long>("audit_packages.verifications");
        _duration = meter.CreateHistogram<double>("audit_packages.generation.duration_ms");
    }

    public void Requested() => _requests.Add(1);
    public void Generated(string outcome, TimeSpan elapsed)
    {
        _generations.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        _duration.Record(elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("outcome", outcome));
    }
    public void Verified(bool valid) => _verifications.Add(1, new KeyValuePair<string, object?>("valid", valid));
}

public sealed class AuditPackageService : IAuditPackageService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly VirtualCompanyDbContext _db;
    private readonly IAccountingReportingService _reporting;
    private readonly ICompanyDocumentStorage _storage;
    private readonly IKnowledgeAccessPolicyEvaluator _accessPolicy;
    private readonly ICompanyMembershipContextResolver _memberships;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditEventWriter _audit;
    private readonly TimeProvider _time;
    private readonly AuditPackageOptions _options;
    private readonly AuditPackageTelemetry _telemetry;
    private readonly ILogger<AuditPackageService> _logger;

    public AuditPackageService(VirtualCompanyDbContext db, IAccountingReportingService reporting,
        ICompanyDocumentStorage storage, IKnowledgeAccessPolicyEvaluator accessPolicy,
        ICompanyMembershipContextResolver memberships, ICurrentUserAccessor currentUser,
        IAuditEventWriter audit, TimeProvider time, IOptions<AuditPackageOptions> options,
        AuditPackageTelemetry telemetry, ILogger<AuditPackageService> logger)
    {
        _db = db; _reporting = reporting; _storage = storage; _accessPolicy = accessPolicy;
        _memberships = memberships; _currentUser = currentUser; _audit = audit; _time = time;
        _options = options.Value; _telemetry = telemetry; _logger = logger;
    }

    public async Task<AuditPackageWorkspaceDto> ListAsync(ListAuditPackagesQuery query, CancellationToken cancellationToken)
    {
        await RequireViewAsync(query.CompanyId, cancellationToken);
        var take = Math.Clamp(query.Take, 1, 200);
        var source = PackageQuery(query.CompanyId);
        if (query.FiscalPeriodId.HasValue) source = source.Where(x => x.FiscalPeriodId == query.FiscalPeriodId.Value);
        var packages = await source.OrderByDescending(x => x.RequestedUtc).Skip(Math.Max(0, query.Skip)).Take(take).ToListAsync(cancellationToken);
        var mapped = packages.Select(Map).ToArray();
        return new AuditPackageWorkspaceDto(query.CompanyId, mapped.Length,
            mapped.Count(x => x.Status == AuditPackageStatuses.Final),
            mapped.Count(x => x.Status == AuditPackageStatuses.Incomplete),
            mapped.Count(x => x.Status is AuditPackageStatuses.PendingApproval or AuditPackageStatuses.Queued or AuditPackageStatuses.Generating or AuditPackageStatuses.RetryScheduled), mapped);
    }

    public async Task<AuditPackageDto> GetAsync(Guid companyId, Guid packageId, CancellationToken cancellationToken)
    {
        await RequireViewAsync(companyId, cancellationToken);
        return Map(await LoadAsync(companyId, packageId, tracked: false, cancellationToken));
    }

    public async Task<AuditPackagePreviewDto> PreviewAsync(PreviewAuditPackageQuery query,
        CancellationToken cancellationToken)
    {
        await RequireViewAsync(query.CompanyId, cancellationToken);
        var scopeKey = Required(query.ScopeKey, nameof(query.ScopeKey), 100).ToLowerInvariant();
        var scopeVersion = Required(query.ScopeVersion, nameof(query.ScopeVersion), 64);
        var period = await _db.FiscalPeriods.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId && x.Id == query.FiscalPeriodId,
                cancellationToken)
            ?? throw Error("fiscal_period_not_found", "The fiscal period was not found in the requested company.");
        var snapshots = await BuildSnapshotVersionsAsync(query.CompanyId, period, cancellationToken);
        var scopeHash = Hash($"{query.CompanyId:N}|{period.Id:N}|{scopeKey}|{scopeVersion}|{snapshots}");
        var existing = await _db.AuditPackages.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.FiscalPeriodId == period.Id &&
                x.ScopeKey == scopeKey && x.ScopeVersion == scopeVersion && x.ScopeHash == scopeHash)
            .OrderByDescending(x => x.RequestedUtc).FirstOrDefaultAsync(cancellationToken);
        var blockers = period.IsClosed ? Array.Empty<string>() : ["closed_period_required"];
        return new(query.CompanyId, period.Id, period.Name, scopeKey, scopeVersion, scopeHash,
            snapshots, blockers.Length == 0, blockers, existing?.Id, existing?.Status,
            existing?.Version, ArtifactGenerated: false);
    }

    public async Task<AuditPackageDto> RequestAsync(RequestAuditPackageCommand command, CancellationToken cancellationToken)
    {
        var role = await RequireManageAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        var idempotencyKey = Required(command.IdempotencyKey, nameof(command.IdempotencyKey), 200);
        var scopeKey = Required(command.ScopeKey, nameof(command.ScopeKey), 100).ToLowerInvariant();
        var scopeVersion = Required(command.ScopeVersion, nameof(command.ScopeVersion), 64);
        var period = await _db.FiscalPeriods.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.FiscalPeriodId, cancellationToken)
            ?? throw Error("fiscal_period_not_found", "The fiscal period was not found in the requested company.");
        if (!period.IsClosed) throw Error("closed_period_required", "Audit packages can only be requested for a closed fiscal period.", true);

        var snapshotVersions = await BuildSnapshotVersionsAsync(command.CompanyId, period, cancellationToken);
        var scopeHash = Hash($"{command.CompanyId:N}|{period.Id:N}|{scopeKey}|{scopeVersion}|{snapshotVersions}");
        var existingByKey = await _db.AuditPackages.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existingByKey is not null)
        {
            if (existingByKey.FiscalPeriodId != period.Id || existingByKey.ScopeHash != scopeHash)
                throw Error("idempotency_payload_mismatch", "The idempotency key was already used for a different audit-package snapshot.", true);
            return Map(await LoadAsync(command.CompanyId, existingByKey.Id, false, cancellationToken));
        }

        var existingScope = await _db.AuditPackages.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.FiscalPeriodId == period.Id &&
                x.ScopeKey == scopeKey && x.ScopeVersion == scopeVersion && x.ScopeHash == scopeHash, cancellationToken);
        if (existingScope is not null) return Map(await LoadAsync(command.CompanyId, existingScope.Id, false, cancellationToken));

        var now = Now();
        var package = new AuditPackage(Guid.NewGuid(), command.CompanyId, period.Id, scopeKey, scopeVersion,
            scopeHash, snapshotVersions, command.ActorUserId, role, idempotencyKey, now,
            now.AddYears(_options.RetentionYears), _options.MaximumAttempts);
        _db.AuditPackages.Add(package);
        await SaveAsync(cancellationToken);
        _telemetry.Requested();
        await AuditAsync(command.CompanyId, command.ActorUserId, "audit_package_requested", package.Id,
            "Requested an immutable audit package for a frozen closed-period snapshot.", cancellationToken);
        return Map(await LoadAsync(command.CompanyId, package.Id, false, cancellationToken));
    }

    public async Task<AuditPackageDto> ApproveAsync(ApproveAuditPackageCommand command, CancellationToken cancellationToken)
    {
        await RequireApproveAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        var package = await LoadAsync(command.CompanyId, command.PackageId, true, cancellationToken);
        EnsureVersion(package, command.ExpectedVersion);
        var now = Now();
        package.Approve(command.ActorUserId, now);
        package.Approvals.Add(new AuditPackageApproval(Guid.NewGuid(), command.CompanyId, package.Id,
            command.ActorUserId, "approved", command.Reason, now));
        await SaveAsync(cancellationToken);
        await AuditAsync(command.CompanyId, command.ActorUserId, "audit_package_approved", package.Id,
            "Approved bounded audit-package generation for the frozen scope and snapshot versions.", cancellationToken);
        return Map(await LoadAsync(command.CompanyId, package.Id, false, cancellationToken));
    }

    public async Task<AuditPackageDto> CancelAsync(CancelAuditPackageCommand command, CancellationToken cancellationToken)
    {
        await RequireManageAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        var package = await LoadAsync(command.CompanyId, command.PackageId, true, cancellationToken);
        EnsureVersion(package, command.ExpectedVersion);
        package.RequestCancellation(Now());
        await SaveAsync(cancellationToken);
        await AuditAsync(command.CompanyId, command.ActorUserId, "audit_package_cancelled", package.Id,
            "Cancellation was requested before package finalization.", cancellationToken);
        return Map(await LoadAsync(command.CompanyId, package.Id, false, cancellationToken));
    }

    public async Task<AuditPackageDownloadAuthorizationDto> AuthorizeDownloadAsync(
        CreateAuditPackageDownloadAuthorizationCommand command, CancellationToken cancellationToken)
    {
        await RequireViewAsync(command.CompanyId, cancellationToken);
        EnsureCurrentUser(command.ActorUserId);
        var package = await LoadAsync(command.CompanyId, command.PackageId, true, cancellationToken);
        EnsureDownloadable(package);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var now = Now();
        var authorization = new AuditPackageDownloadAuthorization(Guid.NewGuid(), command.CompanyId,
            package.Id, command.ActorUserId, Hash(token), now, now.AddMinutes(_options.DownloadAuthorizationMinutes));
        package.DownloadAuthorizations.Add(authorization);
        await SaveAsync(cancellationToken);
        return new(authorization.Id, package.Id, token, authorization.ExpiresUtc,
            $"internal/companies/{command.CompanyId:D}/finance/accounting/audit-packages/{package.Id:D}/download?token={Uri.EscapeDataString(token)}");
    }

    public async Task<AuditPackageDownloadDto> DownloadAsync(DownloadAuditPackageQuery query, CancellationToken cancellationToken)
    {
        await RequireViewAsync(query.CompanyId, cancellationToken);
        EnsureCurrentUser(query.ActorUserId);
        var tokenHash = Hash(Required(query.Token, nameof(query.Token), 256));
        var package = await LoadAsync(query.CompanyId, query.PackageId, true, cancellationToken);
        EnsureDownloadable(package);
        var authorization = package.DownloadAuthorizations.SingleOrDefault(x => x.TokenHash == tokenHash && x.UserId == query.ActorUserId)
            ?? throw Error("download_authorization_invalid", "The audit-package download authorization is invalid.");
        try { authorization.Redeem(Now()); }
        catch (InvalidOperationException ex) { throw Error("download_authorization_invalid", ex.Message); }
        await SaveAsync(cancellationToken);
        Stream content;
        try { content = await _storage.OpenReadAsync(package.StorageKey!, cancellationToken); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw Error("package_object_unavailable", "The audit-package object is unavailable. Run verification or regenerate from the frozen scope."); }
        await AuditAsync(query.CompanyId, query.ActorUserId, "audit_package_downloaded", package.Id,
            "A one-time package download authorization was redeemed.", cancellationToken);
        return new(package.FileName!, package.MediaType!, content, package.ContentLength!.Value,
            package.PackageChecksum!, package.ManifestChecksum!);
    }

    public async Task<AuditPackageVerificationDto> VerifyAsync(VerifyAuditPackageCommand command, CancellationToken cancellationToken)
    {
        await RequireViewAsync(command.CompanyId, cancellationToken);
        EnsureCurrentUser(command.ActorUserId);
        var package = await LoadAsync(command.CompanyId, command.PackageId, true, cancellationToken);
        if (string.IsNullOrWhiteSpace(package.StorageKey) || string.IsNullOrWhiteSpace(package.PackageChecksum) || string.IsNullOrWhiteSpace(package.ManifestChecksum))
            throw Error("package_not_generated", "Generate the audit package before verification.", true);

        var verification = await VerifyArchiveAsync(package, command.ActorUserId, cancellationToken);
        package.VerificationResults.Add(verification);
        await SaveAsync(cancellationToken);
        _telemetry.Verified(verification.IsValid);
        await AuditAsync(command.CompanyId, command.ActorUserId, "audit_package_verified", package.Id,
            verification.SafeSummary, cancellationToken);
        return Map(verification);
    }

    public async Task<int> ProcessPendingAsync(int batchSize, CancellationToken cancellationToken)
    {
        var now = Now();
        var ids = await _db.AuditPackages.IgnoreQueryFilters().AsNoTracking()
            .Where(x => (x.Status == AuditPackageStatuses.Queued || x.Status == AuditPackageStatuses.RetryScheduled ||
                         x.Status == AuditPackageStatuses.Generating && x.LeaseExpiresUtc <= now) &&
                (!x.NextAttemptUtc.HasValue || x.NextAttemptUtc <= now))
            .OrderBy(x => x.RequestedUtc).Select(x => x.Id).Take(Math.Clamp(batchSize, 1, 20)).ToListAsync(cancellationToken);
        var completed = 0;
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _db.ChangeTracker.Clear();
            if (await GenerateOneAsync(id, cancellationToken)) completed++;
        }
        return completed;
    }

    public async Task<int> ExpireAsync(int batchSize, CancellationToken cancellationToken)
    {
        var now = Now();
        var packages = await _db.AuditPackages.IgnoreQueryFilters()
            .Where(x => (x.Status == AuditPackageStatuses.Final || x.Status == AuditPackageStatuses.Incomplete) && x.RetainUntilUtc <= now)
            .OrderBy(x => x.RetainUntilUtc).Take(Math.Clamp(batchSize, 1, 100)).ToListAsync(cancellationToken);
        foreach (var package in packages)
        {
            if (!string.IsNullOrWhiteSpace(package.StorageKey)) await _storage.DeleteAsync(package.StorageKey, cancellationToken);
            package.Expire(now);
        }
        await SaveAsync(cancellationToken);
        return packages.Count;
    }

    private async Task<bool> GenerateOneAsync(Guid packageId, CancellationToken cancellationToken)
    {
        var package = await _db.AuditPackages.IgnoreQueryFilters().Include(x => x.Artifacts)
            .SingleOrDefaultAsync(x => x.Id == packageId, cancellationToken);
        if (package is null || !package.TryStart(Now(), TimeSpan.FromSeconds(_options.LeaseSeconds))) return false;
        var started = package.StartedUtc!.Value;
        await SaveAsync(cancellationToken);
        try
        {
            var period = await _db.FiscalPeriods.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(x => x.CompanyId == package.CompanyId && x.Id == package.FiscalPeriodId, cancellationToken);
            var items = await CollectEvidenceAsync(package, period, cancellationToken);
            var build = AuditPackageArchiveBuilder.Build(package.CompanyId, period.Id, period.Name,
                package.ScopeKey, package.ScopeVersion, package.ScopeHash, package.SnapshotVersionsJson,
                package.RequestedUtc, items);
            if (build.Archive.LongLength > _options.MaximumPackageBytes)
                throw new AuditPackageException("package_size_limit", "The bounded audit package exceeded the configured size limit.");

            await _db.Entry(package).ReloadAsync(cancellationToken);
            if (package.CancellationRequested)
            {
                package.RequestCancellation(Now()); await SaveAsync(cancellationToken); return false;
            }

            var storageKey = $"audit-packages/{package.CompanyId:N}/{package.FiscalPeriodId:N}/{package.ScopeHash}.zip";
            await using var archiveStream = new MemoryStream(build.Archive, writable: false);
            await _storage.WriteAsync(new DocumentStorageWriteRequest(package.CompanyId, package.Id, storageKey,
                $"audit-package-{period.Name}-{package.ScopeVersion}.zip", "application/zip", archiveStream), cancellationToken);

            await _db.Entry(package).ReloadAsync(cancellationToken);
            if (package.CancellationRequested)
            {
                await _storage.DeleteAsync(storageKey, cancellationToken);
                package.RequestCancellation(Now()); await SaveAsync(cancellationToken); return false;
            }

            var artifacts = build.Items.Select(item => new AuditPackageArtifact(Guid.NewGuid(), package.CompanyId,
                package.Id, item.Sequence, item.ArtifactType, item.Path, item.Status, item.IsRequired,
                item.SourceType, item.SourceReference, item.SourceVersion, item.DefinitionVersion,
                item.Sha256, item.ContentLength, item.SafeDetail)).ToArray();
            package.ReplaceArtifacts(artifacts);
            package.Complete(build.ManifestJson, build.ManifestChecksum, build.PackageChecksum, storageKey,
                $"audit-package-{SanitizeFileName(period.Name)}-{package.ScopeVersion}.zip", "application/zip",
                build.Archive.LongLength, build.IsComplete, Now());
            package.GenerationAttempts.Add(new AuditPackageGenerationAttempt(Guid.NewGuid(), package.CompanyId,
                package.Id, package.AttemptCount, build.IsComplete ? "final" : "incomplete", null,
                build.IsComplete ? null : "One or more required evidence items were missing, inaccessible, or corrupt.", started, Now()));
            await SaveAsync(cancellationToken);
            _telemetry.Generated(package.Status, Now() - started);
            await AuditAsync(package.CompanyId, null, "audit_package_generated", package.Id,
                build.IsComplete ? "Generated a complete checksum-verifiable audit package." : "Generated an incomplete package with bounded evidence findings; it was not labeled final.", cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit package {AuditPackageId} generation attempt failed safely.", package.Id);
            var code = ex is AuditPackageException packageError ? packageError.ReasonCode : ex is IOException ? "object_storage_unavailable" : "generation_failed";
            var summary = ex is AuditPackageException ? ex.Message : "Audit package generation failed safely and will retry within the configured bound.";
            var now = Now();
            package.GenerationAttempts.Add(new AuditPackageGenerationAttempt(Guid.NewGuid(), package.CompanyId,
                package.Id, package.AttemptCount, "failed", code, summary, started, now));
            package.ScheduleRetry(code, summary, now.AddSeconds(_options.BaseRetryDelaySeconds * Math.Pow(2, Math.Max(0, package.AttemptCount - 1))), now);
            await SaveAsync(cancellationToken);
            _telemetry.Generated(package.Status, now - started);
            return false;
        }
    }

    private async Task<IReadOnlyList<AuditPackageContentItem>> CollectEvidenceAsync(AuditPackage package,
        FiscalPeriod period, CancellationToken cancellationToken)
    {
        var items = new List<AuditPackageContentItem>();
        await AddReportingEvidenceAsync(items, package, cancellationToken);

        var snapshots = await _db.FinancialReportSuiteSnapshots.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == package.CompanyId && x.FiscalPeriodId == period.Id)
            .OrderBy(x => x.ReportKind).ThenByDescending(x => x.CreatedUtc).ToListAsync(cancellationToken);
        foreach (var snapshot in snapshots.GroupBy(x => x.ReportKind).Select(x => x.First()))
            AddJson(items, "financial_statement", $"statements/{SafePath(snapshot.ReportKind)}.json", true,
                "financial_report_suite_snapshot", snapshot.Id.ToString("D"), snapshot.CalculationVersion,
                snapshot.ReportDefinitionHash ?? snapshot.MappingVersion, JsonDocument.Parse(snapshot.ReportJson).RootElement.Clone());
        if (snapshots.Count == 0) AddMissing(items, "financial_statement", "statements/missing.json", true,
            "financial_report_suite_snapshot", $"period:{period.Id:D}", "No financial-statement snapshot exists for the closed period.");

        var filingPeriods = await _db.VatFilingPeriods.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == package.CompanyId && x.FiscalPeriodId == period.Id).OrderBy(x => x.PeriodCode).ToListAsync(cancellationToken);
        var vatReturns = await _db.VatReturns.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == package.CompanyId && filingPeriods.Select(p => p.Id).Contains(x.FilingPeriodId))
            .OrderBy(x => x.FilingPeriodId).ThenByDescending(x => x.Version).ToListAsync(cancellationToken);
        AddJson(items, "vat_returns", "tax/vat-returns.json", filingPeriods.Count > 0,
            "vat_return", $"period:{period.Id:D}", "vat-return-v1", null,
            new { filingPeriods, returns = vatReturns.Select(x => new { x.Id, x.FilingPeriodId, x.Version, x.Status, x.InputHash, x.CalculationChecksum, x.PackageChecksum, x.FinalizedUtc }) });
        if (filingPeriods.Count > 0 && !vatReturns.Any(x => x.Status == VatReturnStatuses.Locked))
            AddMissing(items, "vat_return_package", "tax/vat-return-package-missing.json", true,
                "vat_return", $"period:{period.Id:D}", "A VAT filing period exists but no finalized return package is available.");

        var close = await _db.AccountingCloseInstances.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == package.CompanyId && x.FiscalPeriodId == period.Id)
            .OrderByDescending(x => x.CompletedUtc).FirstOrDefaultAsync(cancellationToken);
        if (close is null)
        {
            AddMissing(items, "close_history", "close/missing.json", true, "accounting_close_instance",
                $"period:{period.Id:D}", "No accounting-close instance is linked to the closed period.");
        }
        else
        {
            var tasks = await _db.AccountingCloseTasks.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == package.CompanyId && x.CloseInstanceId == close.Id).OrderBy(x => x.Sequence)
                .Select(x => new { x.Id, x.Key, x.Title, x.Status, x.RequiresSignOff, x.ApprovalRequestId, x.CompletedByUserId, x.CompletedUtc, x.ReportedAmount, x.Version })
                .ToListAsync(cancellationToken);
            var history = await _db.AccountingCloseStatusHistory.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == package.CompanyId && x.CloseInstanceId == close.Id)
                .OrderBy(x => x.OccurredUtc).Select(x => new { x.Id, x.CloseTaskId, x.Action, x.FromStatus, x.ToStatus, x.ActorUserId, x.Reason, x.OccurredUtc })
                .ToListAsync(cancellationToken);
            AddJson(items, "close_history", "close/history.json", true, "accounting_close_instance",
                close.Id.ToString("D"), close.Version.ToString(), close.TemplateVersionNumber.ToString(),
                new { close.Id, close.Status, close.TemplateId, close.TemplateVersionId, close.TemplateVersionNumber, close.StartedUtc, close.CompletedUtc, close.Version, tasks, history });
            await AddDocumentsAsync(items, package, close.Id, cancellationToken);
        }

        var policyPacks = await _db.AccountingPolicyPackSelections.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == package.CompanyId && x.EffectiveFrom <= DateOnly.FromDateTime(period.EndUtc))
            .OrderBy(x => x.EffectiveFrom).Select(x => new { x.Id, x.PackKey, x.PackVersion, x.DefinitionHash, x.IsStatutoryComplianceValidated, x.EffectiveFrom, x.EffectiveTo, x.SelectedByUserId, x.SelectedUtc })
            .ToListAsync(cancellationToken);
        if (policyPacks.Count == 0) AddMissing(items, "policy_pack", "policies/missing.json", true,
            "accounting_policy_pack_selection", $"period:{period.Id:D}", "No policy-pack selection covers this period.");
        else AddJson(items, "policy_pack", "policies/policy-pack-selections.json", true,
            "accounting_policy_pack_selection", $"period:{period.Id:D}", policyPacks.Last().PackVersion,
            policyPacks.Last().DefinitionHash, policyPacks);

        var providerExceptions = await _db.AuditEvents.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == package.CompanyId && x.OccurredUtc >= period.StartUtc && x.OccurredUtc < period.EndUtc &&
                (x.Action.Contains("provider") || x.Action.Contains("integration")) && x.Outcome != AuditEventOutcomes.Succeeded)
            .OrderBy(x => x.OccurredUtc).Take(5000)
            .Select(x => new { x.Id, x.Action, x.TargetType, x.TargetId, x.Outcome, x.RationaleSummary, x.CorrelationId, x.OccurredUtc })
            .ToListAsync(cancellationToken);
        AddJson(items, "provider_exceptions", "exceptions/provider-exceptions.json", false,
            "audit_event", $"period:{period.Id:D}", "audit-event-v1", null, providerExceptions);

        var approvals = await _db.AuditEvents.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == package.CompanyId && x.OccurredUtc >= period.StartUtc && x.OccurredUtc < period.EndUtc &&
                (x.TargetType == "approval_request" || x.Action.Contains("approv") || x.Action.Contains("sign")))
            .OrderBy(x => x.OccurredUtc).Take(10000)
            .Select(x => new { x.Id, x.ActorType, x.ActorId, x.Action, x.TargetType, x.TargetId, x.Outcome, x.RationaleSummary, x.CorrelationId, x.OccurredUtc })
            .ToListAsync(cancellationToken);
        AddJson(items, "approvals_signoffs", "approvals/approval-history.json", true,
            "audit_event", $"period:{period.Id:D}", "audit-event-v1", null, approvals);

        var compliance = await _db.ComplianceObligationInstances.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == package.CompanyId && x.CreatedUtc < period.EndUtc && x.UpdatedUtc >= period.StartUtc)
            .OrderBy(x => x.DueDate).Select(x => new { x.Id, x.DefinitionKey, x.Title, x.PolicyPackKey, x.PolicyPackVersion, x.PolicyPackDefinitionHash, x.DueDate, x.Status, x.SourceHash, x.ExportReference, x.ExportChecksum, x.Version })
            .ToListAsync(cancellationToken);
        AddJson(items, "compliance_obligations", "tax/compliance-obligations.json", false,
            "compliance_obligation", $"period:{period.Id:D}", "compliance-obligation-v1", null, compliance);
        return items;
    }

    private async Task AddReportingEvidenceAsync(List<AuditPackageContentItem> items, AuditPackage package,
        CancellationToken cancellationToken)
    {
        try
        {
            var trial = await _reporting.GetTrialBalanceAsync(new(package.CompanyId, package.FiscalPeriodId), cancellationToken);
            AddJson(items, "trial_balance", "ledger/trial-balance.json", true, "ledger", $"period:{package.FiscalPeriodId:D}",
                "trial-balance-v1", trial.Checksum, trial);
        }
        catch (Exception ex) { AddMissing(items, "trial_balance", "ledger/trial-balance-missing.json", true, "ledger", $"period:{package.FiscalPeriodId:D}", SafeFailure(ex, "Trial balance could not be assembled.")); }

        try
        {
            var pages = new List<GeneralLedgerReportDto>();
            for (var page = 1; page <= _options.MaximumGeneralLedgerPages; page++)
            {
                var result = await _reporting.GetGeneralLedgerAsync(new(package.CompanyId, package.FiscalPeriodId, null, page, 1000), cancellationToken);
                pages.Add(result);
                if (!result.HasMore) break;
                if (page == _options.MaximumGeneralLedgerPages)
                    throw Error("general_ledger_bound_exceeded", "The general ledger exceeds the configured bounded package page count.");
            }
            AddJson(items, "general_ledger", "ledger/general-ledger.json", true, "ledger", $"period:{package.FiscalPeriodId:D}", "general-ledger-v1", null, pages);
            var significant = pages.SelectMany(x => x.Accounts).SelectMany(x => x.Lines)
                .OrderByDescending(x => Math.Abs(x.Debit - x.Credit)).Take(1000).ToArray();
            AddJson(items, "significant_journals", "ledger/significant-journals.json", true,
                "ledger_entry", $"period:{package.FiscalPeriodId:D}", "significant-journal-v1", null, significant);
        }
        catch (Exception ex) { AddMissing(items, "general_ledger", "ledger/general-ledger-missing.json", true, "ledger", $"period:{package.FiscalPeriodId:D}", SafeFailure(ex, "General ledger could not be assembled.")); }

        try
        {
            var tax = await _reporting.GetTaxSummaryAsync(new(package.CompanyId, package.FiscalPeriodId), cancellationToken);
            AddJson(items, "tax_summary", "tax/tax-summary.json", true, "ledger_tax_facts",
                $"period:{package.FiscalPeriodId:D}", "tax-summary-v1", tax.Checksum, tax);
        }
        catch (Exception ex) { AddMissing(items, "tax_summary", "tax/tax-summary-missing.json", true, "ledger_tax_facts", $"period:{package.FiscalPeriodId:D}", SafeFailure(ex, "Tax summary could not be assembled.")); }

        try
        {
            var reconciliation = await _reporting.GetControlAccountReconciliationAsync(new(package.CompanyId, package.FiscalPeriodId), cancellationToken);
            AddJson(items, "reconciliation", "reconciliations/control-accounts.json", true,
                "control_account_reconciliation", $"period:{package.FiscalPeriodId:D}", "control-reconciliation-v1", null, reconciliation);
        }
        catch (Exception ex) { AddMissing(items, "reconciliation", "reconciliations/control-accounts-missing.json", true, "control_account_reconciliation", $"period:{package.FiscalPeriodId:D}", SafeFailure(ex, "Control-account reconciliation could not be assembled.")); }

        try
        {
            var history = await _reporting.GetPeriodHistoryAsync(package.CompanyId, package.FiscalPeriodId, cancellationToken);
            AddJson(items, "period_history", "close/period-history.json", true, "fiscal_period_history",
                $"period:{package.FiscalPeriodId:D}", "period-history-v1", null, history);
        }
        catch (Exception ex) { AddMissing(items, "period_history", "close/period-history-missing.json", true, "fiscal_period_history", $"period:{package.FiscalPeriodId:D}", SafeFailure(ex, "Period history could not be assembled.")); }
    }

    private async Task AddDocumentsAsync(List<AuditPackageContentItem> items, AuditPackage package,
        Guid closeInstanceId, CancellationToken cancellationToken)
    {
        var evidence = await _db.AccountingCloseTaskEvidence.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.Document).Where(x => x.CompanyId == package.CompanyId && x.CloseTask.CloseInstanceId == closeInstanceId)
            .OrderBy(x => x.DocumentId).Take(_options.MaximumDocumentCount + 1).ToListAsync(cancellationToken);
        if (evidence.Count > _options.MaximumDocumentCount)
        {
            AddMissing(items, "source_document", "documents/document-bound-exceeded.json", true,
                "company_document", $"close:{closeInstanceId:D}", "The close evidence exceeds the configured bounded document count.");
            evidence = evidence.Take(_options.MaximumDocumentCount).ToList();
        }
        var context = new CompanyKnowledgeAccessContext(package.CompanyId, UserId: package.RequestedByUserId,
            MembershipRole: package.RequestedByRole);
        foreach (var link in evidence)
        {
            var document = link.Document;
            var path = $"documents/{document.Id:N}-{SafePath(document.OriginalFileName)}";
            if (!_accessPolicy.CanAccess(context, document))
            {
                items.Add(new("source_document", path, AuditPackageArtifactStatuses.Inaccessible, true,
                    "company_document", document.Id.ToString("D"), document.UpdatedUtc.ToString("O"), link.ContentHash,
                    null, "The requesting review scope cannot access this linked document."));
                continue;
            }
            if (document.FileSizeBytes > _options.MaximumDocumentBytes)
            {
                items.Add(new("source_document", path, AuditPackageArtifactStatuses.Missing, true,
                    "company_document", document.Id.ToString("D"), document.UpdatedUtc.ToString("O"), link.ContentHash,
                    null, "The linked document exceeds the configured per-document package limit."));
                continue;
            }
            try
            {
                await using var source = await _storage.OpenReadAsync(document.StorageKey, cancellationToken);
                var content = await ReadBoundedAsync(source, _options.MaximumDocumentBytes, cancellationToken);
                var checksum = AuditPackageArchiveBuilder.Hash(content);
                if (!string.IsNullOrWhiteSpace(link.ContentHash) && !checksum.Equals(link.ContentHash, StringComparison.OrdinalIgnoreCase))
                {
                    items.Add(new("source_document", path, AuditPackageArtifactStatuses.Corrupt, true,
                        "company_document", document.Id.ToString("D"), document.UpdatedUtc.ToString("O"), link.ContentHash,
                        null, "The source document checksum does not match the retained close evidence."));
                }
                else items.Add(new("source_document", path, AuditPackageArtifactStatuses.Included, true,
                    "company_document", document.Id.ToString("D"), document.UpdatedUtc.ToString("O"), link.ContentHash,
                    content));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                items.Add(new("source_document", path, AuditPackageArtifactStatuses.Missing, true,
                    "company_document", document.Id.ToString("D"), document.UpdatedUtc.ToString("O"), link.ContentHash,
                    null, "The linked document object is missing or unavailable."));
            }
        }
        if (evidence.Count == 0) AddJson(items, "source_documents", "documents/index.json", false,
            "accounting_close_evidence", $"close:{closeInstanceId:D}", "close-evidence-v1", null,
            new { count = 0, note = "No source documents were linked to this close instance." });
    }

    private async Task<AuditPackageVerificationResult> VerifyArchiveAsync(AuditPackage package,
        Guid actorUserId, CancellationToken cancellationToken)
    {
        byte[] bytes;
        try
        {
            await using var source = await _storage.OpenReadAsync(package.StorageKey!, cancellationToken);
            bytes = await ReadBoundedAsync(source, _options.MaximumPackageBytes, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            bytes = [];
        }
        var result = AuditPackageArchiveVerifier.Verify(bytes, _options.MaximumDocumentBytes,
            package.Artifacts.ToArray(), package.PackageChecksum!, package.ManifestChecksum!);
        return new AuditPackageVerificationResult(Guid.NewGuid(), package.CompanyId, package.Id, actorUserId,
            result.IsValid, result.PackageChecksum, result.ManifestChecksum, result.CheckedItemCount,
            result.MissingItemCount, result.CorruptItemCount, result.ResultCode, result.SafeSummary, Now());
    }

    private async Task<string> BuildSnapshotVersionsAsync(Guid companyId, FiscalPeriod period,
        CancellationToken cancellationToken)
    {
        var close = await _db.AccountingCloseInstances.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.FiscalPeriodId == period.Id)
            .OrderByDescending(x => x.CompletedUtc).Select(x => new { x.Id, x.TemplateVersionId, x.TemplateVersionNumber, x.Version, x.Status }).FirstOrDefaultAsync(cancellationToken);
        var statements = await _db.FinancialReportSuiteSnapshots.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.FiscalPeriodId == period.Id)
            .OrderBy(x => x.ReportKind).ThenByDescending(x => x.CreatedUtc)
            .Select(x => new { x.Id, x.ReportKind, x.Checksum, x.CalculationVersion, x.MappingVersion, x.ReportDefinitionVersionNumber, x.ReportDefinitionHash })
            .ToListAsync(cancellationToken);
        var policies = await _db.AccountingPolicyPackSelections.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.EffectiveFrom <= DateOnly.FromDateTime(period.EndUtc))
            .OrderBy(x => x.EffectiveFrom).Select(x => new { x.Id, x.PackKey, x.PackVersion, x.DefinitionHash, x.EffectiveFrom, x.EffectiveTo }).ToListAsync(cancellationToken);
        var ledger = await _db.LedgerEntries.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.FiscalPeriodId == period.Id)
            .GroupBy(_ => 1).Select(x => new { count = x.Count(), latest = x.Max(y => y.UpdatedUtc) }).SingleOrDefaultAsync(cancellationToken);
        var model = new
        {
            fiscalPeriod = new { period.Id, period.Name, period.StartUtc, period.EndUtc, period.ClosedUtc, period.UpdatedUtc, period.IsReportingLocked },
            close,
            statements = statements.GroupBy(x => x.ReportKind).Select(x => x.First()).OrderBy(x => x.ReportKind),
            policies,
            ledger
        };
        return JsonSerializer.Serialize(model, JsonOptions).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private IQueryable<AuditPackage> PackageQuery(Guid companyId) => _db.AuditPackages.IgnoreQueryFilters().AsNoTracking()
        .Include(x => x.FiscalPeriod).Include(x => x.Artifacts).Include(x => x.GenerationAttempts)
        .Include(x => x.Approvals).Include(x => x.VerificationResults).Where(x => x.CompanyId == companyId);

    private async Task<AuditPackage> LoadAsync(Guid companyId, Guid packageId, bool tracked, CancellationToken cancellationToken)
    {
        IQueryable<AuditPackage> query = _db.AuditPackages.IgnoreQueryFilters().Include(x => x.FiscalPeriod)
            .Include(x => x.Artifacts).Include(x => x.GenerationAttempts).Include(x => x.Approvals)
            .Include(x => x.VerificationResults).Include(x => x.DownloadAuthorizations);
        if (!tracked) query = query.AsNoTracking();
        return await query.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == packageId, cancellationToken)
            ?? throw new KeyNotFoundException("The audit package was not found in the requested company.");
    }

    private static void AddJson(List<AuditPackageContentItem> items, string artifactType, string path,
        bool required, string sourceType, string sourceReference, string? sourceVersion,
        string? definitionVersion, object value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions).Replace("\r\n", "\n", StringComparison.Ordinal);
        items.Add(new(artifactType, path, AuditPackageArtifactStatuses.Included, required, sourceType,
            sourceReference, sourceVersion, definitionVersion, Encoding.UTF8.GetBytes(json)));
    }

    private static void AddMissing(List<AuditPackageContentItem> items, string artifactType, string path,
        bool required, string sourceType, string sourceReference, string detail) =>
        items.Add(new(artifactType, path, AuditPackageArtifactStatuses.Missing, required, sourceType,
            sourceReference, null, null, null, detail));

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, long maximumBytes, CancellationToken cancellationToken)
    {
        await using var owned = stream;
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await owned.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (output.Length + read > maximumBytes) throw Error("package_read_bound_exceeded", "An audit-package object exceeded the configured read bound.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return output.ToArray();
    }

    private async Task<string> RequireManageAsync(Guid companyId, Guid actorUserId, CancellationToken cancellationToken)
    {
        EnsureCurrentUser(actorUserId);
        var membership = await _memberships.ResolveAsync(companyId, cancellationToken) ?? throw new UnauthorizedAccessException();
        var role = membership.MembershipRole.ToStorageValue();
        if (!FinanceAccess.CanManageAccounting(role)) throw new UnauthorizedAccessException();
        return role;
    }

    private async Task RequireApproveAsync(Guid companyId, Guid actorUserId, CancellationToken cancellationToken)
    {
        EnsureCurrentUser(actorUserId);
        var membership = await _memberships.ResolveAsync(companyId, cancellationToken) ?? throw new UnauthorizedAccessException();
        if (!FinanceAccess.CanApproveInvoices(membership.MembershipRole.ToStorageValue())) throw new UnauthorizedAccessException();
    }

    private async Task RequireViewAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var membership = await _memberships.ResolveAsync(companyId, cancellationToken) ?? throw new UnauthorizedAccessException();
        if (!FinanceAccess.CanViewAccounting(membership.MembershipRole.ToStorageValue())) throw new UnauthorizedAccessException();
    }

    private void EnsureCurrentUser(Guid actorUserId)
    {
        if (actorUserId == Guid.Empty || _currentUser.UserId != actorUserId) throw new UnauthorizedAccessException();
    }

    private void EnsureDownloadable(AuditPackage package)
    {
        if (package.Status is not (AuditPackageStatuses.Final or AuditPackageStatuses.Incomplete) ||
            string.IsNullOrWhiteSpace(package.StorageKey) || package.RetainUntilUtc <= Now())
            throw Error("package_not_downloadable", "The audit package is not available for download.", true);
    }

    private static void EnsureVersion(AuditPackage package, long expectedVersion)
    {
        if (package.Version != expectedVersion) throw Error("audit_package_state_changed", "The audit package changed. Refresh and try again.", true);
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try { await _db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw Error("audit_package_state_changed", "The audit package changed. Refresh and try again.", true); }
        catch (DbUpdateException ex) when (ex.InnerException is not null)
        { throw Error("audit_package_conflict", "The audit-package scope or idempotency key conflicts with an existing request.", true); }
    }

    private Task AuditAsync(Guid companyId, Guid? actorUserId, string action, Guid packageId,
        string rationale, CancellationToken cancellationToken) => _audit.WriteAsync(new AuditEventWriteRequest(
            companyId, actorUserId.HasValue ? AuditActorTypes.User : AuditActorTypes.System, actorUserId,
            action, "audit_package", packageId.ToString("D"), AuditEventOutcomes.Succeeded, rationale,
            DataSources: ["fiscal_period", "ledger", "financial_reports", "tax_returns", "reconciliations", "approvals", "close_history", "policy_pack", "company_documents"], OccurredUtc: Now()), cancellationToken);

    private DateTime Now() => _time.GetUtcNow().UtcDateTime;
    private static string Hash(string value) => AuditPackageArchiveBuilder.Hash(Encoding.UTF8.GetBytes(value));
    private static string Required(string? value, string name, int maximum)
    {
        var normalized = value?.Trim();
        return !string.IsNullOrWhiteSpace(normalized) && normalized.Length <= maximum
            ? normalized : throw Error("invalid_" + name.ToLowerInvariant(), $"{name} is required and limited to {maximum} characters.");
    }
    private static string SafeFailure(Exception exception, string fallback) => exception is AuditPackageException ? exception.Message : fallback;
    private static string SafePath(string value)
    {
        var normalized = new string(value.Trim().Select(x => char.IsLetterOrDigit(x) || x is '.' or '-' or '_' ? x : '-').ToArray());
        return string.IsNullOrWhiteSpace(normalized) ? "artifact" : normalized.ToLowerInvariant();
    }
    private static string SanitizeFileName(string value) => SafePath(value).Trim('.');
    private static AuditPackageException Error(string code, string message, bool conflict = false) => new(code, message, conflict);

    private static AuditPackageDto Map(AuditPackage package) => new(package.Id, package.CompanyId,
        package.FiscalPeriodId, package.FiscalPeriod.Name, package.ScopeKey, package.ScopeVersion,
        package.ScopeHash, package.SnapshotVersionsJson, package.Status, package.IsFinal,
        package.ManifestChecksum, package.PackageChecksum, package.FileName, package.MediaType,
        package.ContentLength, package.RequestedByUserId, package.ApprovedByUserId,
        package.RequestedUtc, package.UpdatedUtc, package.RetainUntilUtc, package.FinalizedUtc,
        package.AttemptCount, package.MaxAttempts, package.CancellationRequested, package.FailureCode,
        package.SafeFailureSummary, package.Version,
        package.Artifacts.OrderBy(x => x.Sequence).Select(x => new AuditPackageArtifactDto(x.Id,
            x.Sequence, x.ArtifactType, x.Path, x.Status, x.IsRequired, x.SourceType,
            x.SourceReference, x.SourceVersion, x.DefinitionVersion, x.Checksum, x.ContentLength,
            x.SafeDetail)).ToArray(),
        package.GenerationAttempts.OrderBy(x => x.AttemptNumber).Select(x => new AuditPackageAttemptDto(
            x.Id, x.AttemptNumber, x.Outcome, x.FailureCode, x.SafeSummary, x.StartedUtc, x.CompletedUtc)).ToArray(),
        package.Approvals.OrderBy(x => x.DecidedUtc).Select(x => new AuditPackageApprovalDto(x.Id,
            x.DecidedByUserId, x.Decision, x.Reason, x.DecidedUtc)).ToArray(),
        package.VerificationResults.OrderByDescending(x => x.VerifiedUtc).Select(Map).ToArray());

    private static AuditPackageVerificationDto Map(AuditPackageVerificationResult result) => new(result.Id,
        result.VerifiedByUserId, result.IsValid, result.PackageChecksum, result.ManifestChecksum,
        result.CheckedItemCount, result.MissingItemCount, result.CorruptItemCount, result.ResultCode,
        result.SafeSummary, result.VerifiedUtc);
}
