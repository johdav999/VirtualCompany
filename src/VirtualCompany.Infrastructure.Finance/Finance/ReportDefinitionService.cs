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

public sealed class ReportDefinitionService : IReportDefinitionService
{
    private static readonly IReadOnlyList<ReportSystemTemplateDto> Templates =
    [
        new("cash-flow-management-v1", "Cash flow – management", FinancialReportKinds.CashFlow,
            "Direct or indirect cash-flow layout with governed operating, investing, and financing sections.", false),
        new("equity-changes-management-v1", "Statement of changes in equity", FinancialReportKinds.EquityChanges,
            "Opening equity, governed movements, and closing equity with retained version evidence.", false)
    ];

    private readonly VirtualCompanyDbContext _db;
    private readonly ICompanyMembershipContextResolver _membership;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IFinancialReportSuiteService _reports;
    private readonly IAuditEventWriter _audit;
    private readonly ReportDefinitionTelemetry _telemetry;
    private readonly TimeProvider _time;

    public ReportDefinitionService(VirtualCompanyDbContext db, ICompanyMembershipContextResolver membership,
        ICurrentUserAccessor currentUser, IFinancialReportSuiteService reports, IAuditEventWriter audit,
        ReportDefinitionTelemetry telemetry, TimeProvider time)
    {
        _db = db; _membership = membership; _currentUser = currentUser; _reports = reports;
        _audit = audit; _telemetry = telemetry; _time = time;
    }

    public async Task<IReadOnlyList<ReportSystemTemplateDto>> ListSystemTemplatesAsync(Guid companyId, CancellationToken cancellationToken)
    {
        await RequireViewAsync(companyId, cancellationToken);
        return Templates;
    }

    public async Task<IReadOnlyList<ReportDefinitionSummaryDto>> ListAsync(Guid companyId, CancellationToken cancellationToken)
    {
        await RequireViewAsync(companyId, cancellationToken);
        var rows = await _db.ReportDefinitions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Select(x => new { Definition = x, Latest = x.Versions.OrderByDescending(v => v.VersionNumber).First() })
            .OrderBy(x => x.Definition.Name).ToListAsync(cancellationToken);
        return rows.Select(x => new ReportDefinitionSummaryDto(x.Definition.Id, x.Definition.Code, x.Definition.Name,
            x.Definition.ReportKind, x.Definition.SourceTemplateKey, x.Latest.VersionNumber, x.Latest.Status,
            x.Latest.Id, x.Latest.EffectiveFrom, x.Latest.EffectiveTo, x.Latest.Revision)).ToArray();
    }

    public async Task<ReportDefinitionVersionDto> GetAsync(Guid companyId, Guid versionId, CancellationToken cancellationToken)
    {
        await RequireViewAsync(companyId, cancellationToken);
        return Map(await LoadAsync(companyId, versionId, false, cancellationToken), await CanApproveAsync(companyId, cancellationToken));
    }

    public async Task<ReportDefinitionVersionDto> CopySystemTemplateAsync(CopyReportSystemTemplateCommand command, CancellationToken cancellationToken)
    {
        await RequireManageAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        var replay = await ReplayAsync(command.CompanyId, command.IdempotencyKey, "copy_template", null, cancellationToken);
        if (replay is not null) return replay;
        var template = Templates.SingleOrDefault(x => x.Key == command.TemplateKey)
            ?? throw new ReportDefinitionException("report_definition_template_not_found", "The system report template was not found.");
        var code = NormalizeCode(command.Code);
        if (await _db.ReportDefinitions.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == command.CompanyId && x.Code == code, cancellationToken))
            throw new ReportDefinitionException("report_definition_code_conflict", "A report definition with this code already exists.", true);
        var now = _time.GetUtcNow().UtcDateTime;
        var definition = new ReportDefinition(Guid.NewGuid(), command.CompanyId, code, command.Name,
            template.ReportKind, template.Key, command.ActorUserId, now);
        var version = new ReportDefinitionVersion(Guid.NewGuid(), command.CompanyId, definition.Id, 1,
            command.Name, template.ReportKind, command.ActorUserId, now);
        _db.ReportDefinitions.Add(definition); _db.ReportDefinitionVersions.Add(version);
        await AddTemplateLayoutAsync(command.CompanyId, version, template.ReportKind, cancellationToken);
        AddReceipt(command.CompanyId, command.IdempotencyKey, "copy_template", version.Id, command.ActorUserId, now);
        await AuditAsync(command.CompanyId, command.ActorUserId, "accounting.report_definition.copied", version.Id,
            "Copied an immutable system template into a company-owned draft.", template.ReportKind, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        _telemetry.Action("copy_template", "succeeded", template.ReportKind);
        return Map(await LoadAsync(command.CompanyId, version.Id, false, cancellationToken), await CanApproveAsync(command.CompanyId, cancellationToken));
    }

