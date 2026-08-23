using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingProviderSwitchStagingService : IAccountingProviderSwitchStagingService
{
    private const string ApprovalType = "accounting_provider_switch_mapping";
    private const string RequiredApprovalRole = "finance_approver";
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IApprovalRequestService _approvalRequestService;
    private readonly IAuditEventWriter _auditWriter;
    private readonly TimeProvider _timeProvider;

    public AccountingProviderSwitchStagingService(VirtualCompanyDbContext dbContext,
        IApprovalRequestService approvalRequestService, IAuditEventWriter auditWriter, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _approvalRequestService = approvalRequestService;
        _auditWriter = auditWriter;
        _timeProvider = timeProvider;
    }

    public async Task<AccountingProviderSwitchStagedRecordDto> StageAsync(
        StageAccountingProviderSwitchRecordCommand command, CancellationToken cancellationToken)
    {
        ValidateCommand(command.CompanyId, command.SwitchId, command.ActorUserId, command.CorrelationId);
        var providerSwitch = await GetSwitchAsync(command.CompanyId, command.SwitchId, cancellationToken);
        EnsureStagingAvailable(providerSwitch);
        var extractionExists = await _dbContext.AccountingProviderSwitchAssessments.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.CompanyId == command.CompanyId && x.SwitchId == command.SwitchId &&
                           x.Id == command.ExtractionBatchId &&
                           x.Status == AccountingProviderSwitchAssessmentStatuses.Completed, cancellationToken);
        if (!extractionExists)
            throw Error(AccountingProviderSwitchReasonCodes.InvalidStagedRecord,
                "The extraction batch is not a completed assessment for this accounting-system switch.");

        var dataset = AccountingProviderSwitchStagingDatasets.Normalize(command.Dataset);
        var sourceIdentity = Required(command.SourceIdentity, nameof(command.SourceIdentity), 256);
        var sourceVersion = Required(command.SourceVersion, nameof(command.SourceVersion), 128);
        var normalizedJson = CanonicalizeAndValidateEvidence(command.NormalizedDataJson, nameof(command.NormalizedDataJson));
        var evidenceJson = CanonicalizeAndValidateEvidence(command.EvidenceJson, nameof(command.EvidenceJson));
        var normalizedHash = Hash(normalizedJson);
        var sourceHash = NormalizeHash(command.SourceHash, nameof(command.SourceHash));
        var endpointKey = AccountingProviderSwitchStagedRecord.BuildEndpointKey(providerSwitch.Source);
        var stableIdentityHash = IdentityHash(command.CompanyId, command.SwitchId, endpointKey, dataset, sourceIdentity, sourceVersion);
        var sourceRecordKeyHash = IdentityHash(command.CompanyId, command.SwitchId, endpointKey, dataset, sourceIdentity);
        var now = UtcNow();

        var existing = await _dbContext.AccountingProviderSwitchStagedRecords.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.SwitchId == command.SwitchId &&
                                       x.StableIdentityHash == stableIdentityHash, cancellationToken);
        if (existing is not null)
        {
            var changed = existing.ReplaceNormalizedSnapshot(command.ExtractionBatchId, command.ProviderModifiedUtc,
                sourceHash, normalizedHash, normalizedJson, evidenceJson, command.FinancialAmount,
                command.Currency, now);
            if (changed)
                await MarkAffectedMappingsStaleAsync(command.CompanyId, command.SwitchId, existing.Id, now, cancellationToken);
            await WriteAuditAsync(existing, command.ActorUserId,
                changed ? AuditEventActions.AccountingProviderSwitchStagedRecordChanged : AuditEventActions.AccountingProviderSwitchStagedRecordReplayed,
                changed ? "The staged source record changed and its prior decisions were invalidated." :
                    "The same source version was staged again without creating a duplicate.",
                command.CorrelationId, cancellationToken);
            await SaveAsync(cancellationToken);
            return ToDto(existing);
        }

        var previous = await _dbContext.AccountingProviderSwitchStagedRecords.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.SwitchId == command.SwitchId &&
                                       x.SourceRecordKeyHash == sourceRecordKeyHash && x.IsCurrent, cancellationToken);
        if (previous is not null)
        {
            previous.MarkSuperseded(now);
            await MarkAffectedMappingsStaleAsync(command.CompanyId, command.SwitchId, previous.Id, now, cancellationToken);
        }

        AccountingProviderSwitchStagedRecord staged;
        try
        {
            var initialDisposition = previous is null
                ? command.InitialDisposition
                : AccountingProviderSwitchDispositions.AwaitingEvidence;
            staged = new AccountingProviderSwitchStagedRecord(Guid.NewGuid(), command.CompanyId, command.SwitchId,
                command.ExtractionBatchId, providerSwitch.Source, dataset, sourceIdentity, sourceVersion,
                command.ProviderModifiedUtc, sourceHash, normalizedHash, normalizedJson, evidenceJson,
                command.FinancialAmount, command.Currency, initialDisposition, now);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw Error(AccountingProviderSwitchReasonCodes.InvalidStagedRecord, exception.Message);
        }

        _dbContext.AccountingProviderSwitchStagedRecords.Add(staged);
        await WriteAuditAsync(staged, command.ActorUserId, AuditEventActions.AccountingProviderSwitchStagedRecordCreated,
            previous is null ? "A normalized non-authoritative source record was staged." :
                "A new source version was staged and the previous version was retained as superseded.",
            command.CorrelationId, cancellationToken);
        await SaveAsync(cancellationToken);
        return ToDto(staged);
    }

    public async Task<IReadOnlyList<AccountingProviderSwitchStagedRecordDto>> ListAsync(
        ListAccountingProviderSwitchStagedRecordsQuery query, CancellationToken cancellationToken)
    {
        ValidateCompanySwitch(query.CompanyId, query.SwitchId);
        await EnsureSwitchExistsAsync(query.CompanyId, query.SwitchId, cancellationToken);
        var records = _dbContext.AccountingProviderSwitchStagedRecords.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.SwitchId == query.SwitchId);
        if (!query.IncludeSuperseded) records = records.Where(x => x.IsCurrent);
        if (!string.IsNullOrWhiteSpace(query.Dataset))
        {
            var dataset = AccountingProviderSwitchStagingDatasets.Normalize(query.Dataset);
            records = records.Where(x => x.Dataset == dataset);
        }
        if (!string.IsNullOrWhiteSpace(query.Disposition))
        {
            var disposition = AccountingProviderSwitchDispositions.Normalize(query.Disposition);
            records = records.Where(x => x.Disposition == disposition);
        }
        return (await records.OrderBy(x => x.Dataset).ThenBy(x => x.SourceIdentity)
                .Take(Math.Clamp(query.Limit, 1, 500)).ToListAsync(cancellationToken))
            .Select(ToDto).ToArray();
    }

    public async Task<AccountingProviderSwitchMappingDecisionDto> PreviewMappingAsync(
        PreviewAccountingProviderSwitchMappingCommand command, CancellationToken cancellationToken)
    {
        ValidateCommand(command.CompanyId, command.SwitchId, command.ActorUserId, command.CorrelationId);
        var providerSwitch = await GetSwitchAsync(command.CompanyId, command.SwitchId, cancellationToken);
        EnsureStagingAvailable(providerSwitch);
        var mappingType = AccountingProviderSwitchMappingTypes.Normalize(command.MappingType);
        var sourceKey = Required(command.SourceKey, nameof(command.SourceKey), 256);
        var recordIds = command.AffectedStagedRecordIds?.Where(x => x != Guid.Empty).Distinct().ToArray() ?? [];
        if (recordIds.Length == 0)
            throw Error(AccountingProviderSwitchReasonCodes.InvalidStagedRecord,
                "At least one current staged record is required for a mapping decision.");
        var records = await _dbContext.AccountingProviderSwitchStagedRecords.IgnoreQueryFilters()
            .Where(x => x.CompanyId == command.CompanyId && x.SwitchId == command.SwitchId &&
                        x.IsCurrent && recordIds.Contains(x.Id)).ToListAsync(cancellationToken);
        if (records.Count != recordIds.Length)
            throw Error(AccountingProviderSwitchReasonCodes.StagedRecordNotFound,
                "One or more staged records were not found in this company or are no longer current.");

        var suggestion = await SuggestAsync(command.CompanyId, mappingType, sourceKey,
            command.ProposedTargetKey, command.SourceSemantic, cancellationToken);
        var bindingHash = MappingBindingHash(command.SwitchId, mappingType, sourceKey, suggestion.TargetKey,
            records, command.IsMaterial);
        var currentSet = await _dbContext.AccountingProviderSwitchMappingSets.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.SwitchId == command.SwitchId &&
                                       x.MappingType == mappingType && x.ScopeKey == sourceKey && x.IsCurrent,
                cancellationToken);
        if (currentSet is not null)
        {
            var currentDecision = await _dbContext.AccountingProviderSwitchMappingDecisions.IgnoreQueryFilters()
                .Include(x => x.AffectedRecords)
                .SingleAsync(x => x.CompanyId == command.CompanyId && x.SwitchId == command.SwitchId &&
                                  x.MappingSetId == currentSet.Id, cancellationToken);
            if (currentDecision.BindingHash == bindingHash)
                return await ToDtoAsync(currentDecision, cancellationToken);
            currentSet.Supersede(UtcNow());
            currentDecision.MarkStale(UtcNow());
        }

        var nextVersion = (await _dbContext.AccountingProviderSwitchMappingSets.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && x.SwitchId == command.SwitchId &&
                        x.MappingType == mappingType && x.ScopeKey == sourceKey)
            .MaxAsync(x => (int?)x.MappingVersion, cancellationToken) ?? 0) + 1;
        var now = UtcNow();
        var mappingSet = new AccountingProviderSwitchMappingSet(Guid.NewGuid(), command.CompanyId, command.SwitchId,
            mappingType, sourceKey, nextVersion, command.ActorUserId, now);
        var decision = new AccountingProviderSwitchMappingDecision(Guid.NewGuid(), command.CompanyId, command.SwitchId,
            mappingSet.Id, nextVersion, mappingType, sourceKey, suggestion.TargetKey, suggestion.Method,
            suggestion.Confidence, suggestion.EvidenceJson, command.IsMaterial, records.Count,
            records.Sum(x => x.FinancialAmount), bindingHash, command.ActorUserId, now);
        foreach (var record in records)
            decision.AffectedRecords.Add(new AccountingProviderSwitchMappingRecord(command.CompanyId,
                command.SwitchId, decision.Id, record.Id, record.SourceHash, record.NormalizedHash));
        if (!command.IsMaterial && suggestion.Confidence == 1m && suggestion.TargetKey is not null)
            decision.ApproveAutomatically(now);
        _dbContext.AccountingProviderSwitchMappingSets.Add(mappingSet);
        _dbContext.AccountingProviderSwitchMappingDecisions.Add(decision);
        await WriteMappingAuditAsync(decision, command.ActorUserId,
            AuditEventActions.AccountingProviderSwitchMappingSuggested,
            decision.Status == AccountingProviderSwitchMappingStatuses.Approved
                ? "An exact non-material mapping was accepted by deterministic policy."
                : "A deterministic mapping suggestion was recorded for review.",
            command.CorrelationId, cancellationToken);
        await SaveAsync(cancellationToken);
        return await ToDtoAsync(decision, cancellationToken);
    }

    public async Task<IReadOnlyList<AccountingProviderSwitchMappingDecisionDto>> ListMappingsAsync(
        ListAccountingProviderSwitchMappingsQuery query, CancellationToken cancellationToken)
    {
        ValidateCompanySwitch(query.CompanyId, query.SwitchId);
        await GetSwitchAsync(query.CompanyId, query.SwitchId, cancellationToken);
        IQueryable<AccountingProviderSwitchMappingDecision> decisions = MappingDecisions(
            query.CompanyId, query.SwitchId, tracking: false);
        if (!query.IncludeSuperseded)
            decisions = decisions.Where(x => x.MappingSet.IsCurrent);
        var current = await decisions
            .Include(x => x.MappingSet)
            .Include(x => x.AffectedRecords)
            .OrderByDescending(x => x.IsMaterial)
            .ThenBy(x => x.MappingType)
            .ThenBy(x => x.SourceKey)
            .Take(Math.Clamp(query.Limit, 1, 500))
            .ToListAsync(cancellationToken);
        var result = new List<AccountingProviderSwitchMappingDecisionDto>(current.Count);
        foreach (var decision in current)
            result.Add(await ToDtoAsync(decision, cancellationToken));
        return result;
    }

    public async Task<AccountingProviderSwitchMappingDecisionDto> RequestMappingApprovalAsync(
        RequestAccountingProviderSwitchMappingApprovalCommand command, CancellationToken cancellationToken)
    {
        ValidateCommand(command.CompanyId, command.SwitchId, command.ActorUserId, command.CorrelationId);
        var decision = await MappingDecisions(command.CompanyId, command.SwitchId, tracking: true)
            .Include(x => x.AffectedRecords)
            .SingleOrDefaultAsync(x => x.Id == command.MappingDecisionId, cancellationToken)
            ?? throw Error(AccountingProviderSwitchReasonCodes.MappingDecisionNotFound,
                "The mapping decision was not found for this company.");
        if (!await IsBindingCurrentAsync(decision, cancellationToken))
        {
            decision.MarkStale(UtcNow());
            await SaveAsync(cancellationToken);
            throw Conflict(AccountingProviderSwitchReasonCodes.MappingDecisionStale,
                "The affected source records changed. Create a new mapping preview before requesting approval.");
        }
        if (decision.Status == AccountingProviderSwitchMappingStatuses.Approved)
            return await ToDtoAsync(decision, cancellationToken);
        if (decision.ApprovalRequestId.HasValue)
            return await ToDtoAsync(decision, cancellationToken);
        if (decision.Version != command.ExpectedVersion)
            throw Conflict(AccountingProviderSwitchReasonCodes.ConcurrencyConflict,
                "The mapping decision changed while this request was being reviewed.");

        await SaveAsync(cancellationToken);
        var approval = await _approvalRequestService.CreateAsync(command.CompanyId,
            new CreateApprovalRequestCommand(
                ApprovalTargetEntityType.AccountingProviderSwitchMappingDecision.ToStorageValue(),
                decision.Id,
                "human",
                command.ActorUserId,
                ApprovalType,
                new Dictionary<string, JsonNode?>
                {
                    ["switchId"] = command.SwitchId.ToString("D"),
                    ["mappingDecisionId"] = decision.Id.ToString("D"),
                    ["mappingVersion"] = decision.MappingVersion,
                    ["mappingType"] = decision.MappingType,
                    ["sourceKey"] = decision.SourceKey,
                    ["targetKey"] = decision.TargetKey,
                    ["affectedRecordCount"] = decision.AffectedRecordCount,
                    ["affectedFinancialTotal"] = decision.AffectedFinancialTotal,
                    ["bindingHash"] = decision.BindingHash,
                    ["evidenceHash"] = Hash(decision.EvidenceJson)
                },
                RequiredRole: RequiredApprovalRole), cancellationToken);
        decision.RequestApproval(approval.Id, UtcNow());
        await WriteMappingAuditAsync(decision, command.ActorUserId,
            AuditEventActions.AccountingProviderSwitchMappingApprovalRequested,
            "The material or ambiguous mapping was submitted to the existing approval workflow with versioned evidence.",
            command.CorrelationId, cancellationToken);
        await SaveAsync(cancellationToken);
        return await ToDtoAsync(decision, cancellationToken);
    }

    public async Task<AccountingProviderSwitchStagedRecordDto> ResolveDispositionAsync(
        ResolveAccountingProviderSwitchDispositionCommand command, CancellationToken cancellationToken)
    {
        ValidateCommand(command.CompanyId, command.SwitchId, command.ActorUserId, command.CorrelationId);
        var providerSwitch = await GetSwitchAsync(command.CompanyId, command.SwitchId, cancellationToken);
        EnsureStagingAvailable(providerSwitch);
        var record = await _dbContext.AccountingProviderSwitchStagedRecords.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.SwitchId == command.SwitchId &&
                                       x.Id == command.StagedRecordId, cancellationToken)
            ?? throw Error(AccountingProviderSwitchReasonCodes.StagedRecordNotFound,
                "The staged record was not found for this company.");
        if (record.Version != command.ExpectedVersion)
            throw Conflict(AccountingProviderSwitchReasonCodes.ConcurrencyConflict,
                "The staged record changed while this request was being reviewed.");

        var disposition = AccountingProviderSwitchDispositions.Normalize(command.Disposition);
        AccountingProviderSwitchMappingDecision? decision = null;
        var validApproval = false;
        if (command.MappingDecisionId.HasValue)
        {
            decision = await MappingDecisions(command.CompanyId, command.SwitchId, tracking: false)
                .Include(x => x.AffectedRecords)
                .SingleOrDefaultAsync(x => x.Id == command.MappingDecisionId.Value, cancellationToken)
                ?? throw Error(AccountingProviderSwitchReasonCodes.MappingDecisionNotFound,
                    "The mapping decision was not found for this company.");
            if (!decision.AffectedRecords.Any(x => x.StagedRecordId == record.Id))
                throw Error(AccountingProviderSwitchReasonCodes.MappingApprovalInvalid,
                    "The mapping decision does not cover this staged record.");
            validApproval = await IsDecisionApprovedAndCurrentAsync(decision, cancellationToken);
            if (!validApproval)
                throw Conflict(AccountingProviderSwitchReasonCodes.MappingApprovalRequired,
                    "A current approved mapping or exception is required for this disposition.");
            EnsureDecisionKindMatches(disposition, decision.MappingType);
        }
        if (disposition == AccountingProviderSwitchDispositions.Duplicate)
        {
            if (!command.DuplicateOfStagedRecordId.HasValue || command.DuplicateOfStagedRecordId == record.Id ||
                !await _dbContext.AccountingProviderSwitchStagedRecords.IgnoreQueryFilters().AsNoTracking()
                    .AnyAsync(x => x.CompanyId == command.CompanyId && x.SwitchId == command.SwitchId &&
                                   x.Id == command.DuplicateOfStagedRecordId.Value && x.IsCurrent, cancellationToken))
                throw Error(AccountingProviderSwitchReasonCodes.InvalidStagedRecord,
                    "A duplicate disposition requires a different current staged record in this switch.");
        }
        EnsureExclusionAllowed(providerSwitch.MigrationStrategy, record.Dataset, disposition);

        try
        {
            record.ResolveDisposition(disposition, command.Reason, decision?.Id, decision?.MappingVersion,
                decision?.ApprovalRequestId, decision?.BindingHash, command.DuplicateOfStagedRecordId,
                validApproval, UtcNow());
        }
        catch (InvalidOperationException exception)
        {
            throw Conflict(AccountingProviderSwitchReasonCodes.InvalidStagedRecord, exception.Message);
        }
        var action = disposition switch
        {
            AccountingProviderSwitchDispositions.ExcludedWithApproval => AuditEventActions.AccountingProviderSwitchSourceExcluded,
            AccountingProviderSwitchDispositions.Transformed => AuditEventActions.AccountingProviderSwitchSourceTransformed,
            AccountingProviderSwitchDispositions.Duplicate => AuditEventActions.AccountingProviderSwitchDuplicateMatched,
            _ => AuditEventActions.AccountingProviderSwitchDispositionResolved
        };
        await WriteAuditAsync(record, command.ActorUserId, action,
            "The staged source record received a traceable migration disposition.", command.CorrelationId,
            cancellationToken);
        await SaveAsync(cancellationToken);
        return ToDto(record);
    }

    public async Task<AccountingProviderSwitchCompletenessDto> GetCompletenessAsync(
        GetAccountingProviderSwitchCompletenessQuery query, CancellationToken cancellationToken)
    {
        ValidateCompanySwitch(query.CompanyId, query.SwitchId);
        var providerSwitch = await GetSwitchAsync(query.CompanyId, query.SwitchId, cancellationToken);
        var records = await _dbContext.AccountingProviderSwitchStagedRecords.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.SwitchId == query.SwitchId && x.IsCurrent)
            .ToListAsync(cancellationToken);
        var decisions = await MappingDecisions(query.CompanyId, query.SwitchId, tracking: false)
            .Include(x => x.AffectedRecords).ToListAsync(cancellationToken);
        var approvalIds = decisions.Where(x => x.ApprovalRequestId.HasValue).Select(x => x.ApprovalRequestId!.Value)
            .Concat(records.Where(x => x.ApprovalRequestId.HasValue).Select(x => x.ApprovalRequestId!.Value))
            .Distinct().ToArray();
        var approvals = await _dbContext.ApprovalRequests.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && approvalIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Status, cancellationToken);
        var decisionById = decisions.ToDictionary(x => x.Id);
        var valid = new HashSet<Guid>();
        foreach (var record in records)
        {
            if (IsValidDisposition(providerSwitch.MigrationStrategy, record, decisionById, approvals))
                valid.Add(record.Id);
        }

        var hasCompletedAssessment = await _dbContext.AccountingProviderSwitchAssessments.IgnoreQueryFilters()
            .AsNoTracking().AnyAsync(x => x.CompanyId == query.CompanyId && x.SwitchId == query.SwitchId &&
                                       x.Status == AccountingProviderSwitchAssessmentStatuses.Completed,
                cancellationToken);
        var expected = await ExpectedDatasetCountsAsync(query.CompanyId, query.SwitchId, cancellationToken);
        var datasetKeys = expected.Keys.Concat(records.Select(x => x.Dataset)).Distinct(StringComparer.Ordinal).Order().ToArray();
        var datasetResults = datasetKeys.Select(dataset =>
        {
            var staged = records.Where(x => x.Dataset == dataset).ToArray();
            var expectedCount = expected.GetValueOrDefault(dataset);
            var validCount = staged.LongCount(x => valid.Contains(x.Id));
            var complete = staged.LongLength == expectedCount && validCount == expectedCount;
            var explanation = complete
                ? "Every assessed source record has one current valid disposition."
                : $"Expected {expectedCount}, staged {staged.LongLength}, and validly dispositioned {validCount}.";
            return new AccountingProviderSwitchDatasetCompletenessDto(dataset, expectedCount, staged.LongLength,
                validCount, complete, explanation);
        }).ToArray();
        var dispositionCounts = records.GroupBy(x => x.Disposition).OrderBy(x => x.Key)
            .Select(group => new AccountingProviderSwitchDispositionCountDto(group.Key, group.LongCount(),
                group.Sum(x => x.FinancialAmount))).ToArray();
        var expectedTotal = expected.Values.Sum();
        var isComplete = hasCompletedAssessment && datasetResults.All(x => x.IsComplete);
        return new AccountingProviderSwitchCompletenessDto(query.SwitchId, isComplete, expectedTotal,
            records.Count, valid.Count, records.Count - valid.Count, dispositionCounts, datasetResults,
            isComplete
                ? "Staging is complete and reconciles to the latest completed source assessment."
                : "Preparation cannot advance until staged counts reconcile and every source record has a current valid disposition.");
    }

    private async Task<MappingSuggestion> SuggestAsync(Guid companyId, string mappingType, string sourceKey,
        string? proposedTargetKey, string? sourceSemantic, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(proposedTargetKey))
            return new(Required(proposedTargetKey, nameof(proposedTargetKey), 256), "manual_proposal", .5m,
                JsonSerializer.Serialize(new { basis = "operator_proposal" }));

        var externalReference = await _dbContext.FinanceExternalReferences.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && (x.ExternalId == sourceKey || x.ExternalNumber == sourceKey))
            .Select(x => new { x.InternalRecordId, x.ProviderKey, x.EntityType }).FirstOrDefaultAsync(cancellationToken);
        if (externalReference is not null)
            return new(externalReference.InternalRecordId.ToString("D"), "approved_external_reference", 1m,
                JsonSerializer.Serialize(new { externalReference.ProviderKey, externalReference.EntityType,
                    externalReference.InternalRecordId }));

        if (mappingType == AccountingProviderSwitchMappingTypes.Account)
        {
            var account = await _dbContext.FinanceAccounts.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.Code == sourceKey)
                .Select(x => new { x.Id, x.Code, x.ControlAccountRole }).SingleOrDefaultAsync(cancellationToken);
            if (account is not null)
                return new(account.Code, "exact_identifier", 1m,
                    JsonSerializer.Serialize(new { account.Id, account.Code }));
            if (!string.IsNullOrWhiteSpace(sourceSemantic))
            {
                var normalizedSemantic = sourceSemantic.Trim().ToLowerInvariant();
                var roleMatches = await _dbContext.FinanceAccounts.IgnoreQueryFilters().AsNoTracking()
                    .Where(x => x.CompanyId == companyId && x.ControlAccountRole == normalizedSemantic)
                    .Select(x => x.Code).Take(2).ToArrayAsync(cancellationToken);
                if (roleMatches.Length == 1)
                    return new(roleMatches[0], "account_role", .95m,
                        JsonSerializer.Serialize(new { accountRole = normalizedSemantic }));
            }
        }

        if (mappingType == AccountingProviderSwitchMappingTypes.Currency &&
            sourceKey.Length == 3 && sourceKey.All(char.IsLetter))
            return new(sourceKey.ToUpperInvariant(), "known_currency_identifier", 1m,
                JsonSerializer.Serialize(new { normalizedCurrency = sourceKey.ToUpperInvariant() }));

        if (mappingType == AccountingProviderSwitchMappingTypes.TaxCode &&
            sourceSemantic?.Trim().ToLowerInvariant() is "standard" or "zero" or "exempt" or "reverse_charge")
            return new(sourceSemantic.Trim().ToLowerInvariant(), "known_tax_semantics", .9m,
                JsonSerializer.Serialize(new { taxSemantic = sourceSemantic.Trim().ToLowerInvariant() }));

        var prior = await _dbContext.AccountingProviderSwitchMappingDecisions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.MappingType == mappingType && x.SourceKey == sourceKey &&
                        x.Status == AccountingProviderSwitchMappingStatuses.Approved && x.TargetKey != null)
            .OrderByDescending(x => x.UpdatedUtc).Select(x => new { x.TargetKey, x.Id, x.MappingVersion })
            .FirstOrDefaultAsync(cancellationToken);
        if (prior is not null)
            return new(prior.TargetKey, "previously_approved_company_mapping", .95m,
                JsonSerializer.Serialize(new { prior.Id, prior.MappingVersion }));

        return new(null, "no_deterministic_match", 0m,
            JsonSerializer.Serialize(new { reason = "No exact or previously approved match was found." }));
    }

    private async Task<Dictionary<string, long>> ExpectedDatasetCountsAsync(Guid companyId, Guid switchId,
        CancellationToken cancellationToken)
    {
        var assessmentId = await _dbContext.AccountingProviderSwitchAssessments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.SwitchId == switchId &&
                        x.Status == AccountingProviderSwitchAssessmentStatuses.Completed)
            .OrderByDescending(x => x.CompletedUtc).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(cancellationToken);
        if (!assessmentId.HasValue) return new Dictionary<string, long>(StringComparer.Ordinal);
        var datasets = await _dbContext.AccountingProviderSwitchDatasets.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.SwitchId == switchId && x.AssessmentId == assessmentId.Value &&
                        x.EndpointRole == AccountingProviderSwitchEndpointRoles.Source &&
                        x.Availability == AccountingProviderSwitchDatasetAvailability.Available &&
                        x.CapabilityLevel != AccountingProviderSwitchCapabilityLevels.Unsupported)
            .Select(x => new { x.DatasetKey, x.RecordCount }).ToListAsync(cancellationToken);
        return datasets.Select(x => new { Dataset = ToStagingDataset(x.DatasetKey), x.RecordCount })
            .Where(x => x.Dataset is not null)
            .GroupBy(x => x.Dataset!, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Sum(item => item.RecordCount), StringComparer.Ordinal);
    }

    private static string? ToStagingDataset(string dataset) => dataset switch
    {
        AccountingProviderSwitchDatasetKeys.Accounts => AccountingProviderSwitchStagingDatasets.Accounts,
        AccountingProviderSwitchDatasetKeys.Tax => AccountingProviderSwitchStagingDatasets.TaxTreatments,
        AccountingProviderSwitchDatasetKeys.Customers or AccountingProviderSwitchDatasetKeys.Suppliers =>
            AccountingProviderSwitchStagingDatasets.Counterparties,
        AccountingProviderSwitchDatasetKeys.Invoices => AccountingProviderSwitchStagingDatasets.Invoices,
        AccountingProviderSwitchDatasetKeys.Credits => AccountingProviderSwitchStagingDatasets.Credits,
        AccountingProviderSwitchDatasetKeys.Payments => AccountingProviderSwitchStagingDatasets.Payments,
        AccountingProviderSwitchDatasetKeys.Allocations => AccountingProviderSwitchStagingDatasets.Allocations,
        AccountingProviderSwitchDatasetKeys.BankReconciliation => AccountingProviderSwitchStagingDatasets.BankState,
        AccountingProviderSwitchDatasetKeys.Currencies => AccountingProviderSwitchStagingDatasets.Currencies,
        AccountingProviderSwitchDatasetKeys.ExchangeRates => AccountingProviderSwitchStagingDatasets.ExchangeRates,
        AccountingProviderSwitchDatasetKeys.Dimensions => AccountingProviderSwitchStagingDatasets.Dimensions,
        AccountingProviderSwitchDatasetKeys.Journals => AccountingProviderSwitchStagingDatasets.Journals,
        AccountingProviderSwitchDatasetKeys.Attachments => AccountingProviderSwitchStagingDatasets.Documents,
        _ => null
    };

    private static bool IsValidDisposition(string strategy, AccountingProviderSwitchStagedRecord record,
        IReadOnlyDictionary<Guid, AccountingProviderSwitchMappingDecision> decisions,
        IReadOnlyDictionary<Guid, ApprovalRequestStatus> approvals)
    {
        if (AccountingProviderSwitchDispositions.BlocksProgress(record.Disposition)) return false;
        if (record.Disposition == AccountingProviderSwitchDispositions.Ready) return true;
        if (record.Disposition == AccountingProviderSwitchDispositions.Duplicate)
            return record.DuplicateOfStagedRecordId.HasValue;
        if (!record.MappingDecisionId.HasValue || !decisions.TryGetValue(record.MappingDecisionId.Value, out var decision))
            return false;
        if (!IsBindingCurrent(decision)) return false;
        var approved = decision.Status == AccountingProviderSwitchMappingStatuses.Approved ||
                       (decision.ApprovalRequestId.HasValue &&
                        approvals.GetValueOrDefault(decision.ApprovalRequestId.Value) == ApprovalRequestStatus.Approved);
        if (!approved) return false;
        if (record.Disposition == AccountingProviderSwitchDispositions.ExcludedWithApproval &&
            !IsExclusionAllowed(strategy, record.Dataset)) return false;
        return true;
    }

    private async Task<bool> IsDecisionApprovedAndCurrentAsync(AccountingProviderSwitchMappingDecision decision,
        CancellationToken cancellationToken)
    {
        if (!await IsBindingCurrentAsync(decision, cancellationToken)) return false;
        if (decision.Status == AccountingProviderSwitchMappingStatuses.Approved) return true;
        return decision.ApprovalRequestId.HasValue && await _dbContext.ApprovalRequests.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.CompanyId == decision.CompanyId && x.Id == decision.ApprovalRequestId.Value &&
                           x.Status == ApprovalRequestStatus.Approved, cancellationToken);
    }

    private async Task<bool> IsBindingCurrentAsync(AccountingProviderSwitchMappingDecision decision,
        CancellationToken cancellationToken)
    {
        if (decision.Status == AccountingProviderSwitchMappingStatuses.Stale) return false;
        if (decision.AffectedRecords.Count == 0)
            await _dbContext.Entry(decision).Collection(x => x.AffectedRecords).LoadAsync(cancellationToken);
        var ids = decision.AffectedRecords.Select(x => x.StagedRecordId).ToArray();
        var records = await _dbContext.AccountingProviderSwitchStagedRecords.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == decision.CompanyId && x.SwitchId == decision.SwitchId && ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        return decision.AffectedRecords.All(link => records.TryGetValue(link.StagedRecordId, out var record) &&
            record.IsCurrent && record.SourceHash == link.StagedSourceHash &&
            record.NormalizedHash == link.StagedNormalizedHash);
    }

    private static bool IsBindingCurrent(AccountingProviderSwitchMappingDecision decision) =>
        decision.Status != AccountingProviderSwitchMappingStatuses.Stale &&
        decision.AffectedRecords.Count > 0;

    private async Task MarkAffectedMappingsStaleAsync(Guid companyId, Guid switchId, Guid stagedRecordId,
        DateTime now, CancellationToken cancellationToken)
    {
        var decisions = await MappingDecisions(companyId, switchId, tracking: true)
            .Where(x => x.AffectedRecords.Any(link => link.StagedRecordId == stagedRecordId))
            .ToListAsync(cancellationToken);
        foreach (var decision in decisions.Where(x => x.Status != AccountingProviderSwitchMappingStatuses.Stale))
            decision.MarkStale(now);
    }

    private async Task<AccountingProviderSwitchMappingDecisionDto> ToDtoAsync(
        AccountingProviderSwitchMappingDecision decision, CancellationToken cancellationToken)
    {
        var bindingCurrent = await IsBindingCurrentAsync(decision, cancellationToken);
        var status = decision.Status;
        var approved = status == AccountingProviderSwitchMappingStatuses.Approved;
        if (decision.ApprovalRequestId.HasValue)
        {
            var approvalStatus = await _dbContext.ApprovalRequests.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == decision.CompanyId && x.Id == decision.ApprovalRequestId.Value)
                .Select(x => (ApprovalRequestStatus?)x.Status).SingleOrDefaultAsync(cancellationToken);
            status = approvalStatus switch
            {
                ApprovalRequestStatus.Approved when bindingCurrent => AccountingProviderSwitchMappingStatuses.Approved,
                ApprovalRequestStatus.Rejected => AccountingProviderSwitchMappingStatuses.Rejected,
                ApprovalRequestStatus.Cancelled or ApprovalRequestStatus.Expired => AccountingProviderSwitchMappingStatuses.Stale,
                _ => bindingCurrent ? AccountingProviderSwitchMappingStatuses.AwaitingApproval : AccountingProviderSwitchMappingStatuses.Stale
            };
            approved = approvalStatus == ApprovalRequestStatus.Approved && bindingCurrent;
        }
        if (!bindingCurrent) status = AccountingProviderSwitchMappingStatuses.Stale;
        return new AccountingProviderSwitchMappingDecisionDto(decision.Id, decision.MappingSetId,
            decision.MappingVersion, decision.MappingType, decision.SourceKey, decision.TargetKey,
            decision.SuggestionMethod, decision.Confidence, decision.EvidenceJson, decision.IsMaterial,
            decision.AffectedRecordCount, decision.AffectedFinancialTotal, status, decision.ApprovalRequestId,
            approved, decision.CreatedUtc, decision.UpdatedUtc, decision.Version);
    }

    private static AccountingProviderSwitchStagedRecordDto ToDto(AccountingProviderSwitchStagedRecord record) =>
        new(record.Id, record.CompanyId, record.SwitchId, record.ExtractionBatchId, record.SourceEndpointKey,
            record.Dataset, record.SourceIdentity, record.SourceVersion, record.ProviderModifiedUtc,
            record.SourceHash, record.NormalizedHash, record.NormalizedDataJson, record.EvidenceJson,
            record.FinancialAmount, record.Currency, record.Disposition, record.DispositionReason,
            record.MappingDecisionId, record.MappingVersion, record.ApprovalRequestId,
            record.DuplicateOfStagedRecordId, record.IsCurrent, record.CreatedUtc, record.UpdatedUtc, record.Version);

    private static void EnsureDecisionKindMatches(string disposition, string mappingType)
    {
        var valid = disposition switch
        {
            AccountingProviderSwitchDispositions.ExcludedWithApproval => mappingType == AccountingProviderSwitchMappingTypes.Exclusion,
            AccountingProviderSwitchDispositions.Transformed => mappingType == AccountingProviderSwitchMappingTypes.Transformation,
            AccountingProviderSwitchDispositions.OpeningBalanceRepresentation => mappingType == AccountingProviderSwitchMappingTypes.ManualException,
            AccountingProviderSwitchDispositions.Mapped => mappingType is not AccountingProviderSwitchMappingTypes.Exclusion and
                not AccountingProviderSwitchMappingTypes.Transformation and not AccountingProviderSwitchMappingTypes.ManualException,
            _ => true
        };
        if (!valid)
            throw Error(AccountingProviderSwitchReasonCodes.MappingApprovalInvalid,
                "The approved decision type does not match the requested disposition.");
    }

    private static void EnsureExclusionAllowed(string strategy, string dataset, string disposition)
    {
        if (disposition == AccountingProviderSwitchDispositions.ExcludedWithApproval && !IsExclusionAllowed(strategy, dataset))
            throw Conflict(AccountingProviderSwitchReasonCodes.StagingIncomplete,
                "This source dataset cannot be excluded under the selected migration strategy.");
    }

    private static bool IsExclusionAllowed(string strategy, string dataset) =>
        strategy != AccountingProviderSwitchStrategies.FullHistory || dataset is
            AccountingProviderSwitchStagingDatasets.Documents or AccountingProviderSwitchStagingDatasets.Dimensions;

    private async Task<AccountingProviderSwitch> GetSwitchAsync(Guid companyId, Guid switchId,
        CancellationToken cancellationToken) =>
        await _dbContext.AccountingProviderSwitches.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == switchId, cancellationToken)
        ?? throw Error(AccountingProviderSwitchReasonCodes.NotFound,
            "The accounting-system switch was not found for this company.");

    private async Task EnsureSwitchExistsAsync(Guid companyId, Guid switchId, CancellationToken cancellationToken) =>
        _ = await GetSwitchAsync(companyId, switchId, cancellationToken);

    private static void EnsureStagingAvailable(AccountingProviderSwitch providerSwitch)
    {
        if (providerSwitch.Status is not (AccountingProviderSwitchStatuses.ReadyForPlanning or
            AccountingProviderSwitchStatuses.PlanAwaitingApproval or AccountingProviderSwitchStatuses.PreparingTarget))
            throw Conflict(AccountingProviderSwitchReasonCodes.StagingUnavailable,
                "Normalized staging is available after assessment and before rehearsal begins.");
    }

    private IQueryable<AccountingProviderSwitchMappingDecision> MappingDecisions(Guid companyId, Guid switchId,
        bool tracking)
    {
        var query = _dbContext.AccountingProviderSwitchMappingDecisions.IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.SwitchId == switchId);
        return tracking ? query : query.AsNoTracking();
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try { await _dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        {
            throw Conflict(AccountingProviderSwitchReasonCodes.ConcurrencyConflict,
                "The staging or mapping decision changed while this request was being applied.");
        }
    }

    private Task WriteAuditAsync(AccountingProviderSwitchStagedRecord record, Guid actorUserId, string action,
        string summary, string correlationId, CancellationToken cancellationToken) =>
        _auditWriter.WriteAsync(new AuditEventWriteRequest(record.CompanyId, AuditActorTypes.User, actorUserId,
            action, AuditTargetTypes.AccountingProviderSwitchStagedRecord, record.Id.ToString("D"),
            AuditEventOutcomes.Succeeded, summary,
            ["accounting_provider_switch", "normalized_staging", "source_evidence"],
            new Dictionary<string, string?>
            {
                ["switchId"] = record.SwitchId.ToString("D"),
                ["dataset"] = record.Dataset,
                ["sourceIdentity"] = record.SourceIdentity,
                ["sourceVersion"] = record.SourceVersion,
                ["sourceHash"] = record.SourceHash,
                ["normalizedHash"] = record.NormalizedHash,
                ["disposition"] = record.Disposition,
                ["mappingDecisionId"] = record.MappingDecisionId?.ToString("D"),
                ["approvalRequestId"] = record.ApprovalRequestId?.ToString("D")
            }, correlationId, UtcNow()), cancellationToken);

    private Task WriteMappingAuditAsync(AccountingProviderSwitchMappingDecision decision, Guid actorUserId,
        string action, string summary, string correlationId, CancellationToken cancellationToken) =>
        _auditWriter.WriteAsync(new AuditEventWriteRequest(decision.CompanyId, AuditActorTypes.User, actorUserId,
            action, AuditTargetTypes.AccountingProviderSwitchMappingDecision, decision.Id.ToString("D"),
            AuditEventOutcomes.Succeeded, summary,
            ["accounting_provider_switch", "mapping_decision", "approval"],
            new Dictionary<string, string?>
            {
                ["switchId"] = decision.SwitchId.ToString("D"),
                ["mappingType"] = decision.MappingType,
                ["mappingVersion"] = decision.MappingVersion.ToString(),
                ["sourceKey"] = decision.SourceKey,
                ["targetKey"] = decision.TargetKey,
                ["confidence"] = decision.Confidence.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
                ["bindingHash"] = decision.BindingHash,
                ["affectedRecordCount"] = decision.AffectedRecordCount.ToString(),
                ["affectedFinancialTotal"] = decision.AffectedFinancialTotal.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["approvalRequestId"] = decision.ApprovalRequestId?.ToString("D")
            }, correlationId, UtcNow()), cancellationToken);

    private static string CanonicalizeAndValidateEvidence(string json, string name)
    {
        if (string.IsNullOrWhiteSpace(json)) throw Error(AccountingProviderSwitchReasonCodes.InvalidStagedRecord,
            $"{name} is required.");
        if (Encoding.UTF8.GetByteCount(json) > 16000)
            throw Error(AccountingProviderSwitchReasonCodes.InvalidStagedRecord,
                $"{name} must be 16,000 bytes or fewer.");
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            EnsureNoSensitiveKeys(document.RootElement);
            return Canonicalize(document.RootElement).ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        }
        catch (JsonException exception)
        {
            throw Error(AccountingProviderSwitchReasonCodes.InvalidStagedRecord,
                $"{name} must contain valid bounded JSON: {exception.Message}");
        }
    }

    private static JsonNode Canonicalize(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => new JsonObject(element.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal)
            .Select(property => KeyValuePair.Create<string, JsonNode?>(property.Name, Canonicalize(property.Value)))),
        JsonValueKind.Array => new JsonArray(element.EnumerateArray().Select(Canonicalize).ToArray()),
        _ => JsonNode.Parse(element.GetRawText())!
    };

    private static void EnsureNoSensitiveKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var key = property.Name.Replace("_", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
                if (key.Contains("accesstoken") || key.Contains("refreshtoken") || key.Contains("authorization") ||
                    key.Contains("clientsecret") || key == "password" || key == "credential")
                    throw Error(AccountingProviderSwitchReasonCodes.InvalidStagedRecord,
                        "Staging evidence cannot contain credentials or provider authorization secrets.");
                EnsureNoSensitiveKeys(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) EnsureNoSensitiveKeys(item);
        }
    }

    private static string MappingBindingHash(Guid switchId, string mappingType, string sourceKey,
        string? targetKey, IEnumerable<AccountingProviderSwitchStagedRecord> records, bool isMaterial) =>
        Hash(string.Join("|", new[] { switchId.ToString("N"), mappingType, sourceKey, targetKey ?? string.Empty,
            isMaterial.ToString() }.Concat(records.OrderBy(x => x.Id).Select(x =>
            $"{x.Id:N}:{x.SourceVersion}:{x.SourceHash}:{x.NormalizedHash}:{Hash(x.EvidenceJson)}:{x.FinancialAmount}"))));

    private static string IdentityHash(params object[] values) => Hash(string.Join("|", values.Select(x =>
        x.ToString()?.Trim().ToLowerInvariant())));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
        .ToLowerInvariant();
    private static string NormalizeHash(string value, string name)
    {
        var normalized = Required(value, name, 64).ToLowerInvariant();
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
            throw Error(AccountingProviderSwitchReasonCodes.InvalidStagedRecord,
                $"{name} must be a SHA-256 hexadecimal value.");
        return normalized;
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
    private static string Required(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw Error(AccountingProviderSwitchReasonCodes.InvalidStagedRecord,
            $"{name} is required.");
        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : throw Error(
            AccountingProviderSwitchReasonCodes.InvalidStagedRecord, $"{name} must be {maxLength} characters or fewer.");
    }
    private static void ValidateCompanySwitch(Guid companyId, Guid switchId)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        if (switchId == Guid.Empty) throw new ArgumentException("SwitchId is required.", nameof(switchId));
    }
    private static void ValidateCommand(Guid companyId, Guid switchId, Guid actorUserId, string correlationId)
    {
        ValidateCompanySwitch(companyId, switchId);
        if (actorUserId == Guid.Empty) throw new ArgumentException("ActorUserId is required.", nameof(actorUserId));
        if (string.IsNullOrWhiteSpace(correlationId)) throw new ArgumentException("CorrelationId is required.", nameof(correlationId));
    }
    private static AccountingAuthorityException Error(string code, string message) => new(code, message);
    private static AccountingAuthorityException Conflict(string code, string message) => new(code, message, true);
    private sealed record MappingSuggestion(string? TargetKey, string Method, decimal Confidence, string EvidenceJson);
}
