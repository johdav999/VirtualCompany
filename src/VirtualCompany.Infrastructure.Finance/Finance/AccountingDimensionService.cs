using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingDimensionService : IAccountingDimensionService
{
    private readonly VirtualCompanyDbContext _db;
    private readonly IAuditEventWriter _audit;
    private readonly TimeProvider _time;
    private readonly AccountingDimensionTelemetry _telemetry;

    public AccountingDimensionService(VirtualCompanyDbContext db, IAuditEventWriter audit, TimeProvider time,
        AccountingDimensionTelemetry telemetry)
    {
        _db = db; _audit = audit; _time = time; _telemetry = telemetry;
    }

    public async Task<AccountingDimensionWorkspaceDto> GetWorkspaceAsync(Guid companyId, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        var types = await _db.AccountingDimensionTypes.AsNoTracking().Where(x => x.CompanyId == companyId)
            .Include(x => x.Members).OrderBy(x => x.Code).ToListAsync(cancellationToken);
        var members = types.SelectMany(x => x.Members).ToDictionary(x => x.Id);
        var typeDtos = types.Select(type => new AccountingDimensionTypeDto(type.Id, type.Code, type.Name, type.Description,
            type.AllowsHierarchy, type.Status, type.EffectiveFrom, type.EffectiveTo, type.Version,
            type.Members.OrderBy(x => BuildPath(x, members)).Select(x => MapMember(x, members)).ToArray())).ToArray();
        var policies = await _db.AccountingDimensionAccountPolicies.AsNoTracking().Where(x => x.CompanyId == companyId)
            .Include(x => x.FinanceAccount).Include(x => x.DimensionType).OrderBy(x => x.FinanceAccount.Code).ThenBy(x => x.DimensionType.Code)
            .Select(x => new AccountingDimensionAccountPolicyDto(x.Id, x.FinanceAccountId, x.FinanceAccount.Code,
                x.FinanceAccount.Name, x.DimensionTypeId, x.DimensionType.Code, x.Requirement, x.EffectiveFrom, x.EffectiveTo, x.Version))
            .ToArrayAsync(cancellationToken);
        var rules = await _db.AccountingDimensionCombinationRules.AsNoTracking().Where(x => x.CompanyId == companyId)
            .Include(x => x.LeftMember).ThenInclude(x => x.DimensionType).Include(x => x.RightMember).ThenInclude(x => x.DimensionType)
            .OrderBy(x => x.LeftMember.Code).ThenBy(x => x.RightMember.Code).ToListAsync(cancellationToken);
        var ruleDtos = rules.Select(x => new AccountingDimensionCombinationRuleDto(x.Id, x.LeftMemberId,
            $"{x.LeftMember.DimensionType.Name}: {x.LeftMember.Code} · {x.LeftMember.Name}", x.RightMemberId,
            $"{x.RightMember.DimensionType.Name}: {x.RightMember.Code} · {x.RightMember.Name}", x.IsAllowed,
            x.EffectiveFrom, x.EffectiveTo, x.Version)).ToArray();
        var mappings = await _db.AccountingDimensionExternalMappings.AsNoTracking().Where(x => x.CompanyId == companyId)
            .Include(x => x.DimensionMember).OrderBy(x => x.ProviderKey).ThenBy(x => x.ExternalDimensionType).ThenBy(x => x.ExternalValue)
            .Select(x => new AccountingDimensionExternalMappingDto(x.Id, x.ProviderKey, x.ExternalDimensionType,
                x.ExternalValue, x.DimensionTypeId, x.DimensionMemberId, x.DimensionMember.Code + " · " + x.DimensionMember.Name,
                x.EffectiveFrom, x.EffectiveTo, x.Version)).ToArrayAsync(cancellationToken);
        var conflicts = await _db.AccountingDimensionMappingConflicts.AsNoTracking().Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.Status).ThenByDescending(x => x.CreatedUtc)
            .Select(x => new AccountingDimensionMappingConflictDto(x.Id, x.ProviderKey, x.ExternalDimensionType,
                x.ExternalValue, x.ReasonCode, x.Explanation, x.Status, x.ResolvedDimensionMemberId, x.CreatedUtc, x.ResolvedUtc))
            .ToArrayAsync(cancellationToken);
        var templates = await LoadTemplateDtosAsync(companyId, cancellationToken);
        var today = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);
        return new(typeDtos, policies, ruleDtos, mappings, conflicts, templates,
            types.Count(x => Active(x.Status, x.EffectiveFrom, x.EffectiveTo, today)),
            members.Values.Count(x => Active(x.Status, x.EffectiveFrom, x.EffectiveTo, today)),
            policies.Count(x => x.Requirement == VirtualCompany.Domain.Entities.AccountingDimensionRequirementValues.Required &&
                x.EffectiveFrom <= today && (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= today)),
            conflicts.Count(x => x.Status == "open"));
    }

    public async Task<AccountingDimensionTypeDto> SaveTypeAsync(SaveAccountingDimensionTypeCommand command, CancellationToken cancellationToken)
    {
        ValidateActor(command.CompanyId, command.ActorUserId); var now = Now();
        AccountingDimensionType entity;
        if (command.DimensionTypeId.HasValue)
        {
            entity = await _db.AccountingDimensionTypes.SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.DimensionTypeId, cancellationToken)
                ?? throw NotFound("The dimension type could not be found.");
            EnsureVersion(entity.Version, command.ExpectedVersion); entity.Apply(command.Name, command.Description,
                command.AllowsHierarchy, command.Status, command.EffectiveFrom, command.EffectiveTo, now);
        }
        else
        {
            entity = new AccountingDimensionType(Guid.NewGuid(), command.CompanyId, command.Code, command.Name,
                command.Description, command.AllowsHierarchy, command.Status, command.EffectiveFrom,
                command.EffectiveTo, command.ActorUserId, now); _db.AccountingDimensionTypes.Add(entity);
        }
        await AuditAsync(command.CompanyId, command.ActorUserId, AuditEventActions.AccountingDimensionConfigured,
            AuditTargetTypes.AccountingDimension, entity.Id, "An accounting dimension type was configured.", command.CorrelationId, now, cancellationToken);
        await SaveAsync(cancellationToken); _telemetry.ConfigurationChanged("type");
        return (await GetWorkspaceAsync(command.CompanyId, cancellationToken)).DimensionTypes.Single(x => x.Id == entity.Id);
    }

    public async Task<AccountingDimensionMemberDto> SaveMemberAsync(SaveAccountingDimensionMemberCommand command, CancellationToken cancellationToken)
    {
        ValidateActor(command.CompanyId, command.ActorUserId); var now = Now();
        var type = await _db.AccountingDimensionTypes.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.DimensionTypeId, cancellationToken)
            ?? throw NotFound("The dimension type could not be found.");
        if (command.ParentMemberId.HasValue)
        {
            if (!type.AllowsHierarchy) throw Invalid(AccountingDimensionReasonCodes.HierarchyInvalid, "This dimension type does not allow a hierarchy.");
            var parent = await _db.AccountingDimensionMembers.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.ParentMemberId, cancellationToken);
            if (parent?.DimensionTypeId != type.Id) throw Invalid(AccountingDimensionReasonCodes.HierarchyInvalid, "The parent member belongs to another dimension type.");
            if (command.DimensionMemberId.HasValue && await IsDescendantAsync(command.CompanyId, command.ParentMemberId.Value, command.DimensionMemberId.Value, cancellationToken))
                throw Invalid(AccountingDimensionReasonCodes.HierarchyInvalid, "A dimension member cannot be moved below one of its descendants.");
        }
        AccountingDimensionMember entity;
        if (command.DimensionMemberId.HasValue)
        {
            entity = await _db.AccountingDimensionMembers.SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.DimensionMemberId, cancellationToken)
                ?? throw NotFound("The dimension member could not be found.");
            if (entity.DimensionTypeId != type.Id) throw NotFound("The dimension member could not be found.");
            EnsureVersion(entity.Version, command.ExpectedVersion); entity.Apply(command.ParentMemberId, command.Name,
                command.Status, command.EffectiveFrom, command.EffectiveTo, now);
        }
        else
        {
            entity = new AccountingDimensionMember(Guid.NewGuid(), command.CompanyId, type.Id, command.ParentMemberId,
                command.Code, command.Name, command.Status, command.EffectiveFrom, command.EffectiveTo, command.ActorUserId, now);
            _db.AccountingDimensionMembers.Add(entity);
        }
        await AuditAsync(command.CompanyId, command.ActorUserId, AuditEventActions.AccountingDimensionMemberConfigured,
            AuditTargetTypes.AccountingDimensionMember, entity.Id, "An accounting dimension member was configured.", command.CorrelationId, now, cancellationToken);
        await SaveAsync(cancellationToken); _telemetry.ConfigurationChanged("member");
        var workspace = await GetWorkspaceAsync(command.CompanyId, cancellationToken);
        return workspace.DimensionTypes.SelectMany(x => x.Members).Single(x => x.Id == entity.Id);
    }

    public async Task<AccountingDimensionAccountPolicyDto> SaveAccountPolicyAsync(SaveAccountingDimensionAccountPolicyCommand command, CancellationToken cancellationToken)
    {
        ValidateActor(command.CompanyId, command.ActorUserId); var now = Now();
        if (!await _db.FinanceAccounts.AsNoTracking().AnyAsync(x => x.CompanyId == command.CompanyId && x.Id == command.FinanceAccountId, cancellationToken) ||
            !await _db.AccountingDimensionTypes.AsNoTracking().AnyAsync(x => x.CompanyId == command.CompanyId && x.Id == command.DimensionTypeId, cancellationToken))
            throw NotFound("The account or dimension type could not be found.");
        AccountingDimensionAccountPolicy entity;
        if (command.PolicyId.HasValue)
        {
            entity = await _db.AccountingDimensionAccountPolicies.SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.PolicyId, cancellationToken)
                ?? throw NotFound("The account dimension policy could not be found.");
            if (entity.FinanceAccountId != command.FinanceAccountId || entity.DimensionTypeId != command.DimensionTypeId)
                throw Invalid(AccountingDimensionReasonCodes.Invalid, "Account and dimension identities cannot be changed on an existing policy.");
            EnsureVersion(entity.Version, command.ExpectedVersion); entity.Apply(command.Requirement, command.EffectiveFrom, command.EffectiveTo, now);
        }
        else
        {
            entity = new AccountingDimensionAccountPolicy(Guid.NewGuid(), command.CompanyId, command.FinanceAccountId,
                command.DimensionTypeId, command.Requirement, command.EffectiveFrom, command.EffectiveTo, command.ActorUserId, now);
            _db.AccountingDimensionAccountPolicies.Add(entity);
        }
        await AuditAsync(command.CompanyId, command.ActorUserId, AuditEventActions.AccountingDimensionPolicyConfigured,
            AuditTargetTypes.AccountingDimensionPolicy, entity.Id, "An account dimension requirement was configured.", command.CorrelationId, now, cancellationToken);
        await SaveAsync(cancellationToken); _telemetry.ConfigurationChanged("account_policy");
        return (await GetWorkspaceAsync(command.CompanyId, cancellationToken)).AccountPolicies.Single(x => x.Id == entity.Id);
    }

    public async Task<AccountingDimensionCombinationRuleDto> SaveCombinationRuleAsync(SaveAccountingDimensionCombinationRuleCommand command, CancellationToken cancellationToken)
    {
        ValidateActor(command.CompanyId, command.ActorUserId); var now = Now();
        var members = await _db.AccountingDimensionMembers.AsNoTracking().Where(x => x.CompanyId == command.CompanyId &&
            (x.Id == command.LeftMemberId || x.Id == command.RightMemberId)).ToArrayAsync(cancellationToken);
        if (members.Length != 2) throw NotFound("One or more dimension members could not be found.");
        if (members[0].DimensionTypeId == members[1].DimensionTypeId)
            throw Invalid(AccountingDimensionReasonCodes.CombinationInvalid, "Combination rules must connect two different dimension types.");
        AccountingDimensionCombinationRule entity;
        if (command.RuleId.HasValue)
        {
            entity = await _db.AccountingDimensionCombinationRules.SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.RuleId, cancellationToken)
                ?? throw NotFound("The combination rule could not be found.");
            if (!SamePair(entity.LeftMemberId, entity.RightMemberId, command.LeftMemberId, command.RightMemberId))
                throw Invalid(AccountingDimensionReasonCodes.CombinationInvalid, "Member identities cannot be changed on an existing combination rule.");
            EnsureVersion(entity.Version, command.ExpectedVersion); entity.Apply(command.IsAllowed, command.EffectiveFrom, command.EffectiveTo, now);
        }
        else
        {
            entity = new AccountingDimensionCombinationRule(Guid.NewGuid(), command.CompanyId, command.LeftMemberId,
                command.RightMemberId, command.IsAllowed, command.EffectiveFrom, command.EffectiveTo, command.ActorUserId, now);
            _db.AccountingDimensionCombinationRules.Add(entity);
        }
        await AuditAsync(command.CompanyId, command.ActorUserId, AuditEventActions.AccountingDimensionPolicyConfigured,
            AuditTargetTypes.AccountingDimensionPolicy, entity.Id, "A valid dimension combination was configured.", command.CorrelationId, now, cancellationToken);
        await SaveAsync(cancellationToken); _telemetry.ConfigurationChanged("combination");
        return (await GetWorkspaceAsync(command.CompanyId, cancellationToken)).CombinationRules.Single(x => x.Id == entity.Id);
    }

    public async Task<AccountingDimensionExternalMappingDto> SaveExternalMappingAsync(SaveAccountingDimensionExternalMappingCommand command, CancellationToken cancellationToken)
    {
        ValidateActor(command.CompanyId, command.ActorUserId); var now = Now();
        var member = await _db.AccountingDimensionMembers.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.DimensionMemberId, cancellationToken);
        if (member?.DimensionTypeId != command.DimensionTypeId) throw NotFound("The mapped dimension member could not be found.");
        AccountingDimensionExternalMapping entity;
        if (command.MappingId.HasValue)
        {
            entity = await _db.AccountingDimensionExternalMappings.SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.MappingId, cancellationToken)
                ?? throw NotFound("The external dimension mapping could not be found.");
            EnsureVersion(entity.Version, command.ExpectedVersion); entity.Apply(command.DimensionTypeId, command.DimensionMemberId,
                command.EffectiveFrom, command.EffectiveTo, now);
        }
        else
        {
            entity = new AccountingDimensionExternalMapping(Guid.NewGuid(), command.CompanyId, command.ProviderKey,
                command.ExternalDimensionType, command.ExternalValue, command.DimensionTypeId, command.DimensionMemberId,
                command.EffectiveFrom, command.EffectiveTo, command.ActorUserId, now); _db.AccountingDimensionExternalMappings.Add(entity);
        }
        var conflicts = await _db.AccountingDimensionMappingConflicts.Where(x => x.CompanyId == command.CompanyId && x.Status == "open" &&
            x.ProviderKey == NormalizeCode(command.ProviderKey) && x.ExternalDimensionType == NormalizeCode(command.ExternalDimensionType) &&
            x.ExternalValue == command.ExternalValue.Trim()).ToListAsync(cancellationToken);
        foreach (var conflict in conflicts) conflict.Resolve(command.DimensionMemberId, now);
        await AuditAsync(command.CompanyId, command.ActorUserId, AuditEventActions.AccountingDimensionMappingConfigured,
            AuditTargetTypes.AccountingDimensionMapping, entity.Id, "An external accounting dimension value was mapped.", command.CorrelationId, now, cancellationToken);
        await SaveAsync(cancellationToken); _telemetry.ConfigurationChanged("external_mapping");
        return (await GetWorkspaceAsync(command.CompanyId, cancellationToken)).ExternalMappings.Single(x => x.Id == entity.Id);
    }

    public async Task<AccountingAllocationTemplateDto> SaveAllocationTemplateVersionAsync(SaveAccountingAllocationTemplateVersionCommand command, CancellationToken cancellationToken)
    {
        ValidateActor(command.CompanyId, command.ActorUserId); var now = Now();
        ValidateTemplateLines(command.Lines);
        var memberIds = command.Lines.Select(x => x.DimensionMemberId).Distinct().ToArray();
        var members = await _db.AccountingDimensionMembers.AsNoTracking().Where(x => x.CompanyId == command.CompanyId && memberIds.Contains(x.Id)).ToArrayAsync(cancellationToken);
        if (members.Length != memberIds.Length) throw Invalid(AccountingDimensionReasonCodes.AllocationInvalid, "One or more allocation members could not be found.");
        AccountingAllocationTemplate template;
        if (command.TemplateId.HasValue)
        {
            template = await _db.AccountingAllocationTemplates.Include(x => x.Versions).SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.TemplateId, cancellationToken)
                ?? throw NotFound("The allocation template could not be found.");
            EnsureVersion(template.Version, command.ExpectedTemplateVersion); template.Apply(command.Name, command.Status, command.ApprovalThreshold, now);
        }
        else
        {
            template = new AccountingAllocationTemplate(Guid.NewGuid(), command.CompanyId, command.Code, command.Name,
                command.Status, command.ApprovalThreshold, command.ActorUserId, now); _db.AccountingAllocationTemplates.Add(template);
        }
        var versionNumber = template.Versions.Select(x => x.VersionNumber).DefaultIfEmpty().Max() + 1;
        var version = new AccountingAllocationTemplateVersion(Guid.NewGuid(), command.CompanyId, template.Id, versionNumber,
            command.EffectiveFrom, command.EffectiveTo, command.RoundingPrecision, command.ActorUserId, now);
        var sequence = 0;
        foreach (var line in command.Lines)
            version.Lines.Add(new AccountingAllocationTemplateLine(Guid.NewGuid(), command.CompanyId, version.Id, ++sequence,
                line.DimensionMemberId, line.AllocationKind, line.Value, line.Basis));
        template.Versions.Add(version);
        await AuditAsync(command.CompanyId, command.ActorUserId, AuditEventActions.AccountingAllocationTemplateVersioned,
            AuditTargetTypes.AccountingAllocationTemplate, template.Id, "A new immutable allocation template version was created.", command.CorrelationId, now, cancellationToken);
        await SaveAsync(cancellationToken); _telemetry.ConfigurationChanged("allocation_template");
        return (await LoadTemplateDtosAsync(command.CompanyId, cancellationToken)).Single(x => x.Id == template.Id);
    }

    public async Task<AccountingAllocationPreviewDto> PreviewAllocationAsync(PreviewAccountingAllocationQuery query, CancellationToken cancellationToken)
    {
        EnsureCompany(query.CompanyId); var material = await LoadAllocationMaterialAsync(query.CompanyId, query.TemplateId, query.EffectiveDate, cancellationToken);
        var preview = AccountingAllocationCalculator.Calculate(material.Template.Id, material.Version.Id, material.Version.VersionNumber,
            query.Amount, query.Currency, material.Version.RoundingPrecision, material.Template.ApprovalThreshold,
            material.Version.Lines.OrderBy(x => x.Sequence).Select(x => new AccountingAllocationCalculationLine(x.Sequence,
                x.DimensionMemberId, x.DimensionMember.Code + " · " + x.DimensionMember.Name, x.AllocationKind, x.Value)).ToArray());
        _telemetry.AllocationPreviewed(preview.IsValid);
        return preview.Dto;
    }

    public async Task<AccountingAllocationApplicationDto> ApplyAllocationAsync(ApplyAccountingAllocationCommand command, CancellationToken cancellationToken)
    {
        ValidateActor(command.CompanyId, command.ActorUserId);
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey) || command.IdempotencyKey.Trim().Length > 200)
            throw Invalid(AccountingDimensionReasonCodes.IdempotencyConflict, "A stable allocation request identity is required.");
        var preview = await PreviewAllocationAsync(new(command.CompanyId, command.TemplateId, command.Amount, command.Currency, command.EffectiveDate), cancellationToken);
        if (preview.Issues.Count > 0) throw Invalid(preview.Issues[0].ReasonCode, preview.Issues[0].Explanation);
        var payloadHash = Hash(JsonSerializer.Serialize(new { command.CompanyId, command.TemplateId, command.Amount,
            Currency = command.Currency.Trim().ToUpperInvariant(), command.EffectiveDate, SourceType = NormalizeCode(command.SourceType),
            SourceId = command.SourceId.Trim(), SourceVersion = command.SourceVersion.Trim(),
            Evidence = (command.Evidence ?? []).OrderBy(x => x.DocumentId).Select(x => new { x.DocumentId, x.ContentHash }) }));
        var replay = await _db.AccountingAllocationApplications.AsNoTracking().Where(x => x.CompanyId == command.CompanyId && x.IdempotencyKey == command.IdempotencyKey.Trim())
            .Include(x => x.Lines).ThenInclude(x => x.DimensionMember).SingleOrDefaultAsync(cancellationToken);
        if (replay is not null)
        {
            if (replay.PayloadHash != payloadHash) throw Invalid(AccountingDimensionReasonCodes.IdempotencyConflict,
                "This allocation request identity was already used with different content.", true);
            return MapApplication(replay, true);
        }
        if (preview.RequiresApproval)
        {
            var approval = command.ApprovalRequestId.HasValue
                ? await _db.ApprovalRequests.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.ApprovalRequestId, cancellationToken)
                : null;
            if (approval?.Status != ApprovalRequestStatus.Approved ||
                approval.TargetEntityType != ApprovalTargetEntityType.AccountingAllocation.ToStorageValue() || approval.TargetEntityId != command.TemplateId)
                throw Invalid(AccountingDimensionReasonCodes.AllocationApprovalRequired, "This material allocation requires a current approved request.");
        }
        var evidenceIds = (command.Evidence ?? []).Select(x => x.DocumentId).Distinct().ToArray();
        var documents = await _db.CompanyKnowledgeDocuments.AsNoTracking().Where(x => x.CompanyId == command.CompanyId && evidenceIds.Contains(x.Id)).ToArrayAsync(cancellationToken);
        if (documents.Length != evidenceIds.Length) throw Invalid(AccountingDimensionReasonCodes.AllocationInvalid, "One or more allocation evidence documents could not be found.");
        var now = Now(); var application = new AccountingAllocationApplication(Guid.NewGuid(), command.CompanyId,
            command.TemplateId, preview.TemplateVersionId, command.SourceType, command.SourceId, command.SourceVersion,
            command.IdempotencyKey, payloadHash, command.Amount, preview.AllocatedAmount, command.Currency,
            command.ActorUserId, command.ApprovalRequestId, now);
        foreach (var line in preview.Lines)
            application.Lines.Add(new AccountingAllocationApplicationLine(Guid.NewGuid(), command.CompanyId, application.Id,
                line.Sequence, line.DimensionMemberId, line.AllocationKind, line.DriverValue, line.RawAmount,
                line.RoundedAmount, line.RoundingResidual));
        foreach (var evidence in command.Evidence ?? [])
            application.EvidenceLinks.Add(new AccountingAllocationEvidenceLink(Guid.NewGuid(), command.CompanyId, application.Id,
                evidence.DocumentId, evidence.ContentHash, evidence.Title, now));
        _db.AccountingAllocationApplications.Add(application);
        await AuditAsync(command.CompanyId, command.ActorUserId, AuditEventActions.AccountingAllocationApplied,
            AuditTargetTypes.AccountingAllocation, application.Id, "A governed allocation was applied with retained source evidence.", command.CorrelationId, now, cancellationToken);
        await SaveAsync(cancellationToken); _telemetry.AllocationApplied(preview.RequiresApproval);
        return MapApplication(application, false);
    }

    public async Task<AccountingDimensionReportDto> GetReportAsync(GetAccountingDimensionReportQuery query, CancellationToken cancellationToken)
    {
        EnsureCompany(query.CompanyId); var skip = Math.Max(0, query.Skip); var take = Math.Clamp(query.Take, 1, 500);
        var member = await _db.AccountingDimensionMembers.AsNoTracking().Include(x => x.DimensionType)
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId && x.Id == query.DimensionMemberId, cancellationToken)
            ?? throw NotFound("The dimension member could not be found.");
        var rows = _db.LedgerEntryLineDimensions.AsNoTracking().Where(x => x.CompanyId == query.CompanyId && x.DimensionMemberId == query.DimensionMemberId)
            .Select(x => new { Assignment = x, Line = x.LedgerEntryLine, Entry = x.LedgerEntryLine.LedgerEntry,
                Account = x.LedgerEntryLine.FinanceAccount });
        if (query.From.HasValue) rows = rows.Where(x => x.Entry.PostingDate >= query.From.Value);
        if (query.To.HasValue) rows = rows.Where(x => x.Entry.PostingDate <= query.To.Value);
        var totalCount = await rows.CountAsync(cancellationToken); var totalDebit = await rows.SumAsync(x => x.Line.DebitAmount, cancellationToken);
        var totalCredit = await rows.SumAsync(x => x.Line.CreditAmount, cancellationToken);
        var page = await rows.OrderByDescending(x => x.Entry.PostingDate).ThenByDescending(x => x.Entry.EntryNumber).ThenBy(x => x.Line.Id)
            .Skip(skip).Take(take).Select(x => new AccountingDimensionReportLineDto(x.Entry.Id, x.Line.Id, x.Entry.EntryNumber,
                x.Entry.PostingDate!.Value, x.Account.Code, x.Account.Name, x.Line.DebitAmount, x.Line.CreditAmount,
                x.Line.Currency, x.Line.Description, x.Assignment.DimensionTypeCodeSnapshot, x.Assignment.MemberCodeSnapshot,
                x.Assignment.MemberNameSnapshot, x.Assignment.HierarchyPathSnapshot)).ToArrayAsync(cancellationToken);
        return new(member.DimensionTypeId, member.Id, member.DimensionType.Code, member.Code, member.Name,
            totalDebit, totalCredit, totalDebit - totalCredit, totalCount, skip, take, page);
    }

    private async Task<(AccountingAllocationTemplate Template, AccountingAllocationTemplateVersion Version)> LoadAllocationMaterialAsync(
        Guid companyId, Guid templateId, DateOnly date, CancellationToken cancellationToken)
    {
        var template = await _db.AccountingAllocationTemplates.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == templateId, cancellationToken)
            ?? throw NotFound("The allocation template could not be found.");
        if (template.Status != AccountingDimensionStatusValues.Active) throw Invalid(AccountingDimensionReasonCodes.AllocationInvalid, "The allocation template is archived.");
        var version = await _db.AccountingAllocationTemplateVersions.AsNoTracking().Where(x => x.CompanyId == companyId && x.TemplateId == templateId &&
            x.EffectiveFrom <= date && (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= date)).Include(x => x.Lines).ThenInclude(x => x.DimensionMember)
            .OrderByDescending(x => x.VersionNumber).FirstOrDefaultAsync(cancellationToken)
            ?? throw Invalid(AccountingDimensionReasonCodes.AllocationInvalid, "No allocation template version is effective on the selected date.");
        return (template, version);
    }

    private async Task<IReadOnlyList<AccountingAllocationTemplateDto>> LoadTemplateDtosAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var templates = await _db.AccountingAllocationTemplates.AsNoTracking().Where(x => x.CompanyId == companyId)
            .Include(x => x.Versions).ThenInclude(x => x.Lines).ThenInclude(x => x.DimensionMember).OrderBy(x => x.Code).ToListAsync(cancellationToken);
        return templates.Select(template =>
        {
            var current = template.Versions.OrderByDescending(x => x.VersionNumber).FirstOrDefault();
            return new AccountingAllocationTemplateDto(template.Id, template.Code, template.Name, template.Status,
                template.ApprovalThreshold, template.Version, current?.Id, current?.VersionNumber, current?.EffectiveFrom,
                current?.EffectiveTo, current?.RoundingPrecision ?? 2,
                current?.Lines.OrderBy(x => x.Sequence).Select(x => new AccountingAllocationTemplateLineDto(x.Id, x.Sequence,
                    x.DimensionMemberId, x.DimensionMember.Code + " · " + x.DimensionMember.Name, x.AllocationKind, x.Value, x.Basis)).ToArray() ?? []);
        }).ToArray();
    }

    private async Task<bool> IsDescendantAsync(Guid companyId, Guid candidateParentId, Guid memberId, CancellationToken cancellationToken)
    {
        var parents = await _db.AccountingDimensionMembers.AsNoTracking().Where(x => x.CompanyId == companyId)
            .Select(x => new { x.Id, x.ParentMemberId }).ToDictionaryAsync(x => x.Id, x => x.ParentMemberId, cancellationToken);
        var current = candidateParentId; var visited = new HashSet<Guid>();
        while (visited.Add(current) && parents.TryGetValue(current, out var parent) && parent.HasValue)
        { if (parent.Value == memberId) return true; current = parent.Value; }
        return candidateParentId == memberId;
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try { await _db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw Invalid(AccountingDimensionReasonCodes.VersionConflict, "The accounting dimension configuration changed. Reload and try again.", true); }
        catch (DbUpdateException exception) when (exception.ToString().Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) || exception.ToString().Contains("duplicate", StringComparison.OrdinalIgnoreCase))
        { throw Invalid(AccountingDimensionReasonCodes.Invalid, "This accounting dimension code or effective rule already exists.", true); }
    }

    private async Task AuditAsync(Guid companyId, Guid actorId, string action, string targetType, Guid targetId,
        string summary, string? correlationId, DateTime now, CancellationToken cancellationToken) =>
        await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.User, actorId, action, targetType,
            targetId.ToString("N"), AuditEventOutcomes.Succeeded, summary, ["accounting_dimensions"], null,
            correlationId, now), cancellationToken);

    private static AccountingDimensionMemberDto MapMember(AccountingDimensionMember member, IReadOnlyDictionary<Guid, AccountingDimensionMember> members) =>
        new(member.Id, member.DimensionTypeId, member.ParentMemberId, member.Code, member.Name, member.Status,
            member.EffectiveFrom, member.EffectiveTo, BuildPath(member, members), member.Version);
    private static string BuildPath(AccountingDimensionMember member, IReadOnlyDictionary<Guid, AccountingDimensionMember> members)
    { var path = new Stack<string>(); var current = member; var visited = new HashSet<Guid>(); while (visited.Add(current.Id))
        { path.Push(current.Code); if (!current.ParentMemberId.HasValue || !members.TryGetValue(current.ParentMemberId.Value, out current!)) break; }
        return string.Join(" / ", path); }
    private static bool Active(string status, DateOnly from, DateOnly? to, DateOnly date) => status == AccountingDimensionStatusValues.Active && from <= date && (!to.HasValue || to.Value >= date);
    private static bool SamePair(Guid a, Guid b, Guid c, Guid d) => a == c && b == d || a == d && b == c;
    private static void ValidateTemplateLines(IReadOnlyList<AccountingAllocationTemplateLineInput> lines)
    {
        if (lines is null || lines.Count < 2) throw Invalid(AccountingDimensionReasonCodes.AllocationInvalid, "An allocation template requires at least two target members.");
        if (lines.Select(x => x.DimensionMemberId).Distinct().Count() != lines.Count) throw Invalid(AccountingDimensionReasonCodes.AllocationInvalid, "An allocation member can appear only once in a template version.");
        var kinds = lines.Select(x => VirtualCompany.Domain.Entities.AccountingAllocationKindValues.Normalize(x.AllocationKind)).Distinct().ToArray();
        if (kinds.Length != 1) throw Invalid(AccountingDimensionReasonCodes.AllocationInvalid, "One template version cannot mix fixed and percentage lines.");
        if (kinds[0] == VirtualCompany.Domain.Entities.AccountingAllocationKindValues.Percentage && lines.Sum(x => x.Value) != 100m)
            throw Invalid(AccountingDimensionReasonCodes.AllocationInvalid, "Percentage allocation lines must total exactly 100%.");
        if (lines.Any(x => x.Value <= 0m)) throw Invalid(AccountingDimensionReasonCodes.AllocationInvalid, "Allocation values must be positive.");
    }
    private static AccountingAllocationApplicationDto MapApplication(AccountingAllocationApplication value, bool replay) =>
        new(value.Id, value.TemplateId, value.TemplateVersionId, value.SourceType, value.SourceId, value.SourceVersion,
            value.IdempotencyKey, value.PayloadHash, value.SourceAmount, value.AllocatedAmount, value.Currency,
            value.ApprovalRequestId, replay, value.CreatedUtc, value.Lines.OrderBy(x => x.Sequence).Select(x =>
                new AccountingAllocationPreviewLineDto(x.Sequence, x.DimensionMemberId,
                    x.DimensionMember is null ? x.DimensionMemberId.ToString("D") : x.DimensionMember.Code + " · " + x.DimensionMember.Name,
                    x.AllocationKind, x.DriverValue, x.RawAmount, x.RoundedAmount, x.RoundingResidual)).ToArray());
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string NormalizeCode(string value) => value.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
    private DateTime Now() => _time.GetUtcNow().UtcDateTime;
    private static void EnsureCompany(Guid companyId) { if (companyId == Guid.Empty) throw NotFound("The accounting dimension workspace could not be found."); }
    private static void ValidateActor(Guid companyId, Guid actorId) { EnsureCompany(companyId); if (actorId == Guid.Empty) throw NotFound("The accounting dimension workspace could not be found."); }
    private static void EnsureVersion(long current, long? expected) { if (!expected.HasValue || expected.Value != current) throw new AccountingDimensionException(AccountingDimensionReasonCodes.VersionConflict, $"This record is now version {current}. Reload it before continuing.", true, current); }
    private static AccountingDimensionException NotFound(string message) => new(AccountingDimensionReasonCodes.NotFound, message);
    private static AccountingDimensionException Invalid(string code, string message, bool conflict = false) => new(code, message, conflict);
}