    public async Task<ReportDefinitionVersionDto> CreateVersionAsync(CreateReportDefinitionVersionCommand command, CancellationToken cancellationToken)
    {
        await RequireManageAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        var replay = await ReplayAsync(command.CompanyId, command.IdempotencyKey, "create_version", null, cancellationToken);
        if (replay is not null) return replay;
        var source = await LoadAsync(command.CompanyId, command.SourceVersionId, true, cancellationToken);
        if (source.DefinitionId != command.DefinitionId) throw new ReportDefinitionException("report_definition_source_mismatch", "The source version belongs to another definition.");
        var next = await _db.ReportDefinitionVersions.IgnoreQueryFilters().Where(x => x.CompanyId == command.CompanyId && x.DefinitionId == command.DefinitionId)
            .MaxAsync(x => x.VersionNumber, cancellationToken) + 1;
        var now = _time.GetUtcNow().UtcDateTime;
        var version = new ReportDefinitionVersion(Guid.NewGuid(), command.CompanyId, source.DefinitionId, next,
            source.Name, source.ReportKind, command.ActorUserId, now);
        _db.ReportDefinitionVersions.Add(version);
        CloneLayout(command.CompanyId, source, version);
        AddReceipt(command.CompanyId, command.IdempotencyKey, "create_version", version.Id, command.ActorUserId, now);
        await AuditAsync(command.CompanyId, command.ActorUserId, "accounting.report_definition.version_created", version.Id,
            "Created a prospective draft from an existing immutable definition version.", version.ReportKind, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        _telemetry.Action("create_version", "succeeded", version.ReportKind);
        return Map(await LoadAsync(command.CompanyId, version.Id, false, cancellationToken), await CanApproveAsync(command.CompanyId, cancellationToken));
    }

    public async Task<ReportDefinitionVersionDto> UpdateAsync(UpdateReportDefinitionVersionCommand command, CancellationToken cancellationToken)
    {
        await RequireManageAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        var replay = await ReplayAsync(command.CompanyId, command.IdempotencyKey, "update", command.VersionId, cancellationToken);
        if (replay is not null) return replay;
        var version = await LoadAsync(command.CompanyId, command.VersionId, true, cancellationToken);
        var now = _time.GetUtcNow().UtcDateTime;
        try { version.UpdateDraft(command.Name, command.ExpectedRevision, now); }
        catch (ReportDefinitionConcurrencyException ex) { throw Conflict(ex); }
        _db.ReportDefinitionAccountGroupMembers.RemoveRange(version.AccountGroups.SelectMany(x => x.Members));
        _db.ReportDefinitionAccountGroups.RemoveRange(version.AccountGroups);
        _db.ReportDefinitionLines.RemoveRange(version.Sections.SelectMany(x => x.Lines));
        _db.ReportDefinitionSections.RemoveRange(version.Sections);
        if (version.Comparison is not null) _db.ReportDefinitionComparisons.Remove(version.Comparison);
        AddLayout(command.CompanyId, version, command.Sections, command.Comparison);
        AddReceipt(command.CompanyId, command.IdempotencyKey, "update", version.Id, command.ActorUserId, now);
        await AuditAsync(command.CompanyId, command.ActorUserId, "accounting.report_definition.updated", version.Id,
            "Updated a company-owned draft definition without changing historical versions.", version.ReportKind, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        _telemetry.Action("update", "succeeded", version.ReportKind);
        return Map(await LoadAsync(command.CompanyId, version.Id, false, cancellationToken), await CanApproveAsync(command.CompanyId, cancellationToken));
    }

    public async Task<ReportDefinitionVersionDto> ValidateAsync(ValidateReportDefinitionCommand command, CancellationToken cancellationToken)
    {
        await RequireManageAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        var replay = await ReplayAsync(command.CompanyId, command.IdempotencyKey, "validate", command.VersionId, cancellationToken);
        if (replay is not null) return replay;
        var version = await LoadAsync(command.CompanyId, command.VersionId, true, cancellationToken);
        if (version.Revision != command.ExpectedRevision) throw Conflict(new ReportDefinitionConcurrencyException(version.Revision));
        if (version.Status != ReportDefinitionVersionStatuses.Draft) throw new ReportDefinitionException("report_definition_not_draft", "Only a draft definition can be validated.", true);
        var issues = await ValidateCoreAsync(command.CompanyId, version, cancellationToken);
        var hash = DefinitionHash(version);
        var now = _time.GetUtcNow().UtcDateTime;
        var result = new ReportDefinitionValidationResult(Guid.NewGuid(), command.CompanyId, version.Id,
            issues.Count == 0, hash, command.ActorUserId, now);
        _db.ReportDefinitionValidationResults.Add(result);
        foreach (var issue in issues)
            _db.ReportDefinitionValidationIssues.Add(new(Guid.NewGuid(), command.CompanyId, result.Id, issue.Code,
                issue.Severity, issue.Explanation, issue.LineId, issue.AccountId));
        try { version.MarkValidated(hash, command.ExpectedRevision, now); }
        catch (ReportDefinitionConcurrencyException ex) { throw Conflict(ex); }
        AddReceipt(command.CompanyId, command.IdempotencyKey, "validate", version.Id, command.ActorUserId, now);
        await AuditAsync(command.CompanyId, command.ActorUserId, "accounting.report_definition.validated", version.Id,
            issues.Count == 0 ? "Validated a report definition successfully." : $"Validation found {issues.Count} blocking issue(s).",
            version.ReportKind, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        _telemetry.Action("validate", issues.Count == 0 ? "succeeded" : "blocked", version.ReportKind, issues.Count);
        return Map(await LoadAsync(command.CompanyId, version.Id, false, cancellationToken), await CanApproveAsync(command.CompanyId, cancellationToken));
    }

    public async Task<CompleteFinancialReportDto> PreviewAsync(PreviewReportDefinitionQuery query, CancellationToken cancellationToken)
    {
        await RequireViewAsync(query.CompanyId, cancellationToken);
        var version = await LoadAsync(query.CompanyId, query.VersionId, false, cancellationToken);
        var result = await _reports.GetAsync(new GetFinancialReportSuiteQuery(query.CompanyId, query.FiscalPeriodId,
            version.ReportKind, ComparisonFiscalPeriodId: query.ComparisonFiscalPeriodId, Page: query.Page,
            PageSize: query.PageSize, DefinitionVersionId: version.Id, PreviewDefinition: true), cancellationToken);
        _telemetry.Action("preview", "succeeded", version.ReportKind);
        return result;
    }

    public Task<ReportDefinitionVersionDto> SubmitAsync(SubmitReportDefinitionCommand command, CancellationToken cancellationToken) =>
        TransitionAsync(command.CompanyId, command.VersionId, command.ExpectedRevision, command.ActorUserId,
            command.IdempotencyKey, "submit", cancellationToken);

    public async Task<ReportDefinitionVersionDto> DecideAsync(DecideReportDefinitionCommand command, CancellationToken cancellationToken)
    {
        await RequireApproveAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        var operation = command.Approve ? "approve" : "reject";
        var replay = await ReplayAsync(command.CompanyId, command.IdempotencyKey, operation, command.VersionId, cancellationToken);
        if (replay is not null) return replay;
        var version = await LoadAsync(command.CompanyId, command.VersionId, true, cancellationToken);
        var approval = version.Approvals.OrderByDescending(x => x.SubmittedUtc).FirstOrDefault(x => x.Status == "pending")
            ?? throw new ReportDefinitionException("report_definition_approval_missing", "No pending approval exists.", true);
        if (approval.SubmittedByUserId == command.ActorUserId)
            throw new ReportDefinitionException("report_definition_self_approval_forbidden", "The submitter cannot approve their own definition.", true);
        var now = _time.GetUtcNow().UtcDateTime;
        try { if (command.Approve) version.Approve(command.ExpectedRevision, now); else version.Reject(command.ExpectedRevision, now); }
        catch (ReportDefinitionConcurrencyException ex) { throw Conflict(ex); }
        approval.Decide(command.Approve, command.ActorUserId, command.DecisionNote, now);
        AddReceipt(command.CompanyId, command.IdempotencyKey, operation, version.Id, command.ActorUserId, now);
        await AuditAsync(command.CompanyId, command.ActorUserId, $"accounting.report_definition.{operation}d", version.Id,
            command.Approve ? "Approved a validated report definition version." : "Rejected a submitted report definition version.",
            version.ReportKind, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        _telemetry.Action(operation, "succeeded", version.ReportKind);
        return Map(await LoadAsync(command.CompanyId, version.Id, false, cancellationToken), true);
    }

    public async Task<ReportDefinitionVersionDto> ActivateAsync(ActivateReportDefinitionCommand command, CancellationToken cancellationToken)
    {
        await RequireManageAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        var replay = await ReplayAsync(command.CompanyId, command.IdempotencyKey, "activate", command.VersionId, cancellationToken);
        if (replay is not null) return replay;
        var version = await LoadAsync(command.CompanyId, command.VersionId, true, cancellationToken);
        var latestValidation = version.ValidationResults.OrderByDescending(x => x.ValidatedUtc).FirstOrDefault();
        if (latestValidation?.IsValid != true || latestValidation.DefinitionHash != version.DefinitionHash)
            throw new ReportDefinitionException("report_definition_validation_required", "A current successful validation is required before activation.", true);
        if (!version.Approvals.Any(x => x.Status == "approved"))
            throw new ReportDefinitionException("report_definition_approval_required", "Approval is required before activation.", true);
        var now = _time.GetUtcNow().UtcDateTime;
        var active = await _db.ReportDefinitionVersions.IgnoreQueryFilters()
            .Where(x => x.CompanyId == command.CompanyId && x.DefinitionId == version.DefinitionId &&
                x.Status == ReportDefinitionVersionStatuses.Active && x.Id != version.Id).ToListAsync(cancellationToken);
        foreach (var prior in active)
        {
            if (prior.EffectiveFrom.HasValue && command.EffectiveFrom <= prior.EffectiveFrom.Value)
                throw new ReportDefinitionException("report_definition_effective_date_conflict", "The new version must become effective after the current active version.", true);
            prior.Retire(command.EffectiveFrom, prior.Revision, now);
        }
        try { version.Activate(command.EffectiveFrom, command.ExpectedRevision, now); }
        catch (ReportDefinitionConcurrencyException ex) { throw Conflict(ex); }
        AddReceipt(command.CompanyId, command.IdempotencyKey, "activate", version.Id, command.ActorUserId, now);
        await AuditAsync(command.CompanyId, command.ActorUserId, "accounting.report_definition.activated", version.Id,
            $"Activated an approved report definition effective {command.EffectiveFrom:yyyy-MM-dd}.", version.ReportKind, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        _telemetry.Action("activate", "succeeded", version.ReportKind);
        return Map(await LoadAsync(command.CompanyId, version.Id, false, cancellationToken), await CanApproveAsync(command.CompanyId, cancellationToken));
    }

    public Task<ReportDefinitionVersionDto> RetireAsync(RetireReportDefinitionCommand command, CancellationToken cancellationToken) =>
        TransitionAsync(command.CompanyId, command.VersionId, command.ExpectedRevision, command.ActorUserId,
            command.IdempotencyKey, "retire", cancellationToken, command.EffectiveTo);

    private async Task<ReportDefinitionVersionDto> TransitionAsync(Guid companyId, Guid versionId, int expectedRevision,
        Guid actor, string key, string operation, CancellationToken cancellationToken, DateOnly? effectiveTo = null)
    {
        await RequireManageAsync(companyId, actor, cancellationToken);
        var replay = await ReplayAsync(companyId, key, operation, versionId, cancellationToken);
        if (replay is not null) return replay;
        var version = await LoadAsync(companyId, versionId, true, cancellationToken);
        var now = _time.GetUtcNow().UtcDateTime;
        try
        {
            if (operation == "submit")
            {
                var latest = version.ValidationResults.OrderByDescending(x => x.ValidatedUtc).FirstOrDefault();
                if (latest?.IsValid != true || latest.DefinitionHash != version.DefinitionHash)
                    throw new ReportDefinitionException("report_definition_validation_required", "Resolve validation issues before submission.", true);
                version.Submit(expectedRevision, now);
                _db.ReportDefinitionApprovals.Add(new(Guid.NewGuid(), companyId, version.Id, actor, now));
            }
            else version.Retire(effectiveTo ?? throw new ArgumentNullException(nameof(effectiveTo)), expectedRevision, now);
        }
        catch (ReportDefinitionConcurrencyException ex) { throw Conflict(ex); }
        AddReceipt(companyId, key, operation, version.Id, actor, now);
        await AuditAsync(companyId, actor, $"accounting.report_definition.{operation}d", version.Id,
            operation == "submit" ? "Submitted a validated report definition for independent approval." : "Retired a report definition prospectively.",
            version.ReportKind, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        _telemetry.Action(operation, "succeeded", version.ReportKind);
        return Map(await LoadAsync(companyId, version.Id, false, cancellationToken), await CanApproveAsync(companyId, cancellationToken));
    }

    private async Task<List<ReportDefinitionValidationIssueDto>> ValidateCoreAsync(Guid companyId,
        ReportDefinitionVersion version, CancellationToken cancellationToken)
    {
        var issues = new List<ReportDefinitionValidationIssueDto>();
        var lines = version.Sections.SelectMany(x => x.Lines).ToArray();
        if (version.Sections.Count == 0) issues.Add(Issue("report_definition_sections_missing", "The definition requires at least one section."));
        foreach (var duplicate in version.Sections.GroupBy(x => x.Code).Where(x => x.Count() > 1))
            issues.Add(Issue("report_definition_duplicate_section", $"Section code {duplicate.Key} is duplicated."));
        foreach (var duplicate in lines.GroupBy(x => x.Code).Where(x => x.Count() > 1))
            issues.Add(Issue("report_definition_duplicate_line", $"Line code {duplicate.Key} is duplicated.", duplicate.First().Id));
        var codes = lines.Select(x => x.Code).ToHashSet(StringComparer.Ordinal);
        var graph = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            if (line.LineType == ReportDefinitionLineTypes.Formula || !string.IsNullOrWhiteSpace(line.Formula))
            {
                var analysis = ReportFormulaEngine.Analyze(line.Formula);
                if (!analysis.IsValid) { issues.Add(Issue("report_definition_formula_invalid", $"{line.Label}: {analysis.Error}", line.Id)); continue; }
                graph[line.Code] = analysis.References;
                foreach (var missing in analysis.References.Where(x => !codes.Contains(x)))
                    issues.Add(Issue("report_definition_reference_missing", $"{line.Label} references unknown line [{missing}].", line.Id));
                if (line.SignRule == ReportDefinitionSignRules.Invert && line.Formula!.TrimStart().StartsWith("-", StringComparison.Ordinal))
                    issues.Add(Issue("report_definition_sign_conflict", $"{line.Label} applies both a negative formula and an inverted display sign.", line.Id));
            }
            else graph[line.Code] = [];
            if (line.LineType is ReportDefinitionLineTypes.Detail or ReportDefinitionLineTypes.Subtotal &&
                string.IsNullOrWhiteSpace(line.Formula) && line.AccountGroups.SelectMany(x => x.Members).Count() == 0)
                issues.Add(Issue("report_definition_mapping_missing", $"{line.Label} has no account-group mapping.", line.Id));
            if (line.DimensionMemberId.HasValue && !line.DimensionTypeId.HasValue)
                issues.Add(Issue("report_definition_dimension_type_missing", $"{line.Label} selects a dimension member without its dimension type.", line.Id));
            if (line.CurrencyMode == ReportDefinitionCurrencyModes.Document && line.DimensionTypeId.HasValue)
                issues.Add(Issue("report_definition_currency_dimension_incompatible", $"{line.Label} cannot combine document-currency aggregation with a dimension filter.", line.Id));
        }
        foreach (var cycle in ReportFormulaEngine.FindCycles(graph))
            issues.Add(Issue("report_definition_formula_cycle", $"Formula cycle detected: {string.Join(" → ", cycle)}.",
                lines.FirstOrDefault(x => x.Code == cycle[0])?.Id));
        var members = version.AccountGroups.SelectMany(g => g.Members.Select(m => new { g.LineId, m.FinanceAccountId })).ToArray();
        foreach (var overlap in members.GroupBy(x => x.FinanceAccountId).Where(x => x.Select(y => y.LineId).Distinct().Count() > 1))
            issues.Add(Issue("report_definition_duplicate_account_coverage",
                "The same finance account is covered by more than one report line.", accountId: overlap.Key));
        var memberIds = members.Select(x => x.FinanceAccountId).Distinct().ToArray();
        var validAccounts = await _db.FinanceAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && memberIds.Contains(x.Id)).Select(x => x.Id).ToArrayAsync(cancellationToken);
        foreach (var invalid in memberIds.Except(validAccounts))
            issues.Add(Issue("report_definition_account_scope_invalid", "An account-group mapping references an unavailable company account.", accountId: invalid));
        var dimensionTypeIds = lines.Where(x => x.DimensionTypeId.HasValue).Select(x => x.DimensionTypeId!.Value).Distinct().ToArray();
        var validDimensionTypes = await _db.AccountingDimensionTypes.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && dimensionTypeIds.Contains(x.Id)).Select(x => x.Id).ToArrayAsync(cancellationToken);
        foreach (var invalid in dimensionTypeIds.Except(validDimensionTypes))
            issues.Add(Issue("report_definition_dimension_scope_invalid", "A line references an unavailable company dimension type."));
        return issues;
    }

    private static ReportDefinitionValidationIssueDto Issue(string code, string explanation, Guid? lineId = null, Guid? accountId = null) =>
        new(code, "error", explanation, lineId, accountId);

    private async Task AddTemplateLayoutAsync(Guid companyId, ReportDefinitionVersion version, string reportKind,
        CancellationToken cancellationToken)
    {
        var mappings = await _db.FinancialStatementMappings.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.IsActive).Select(x => new { x.FinanceAccountId, x.StatementType, x.ReportSection }).ToListAsync(cancellationToken);
        var sections = new List<ReportDefinitionSectionInput>();
        if (reportKind == FinancialReportKinds.CashFlow)
        {
            var kinds = new[] { ("OPERATING", "Operating activities"), ("INVESTING", "Investing activities"), ("FINANCING", "Financing activities") };
            var order = 10;
            foreach (var kind in kinds)
            {
                var accounts = mappings.Where(x => x.StatementType == FinancialStatementType.CashFlow &&
                    string.Equals(x.ReportSection.ToStorageValue(), kind.Item1, StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.FinanceAccountId).Distinct().ToArray();
                sections.Add(new(kind.Item1, kind.Item2, order,
                [new(kind.Item1 + "_NET", "Net cash from " + kind.Item2.ToLowerInvariant(), ReportDefinitionLineTypes.Detail,
                    10, null, ReportDefinitionSignRules.Normal, 1, 2, false, ReportDefinitionCurrencyModes.Functional,
                    null, null, [new(kind.Item1 + "_ACCOUNTS", kind.Item2 + " accounts", accounts)])]));
                order += 10;
            }
            sections.Add(new("SUMMARY", "Cash movement", 40,
            [new("NET_CHANGE", "Net change in cash", ReportDefinitionLineTypes.Formula, 10,
                "SUM([OPERATING_NET],[INVESTING_NET],[FINANCING_NET])", ReportDefinitionSignRules.Normal, 1, 2,
                false, ReportDefinitionCurrencyModes.Functional, null, null, [])]));
        }
        else
        {
            var equity = await _db.FinanceAccounts.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.AccountClass == FinanceAccountClassValues.Equity &&
                    x.IsReportable && !x.EffectiveTo.HasValue)
                .Select(x => x.Id).ToArrayAsync(cancellationToken);
            sections.Add(new("EQUITY", "Equity", 10,
            [new("EQUITY_MOVEMENT", "Movement in equity", ReportDefinitionLineTypes.Detail, 10, null,
                ReportDefinitionSignRules.Normal, 1, 2, false, ReportDefinitionCurrencyModes.Functional, null, null,
                [new("EQUITY_ACCOUNTS", "Equity accounts", equity)])]));
        }
        AddLayout(companyId, version, sections, new("prior_period", 1, true, true));
    }

    private void AddLayout(Guid companyId, ReportDefinitionVersion version, IReadOnlyList<ReportDefinitionSectionInput> sections,
        ReportDefinitionComparisonDto comparison)
    {
        foreach (var sectionInput in sections.OrderBy(x => x.DisplayOrder))
        {
            var section = new ReportDefinitionSection(Guid.NewGuid(), companyId, version.Id, sectionInput.Code, sectionInput.Label, sectionInput.DisplayOrder);
            _db.ReportDefinitionSections.Add(section);
            foreach (var lineInput in sectionInput.Lines.OrderBy(x => x.DisplayOrder))
            {
                var line = new ReportDefinitionLine(Guid.NewGuid(), companyId, version.Id, section.Id, lineInput.Code,
                    lineInput.Label, lineInput.LineType, lineInput.DisplayOrder, lineInput.Formula, lineInput.SignRule,
                    lineInput.Scale, lineInput.Decimals, lineInput.SuppressZero, lineInput.CurrencyMode,
                    lineInput.DimensionTypeId, lineInput.DimensionMemberId);
                _db.ReportDefinitionLines.Add(line);
                foreach (var groupInput in lineInput.AccountGroups)
                {
                    var group = new ReportDefinitionAccountGroup(Guid.NewGuid(), companyId, version.Id, line.Id, groupInput.Code, groupInput.Name);
                    _db.ReportDefinitionAccountGroups.Add(group);
                    foreach (var accountId in groupInput.FinanceAccountIds.Distinct())
                        _db.ReportDefinitionAccountGroupMembers.Add(new(Guid.NewGuid(), companyId, group.Id, accountId));
                }
            }
        }
        _db.ReportDefinitionComparisons.Add(new(Guid.NewGuid(), companyId, version.Id, comparison.Mode,
            comparison.PeriodCount, comparison.ShowVariance, comparison.ShowVariancePercent));
    }

    private void CloneLayout(Guid companyId, ReportDefinitionVersion source, ReportDefinitionVersion target)
    {
        var sections = source.Sections.OrderBy(x => x.DisplayOrder).Select(section => new ReportDefinitionSectionInput(section.Code,
            section.Label, section.DisplayOrder, section.Lines.OrderBy(x => x.DisplayOrder).Select(line => new ReportDefinitionLineInput(
                line.Code, line.Label, line.LineType, line.DisplayOrder, line.Formula, line.SignRule, line.Scale, line.Decimals,
                line.SuppressZero, line.CurrencyMode, line.DimensionTypeId, line.DimensionMemberId,
                line.AccountGroups.Select(g => new ReportDefinitionAccountGroupInput(g.Code, g.Name,
                    g.Members.Select(m => m.FinanceAccountId).ToArray())).ToArray())).ToArray())).ToArray();
        var comparison = source.Comparison is null ? new ReportDefinitionComparisonDto("none", 1, false, false) :
            new(source.Comparison.Mode, source.Comparison.PeriodCount, source.Comparison.ShowVariance, source.Comparison.ShowVariancePercent);
        AddLayout(companyId, target, sections, comparison);
    }

    private async Task<ReportDefinitionVersion> LoadAsync(Guid companyId, Guid versionId, bool tracking, CancellationToken cancellationToken)
    {
        IQueryable<ReportDefinitionVersion> query = _db.ReportDefinitionVersions.IgnoreQueryFilters();
        if (!tracking) query = query.AsNoTracking();
        return await query.Where(x => x.CompanyId == companyId && x.Id == versionId)
            .Include(x => x.Definition).Include(x => x.Comparison)
            .Include(x => x.Sections).ThenInclude(x => x.Lines).ThenInclude(x => x.AccountGroups).ThenInclude(x => x.Members)
            .Include(x => x.AccountGroups).ThenInclude(x => x.Members)
            .Include(x => x.ValidationResults).ThenInclude(x => x.Issues).Include(x => x.Approvals)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("The report definition version was not found in the requested company.");
    }

    private static string DefinitionHash(ReportDefinitionVersion version)
    {
        var canonical = new
        {
            version.ReportKind,
            Sections = version.Sections.OrderBy(x => x.DisplayOrder).ThenBy(x => x.Code).Select(s => new
            {
                s.Code, s.Label, s.DisplayOrder,
                Lines = s.Lines.OrderBy(x => x.DisplayOrder).ThenBy(x => x.Code).Select(l => new
                {
                    l.Code, l.Label, l.LineType, l.DisplayOrder, l.Formula, l.SignRule, l.Scale, l.Decimals,
                    l.SuppressZero, l.CurrencyMode, l.DimensionTypeId, l.DimensionMemberId,
                    Groups = l.AccountGroups.OrderBy(x => x.Code).Select(g => new { g.Code, g.Name,
                        Accounts = g.Members.Select(m => m.FinanceAccountId).Order().ToArray() })
                })
            }),
            Comparison = version.Comparison is null ? null : new { version.Comparison.Mode, version.Comparison.PeriodCount,
                version.Comparison.ShowVariance, version.Comparison.ShowVariancePercent }
        };
        return FinancialReportSuiteCalculator.Sha256(JsonSerializer.Serialize(canonical));
    }

    private ReportDefinitionVersionDto Map(ReportDefinitionVersion x, bool canApprove)
    {
        var validation = x.ValidationResults.OrderByDescending(v => v.ValidatedUtc).FirstOrDefault();
        var approval = x.Approvals.OrderByDescending(v => v.SubmittedUtc).FirstOrDefault();
        return new(x.DefinitionId, x.Id, x.Definition.Code, x.Name, x.ReportKind, x.Definition.SourceTemplateKey,
            x.VersionNumber, x.Status, x.EffectiveFrom, x.EffectiveTo, string.IsNullOrWhiteSpace(x.DefinitionHash) ? null : x.DefinitionHash,
            x.Revision, x.CreatedUtc, x.UpdatedUtc, x.Sections.OrderBy(s => s.DisplayOrder).Select(s => new ReportDefinitionSectionDto(
                s.Id, s.Code, s.Label, s.DisplayOrder, s.Lines.OrderBy(l => l.DisplayOrder).Select(l => new ReportDefinitionLineDto(
                    l.Id, l.Code, l.Label, l.LineType, l.DisplayOrder, l.Formula, l.SignRule, l.Scale, l.Decimals,
                    l.SuppressZero, l.CurrencyMode, l.DimensionTypeId, l.DimensionMemberId,
                    l.AccountGroups.Select(g => new ReportDefinitionAccountGroupDto(g.Id, g.Code, g.Name,
                        g.Members.Select(m => m.FinanceAccountId).ToArray())).ToArray())).ToArray())).ToArray(),
            x.Comparison is null ? new("none", 1, false, false) : new(x.Comparison.Mode, x.Comparison.PeriodCount,
                x.Comparison.ShowVariance, x.Comparison.ShowVariancePercent),
            validation is null ? null : new(validation.Id, validation.IsValid, validation.DefinitionHash,
                validation.ValidatedByUserId, validation.ValidatedUtc, validation.Issues.Select(i => new ReportDefinitionValidationIssueDto(
                    i.Code, i.Severity, i.Explanation, i.LineId, i.AccountId)).ToArray()),
            approval is null ? null : new(approval.Id, approval.Status, approval.SubmittedByUserId, approval.SubmittedUtc,
                approval.DecidedByUserId, approval.DecidedUtc, approval.DecisionNote),
            x.Status == ReportDefinitionVersionStatuses.Draft,
            x.Status == ReportDefinitionVersionStatuses.Draft && validation?.IsValid == true && validation.DefinitionHash == x.DefinitionHash,
            canApprove && x.Status == ReportDefinitionVersionStatuses.Submitted && approval?.Status == "pending",
            x.Status == ReportDefinitionVersionStatuses.Approved && approval?.Status == "approved",
            x.Status is ReportDefinitionVersionStatuses.Active or ReportDefinitionVersionStatuses.Approved);
    }

    private async Task<ReportDefinitionVersionDto?> ReplayAsync(Guid companyId, string key, string operation, Guid? expectedVersionId,
        CancellationToken cancellationToken)
    {
        key = Required(key, nameof(key), 200);
        var receipt = await _db.ReportDefinitionCommandReceipts.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.IdempotencyKey == key, cancellationToken);
        if (receipt is null) return null;
        if (receipt.Operation != operation || expectedVersionId.HasValue && receipt.VersionId != expectedVersionId.Value)
            throw new ReportDefinitionException("report_definition_idempotency_conflict", "The idempotency key is already bound to another report-definition action.", true);
        return Map(await LoadAsync(companyId, receipt.VersionId, false, cancellationToken), await CanApproveAsync(companyId, cancellationToken));
    }

    private void AddReceipt(Guid companyId, string key, string operation, Guid versionId, Guid actor, DateTime now) =>
        _db.ReportDefinitionCommandReceipts.Add(new(Guid.NewGuid(), companyId, Required(key, nameof(key), 200), operation, versionId, actor, now));

    private async Task AuditAsync(Guid companyId, Guid actor, string action, Guid target, string rationale, string reportKind,
        CancellationToken cancellationToken) => await _audit.WriteAsync(new(companyId, AuditActorTypes.User, actor, action,
            "report_definition_version", target.ToString("D"), AuditEventOutcomes.Succeeded, rationale,
            DataSources: ["report_definition", "finance_accounts", "financial_statement_mappings"],
            Metadata: new Dictionary<string, string?> { ["reportKind"] = reportKind }, OccurredUtc: _time.GetUtcNow().UtcDateTime), cancellationToken);

    private async Task RequireViewAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var user = _currentUser.UserId ?? throw new UnauthorizedAccessException();
        var member = await _membership.ResolveAsync(companyId, cancellationToken) ?? throw new UnauthorizedAccessException();
        if (!FinanceAccess.CanViewAccounting(member.MembershipRole.ToStorageValue())) throw new UnauthorizedAccessException();
    }
    private async Task RequireManageAsync(Guid companyId, Guid actor, CancellationToken cancellationToken)
    {
        var user = _currentUser.UserId ?? throw new UnauthorizedAccessException();
        if (user != actor) throw new UnauthorizedAccessException();
        var member = await _membership.ResolveAsync(companyId, cancellationToken) ?? throw new UnauthorizedAccessException();
        if (!FinanceAccess.CanManageAccounting(member.MembershipRole.ToStorageValue())) throw new UnauthorizedAccessException();
    }
    private async Task RequireApproveAsync(Guid companyId, Guid actor, CancellationToken cancellationToken)
    {
        var user = _currentUser.UserId ?? throw new UnauthorizedAccessException(); if (user != actor) throw new UnauthorizedAccessException();
        var member = await _membership.ResolveAsync(companyId, cancellationToken) ?? throw new UnauthorizedAccessException();
        if (!FinanceAccess.CanApproveInvoices(member.MembershipRole.ToStorageValue())) throw new UnauthorizedAccessException();
    }
    private async Task<bool> CanApproveAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var user = _currentUser.UserId; if (!user.HasValue) return false;
        var member = await _membership.ResolveAsync(companyId, cancellationToken);
        return member is not null && FinanceAccess.CanApproveInvoices(member.MembershipRole.ToStorageValue());
    }

    private static ReportDefinitionException Conflict(ReportDefinitionConcurrencyException ex) =>
        new("report_definition_concurrency_conflict", $"{ex.Message} Current revision: {ex.CurrentRevision}.", true);
    private static string NormalizeCode(string value) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Code is required.") : value.Trim().ToUpperInvariant();
    private static string Required(string value, string name, int max) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name)
        : value.Trim().Length <= max ? value.Trim() : throw new ArgumentOutOfRangeException(name);
}