public sealed record AccountingAllocationCalculationLine(int Sequence, Guid DimensionMemberId,
    string DimensionDisplay, string AllocationKind, decimal Value);

public sealed record AccountingAllocationCalculationResult(bool IsValid, AccountingAllocationPreviewDto Dto);

public static class AccountingAllocationCalculator
{
    public static AccountingAllocationCalculationResult Calculate(Guid templateId, Guid versionId, int versionNumber,
        decimal amount, string currency, int precision, decimal? approvalThreshold,
        IReadOnlyList<AccountingAllocationCalculationLine> lines)
    {
        var issues = new List<AccountingPostingIssue>(); var normalizedCurrency = currency?.Trim().ToUpperInvariant() ?? string.Empty;
        if (templateId == Guid.Empty || versionId == Guid.Empty || string.IsNullOrWhiteSpace(normalizedCurrency) || normalizedCurrency.Length != 3 || amount == 0m)
            issues.Add(new(AccountingDimensionReasonCodes.AllocationInvalid, "Template, non-zero amount, and three-letter currency are required."));
        if (precision is < 0 or > 6 || lines.Count < 2 || lines.Select(x => x.DimensionMemberId).Distinct().Count() != lines.Count)
            issues.Add(new(AccountingDimensionReasonCodes.AllocationInvalid, "The allocation template version is incomplete."));
        var kinds = lines.Select(x => VirtualCompany.Domain.Entities.AccountingAllocationKindValues.Normalize(x.AllocationKind)).Distinct().ToArray();
        if (kinds.Length != 1) issues.Add(new(AccountingDimensionReasonCodes.AllocationInvalid, "One allocation cannot mix fixed and percentage lines."));
        if (issues.Count > 0) return Result([], amount, normalizedCurrency, issues);
        var absolute = Math.Abs(amount); var sign = Math.Sign(amount); var factor = Pow10(precision);
        var raw = kinds[0] == VirtualCompany.Domain.Entities.AccountingAllocationKindValues.Percentage
            ? lines.Select(x => new Raw(x, absolute * x.Value / 100m)).ToArray()
            : lines.Select(x => new Raw(x, x.Value)).ToArray();
        if (kinds[0] == VirtualCompany.Domain.Entities.AccountingAllocationKindValues.Percentage && lines.Sum(x => x.Value) != 100m ||
            kinds[0] == VirtualCompany.Domain.Entities.AccountingAllocationKindValues.Fixed && raw.Sum(x => x.Amount) != absolute)
            issues.Add(new(AccountingDimensionReasonCodes.AllocationInvalid,
                kinds[0] == VirtualCompany.Domain.Entities.AccountingAllocationKindValues.Percentage
                    ? "Percentage allocation lines must total exactly 100%." : "Fixed allocation lines must total the source amount."));
        if (issues.Count > 0) return Result([], amount, normalizedCurrency, issues);
        var bases = raw.Select(x => new Rounded(x.Line, x.Amount, decimal.Truncate(x.Amount * factor) / factor)).ToArray();
        var units = decimal.ToInt32(decimal.Round((absolute - bases.Sum(x => x.Amount)) * factor, 0, MidpointRounding.ToEven));
        var ordered = bases.OrderByDescending(x => x.RawAmount - x.Amount).ThenBy(x => x.Line.Sequence).ToArray();
        for (var i = 0; i < units; i++) ordered[i % ordered.Length].Amount += 1m / factor;
        var resultLines = bases.OrderBy(x => x.Line.Sequence).Select(x => new AccountingAllocationPreviewLineDto(x.Line.Sequence,
            x.Line.DimensionMemberId, x.Line.DimensionDisplay, x.Line.AllocationKind, x.Line.Value, sign * x.RawAmount,
            sign * x.Amount, sign * (x.RawAmount - x.Amount))).ToArray();
        return Result(resultLines, amount, normalizedCurrency, issues);

        AccountingAllocationCalculationResult Result(IReadOnlyList<AccountingAllocationPreviewLineDto> resultLines,
            decimal source, string code, IReadOnlyList<AccountingPostingIssue> resultIssues)
        {
            var allocated = resultLines.Sum(x => x.RoundedAmount); var dto = new AccountingAllocationPreviewDto(templateId,
                versionId, versionNumber, source, allocated, source - allocated, code, precision,
                approvalThreshold.HasValue && Math.Abs(source) >= approvalThreshold.Value, resultLines, resultIssues);
            return new(resultIssues.Count == 0 && dto.Difference == 0m, dto);
        }
    }

    private static decimal Pow10(int precision) { var value = 1m; for (var i = 0; i < precision; i++) value *= 10m; return value; }
    private sealed record Raw(AccountingAllocationCalculationLine Line, decimal Amount);
    private sealed class Rounded(AccountingAllocationCalculationLine line, decimal rawAmount, decimal amount)
    { public AccountingAllocationCalculationLine Line { get; } = line; public decimal RawAmount { get; } = rawAmount; public decimal Amount { get; set; } = amount; }
}

public sealed class AccountingDimensionTelemetry : IDisposable
{
    private readonly Meter _meter = new("VirtualCompany.Finance.AccountingDimensions", "1.0.0");
    private readonly Counter<long> _configuration; private readonly Counter<long> _previews; private readonly Counter<long> _applications;
    public AccountingDimensionTelemetry()
    { _configuration = _meter.CreateCounter<long>("accounting_dimension_configuration_changes");
        _previews = _meter.CreateCounter<long>("accounting_allocation_previews"); _applications = _meter.CreateCounter<long>("accounting_allocation_applications"); }
    public void ConfigurationChanged(string kind) => _configuration.Add(1, new KeyValuePair<string, object?>("kind", kind));
    public void AllocationPreviewed(bool valid) => _previews.Add(1, new KeyValuePair<string, object?>("valid", valid));
    public void AllocationApplied(bool approved) => _applications.Add(1, new KeyValuePair<string, object?>("approval_required", approved));
    public void Dispose() => _meter.Dispose();
}
