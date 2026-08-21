using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingConfigurationService : IAccountingConfigurationService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAccountingPolicyPackResolver _packResolver;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly TimeProvider _timeProvider;

    public AccountingConfigurationService(
        VirtualCompanyDbContext dbContext,
        IAccountingPolicyPackResolver packResolver,
        IAuditEventWriter auditEventWriter,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _packResolver = packResolver;
        _auditEventWriter = auditEventWriter;
        _timeProvider = timeProvider;
    }

    public async Task<AccountingSetupStatusDto> GetSetupStatusAsync(
        GetAccountingSetupStatusQuery query,
        CancellationToken cancellationToken)
    {
        ValidateCompanyId(query.CompanyId);
        var configuration = await LoadConfigurationAsync(query.CompanyId, tracking: false, cancellationToken);
        return configuration is null
            ? BuildMissingStatus(query.CompanyId)
            : BuildStatus(configuration);
    }

    public async Task<AccountingSetupStatusDto> CreateInitialAsync(
        CreateInitialAccountingConfigurationCommand command,
        CancellationToken cancellationToken)
    {
        ValidateCompanyId(command.CompanyId);
        ValidateActor(command.ActorUserId);
        var pack = _packResolver.Resolve(command.PolicyPackKey, command.PolicyPackVersion);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        if (await _dbContext.AccountingConfigurations.AnyAsync(
                configuration => configuration.CompanyId == command.CompanyId,
                cancellationToken))
        {
            throw new AccountingConfigurationException(
                AccountingConfigurationReasonCodes.ConfigurationAlreadyExists,
                "Accounting has already been configured for this company.",
                isConflict: true);
        }

        var assignments = NormalizeAssignments(command.AccountRoleAssignments);
        await ValidateRoleAssignmentsAsync(command.CompanyId, pack, assignments, cancellationToken);

        var configuration = new AccountingConfiguration(
            Guid.NewGuid(),
            command.CompanyId,
            command.BaseCurrency,
            command.FiscalYearStartMonth,
            command.FiscalYearStartDay,
            pack.Definition.PackKey,
            pack.Definition.Version,
            command.EffectiveFrom,
            command.RoundingPrecision,
            command.RoundingMode,
            command.ActorUserId,
            nowUtc);

        foreach (var assignment in assignments)
        {
            configuration.AccountRoles.Add(new AccountingConfigurationAccountRole(
                Guid.NewGuid(),
                command.CompanyId,
                configuration.Id,
                assignment.Key,
                assignment.Value,
                nowUtc));
        }

        configuration.PolicyPackSelections.Add(new AccountingPolicyPackSelection(
            Guid.NewGuid(),
            command.CompanyId,
            configuration.Id,
            pack.Definition.PackKey,
            pack.Definition.Version,
            pack.DefinitionHash,
            pack.Definition.IsStatutoryComplianceValidated,
            command.EffectiveFrom,
            command.ActorUserId,
            nowUtc));

        var initialIssues = BuildIssues(pack, assignments.Keys);
        if (initialIssues.All(issue => !issue.IsBlocking))
        {
            configuration.SetSetupState(AccountingSetupStateValues.Ready, command.ActorUserId, nowUtc);
        }

        _dbContext.AccountingConfigurations.Add(configuration);
        _dbContext.AccountingAuthorityPeriods.Add(new AccountingAuthorityPeriod(
            Guid.NewGuid(),
            command.CompanyId,
            command.EffectiveFrom,
            effectiveTo: null,
            AccountingAuthorityValues.InternalLedger,
            providerKey: null,
            command.ActorUserId,
            "Virtual Company became the accounting authority when accounting setup was created.",
            nowUtc));
        await WriteAuditAsync(
            command.CompanyId,
            command.ActorUserId,
            AuditEventActions.AccountingConfigurationCreated,
            configuration.Id,
            "Internal-ledger accounting configuration was created.",
            pack,
            command.CorrelationId,
            nowUtc,
            new Dictionary<string, string?>
            {
                ["authority"] = AccountingAuthorityValues.InternalLedger,
                ["baseCurrency"] = configuration.BaseCurrency,
                ["setupState"] = configuration.SetupState
            },
            cancellationToken);
        await WriteAuditAsync(
            command.CompanyId,
            command.ActorUserId,
            AuditEventActions.AccountingPolicyPackSelected,
            configuration.Id,
            "The initial accounting policy pack was selected.",
            pack,
            command.CorrelationId,
            nowUtc,
            new Dictionary<string, string?>
            {
                ["effectiveFrom"] = command.EffectiveFrom.ToString("yyyy-MM-dd"),
                ["selectionType"] = "initial"
            },
            cancellationToken);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsConfigurationUniqueConflict(exception))
        {
            throw new AccountingConfigurationException(
                AccountingConfigurationReasonCodes.ConfigurationAlreadyExists,
                "Accounting has already been configured for this company.",
                isConflict: true);
        }

        return BuildStatus(configuration);
    }

    public async Task<AccountingPolicyPackImpactPreviewDto> PreviewPolicyPackSelectionAsync(
        PreviewAccountingPolicyPackSelectionQuery query,
        CancellationToken cancellationToken)
    {
        ValidateCompanyId(query.CompanyId);
        var targetPack = _packResolver.Resolve(query.PackKey, query.PackVersion);
        var configuration = await LoadConfigurationAsync(query.CompanyId, tracking: false, cancellationToken)
            ?? throw MissingConfiguration();
        var currentPack = _packResolver.Resolve(configuration.PolicyPackKey, configuration.PolicyPackVersion);
        var assignments = configuration.AccountRoles.ToDictionary(role => role.RoleKey, role => role.FinanceAccountId, StringComparer.OrdinalIgnoreCase);

        foreach (var assignment in NormalizeAssignments(query.AccountRoleAssignments))
        {
            assignments[assignment.Key] = assignment.Value;
        }

        var issues = new List<AccountingConfigurationIssueDto>();
        if (string.Equals(currentPack.Definition.PackKey, targetPack.Definition.PackKey, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(currentPack.Definition.Version, targetPack.Definition.Version, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new(
                AccountingConfigurationReasonCodes.InvalidUpgrade,
                "The selected policy pack and version are already configured.",
                IsBlocking: true));
        }

        if (query.EffectiveFrom <= configuration.PolicyPackEffectiveFrom ||
            query.EffectiveFrom < DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime))
        {
            issues.Add(new(
                AccountingConfigurationReasonCodes.InvalidUpgrade,
                "A policy-pack upgrade must take effect after the current selection and cannot rewrite earlier activity.",
                IsBlocking: true));
        }

        issues.AddRange(await BuildRoleAssignmentIssuesAsync(query.CompanyId, targetPack, assignments, cancellationToken));
        var warnings = BuildWarnings(targetPack);
        var currentRoleKeys = currentPack.Definition.AccountRoles.Select(role => role.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targetRoleKeys = targetPack.Definition.AccountRoles.Select(role => role.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentTaxKeys = currentPack.Definition.TaxRules.Select(rule => rule.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targetTaxKeys = targetPack.Definition.TaxRules.Select(rule => rule.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentExports = currentPack.Definition.SupportedExports.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targetExports = targetPack.Definition.SupportedExports.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new AccountingPolicyPackImpactPreviewDto(
            query.CompanyId,
            targetPack.Definition.PackKey,
            targetPack.Definition.Version,
            query.EffectiveFrom,
            issues.All(issue => !issue.IsBlocking),
            IsUpgrade: true,
            targetPack.Definition.AccountRoles.Where(role => role.IsRequired && !currentRoleKeys.Contains(role.Key)).Select(role => role.Key).Order().ToArray(),
            currentRoleKeys.Except(targetRoleKeys, StringComparer.OrdinalIgnoreCase).Order().ToArray(),
            targetTaxKeys.Except(currentTaxKeys, StringComparer.OrdinalIgnoreCase).Order().ToArray(),
            currentTaxKeys.Except(targetTaxKeys, StringComparer.OrdinalIgnoreCase).Order().ToArray(),
            targetExports.Except(currentExports, StringComparer.OrdinalIgnoreCase).Order().ToArray(),
            currentExports.Except(targetExports, StringComparer.OrdinalIgnoreCase).Order().ToArray(),
            issues,
            warnings);
    }

    public async Task<AccountingSetupStatusDto> ApplyPolicyPackSelectionAsync(
        ApplyAccountingPolicyPackSelectionCommand command,
        CancellationToken cancellationToken)
    {
        ValidateCompanyId(command.CompanyId);
        ValidateActor(command.ActorUserId);
        var preview = await PreviewPolicyPackSelectionAsync(
            new PreviewAccountingPolicyPackSelectionQuery(
                command.CompanyId,
                command.PackKey,
                command.PackVersion,
                command.EffectiveFrom,
                command.AccountRoleAssignments),
            cancellationToken);
        var blockingIssue = preview.Issues.FirstOrDefault(issue => issue.IsBlocking);
        if (blockingIssue is not null)
        {
            throw new AccountingConfigurationException(blockingIssue.ReasonCode, blockingIssue.Explanation);
        }

        var targetPack = _packResolver.Resolve(command.PackKey, command.PackVersion);
        var configuration = await LoadConfigurationAsync(command.CompanyId, tracking: true, cancellationToken)
            ?? throw MissingConfiguration();
        if (configuration.Version != command.ExpectedVersion)
        {
            throw ConcurrencyConflict();
        }

        var oldPackKey = configuration.PolicyPackKey;
        var oldPackVersion = configuration.PolicyPackVersion;
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var assignments = configuration.AccountRoles.ToDictionary(role => role.RoleKey, role => role.FinanceAccountId, StringComparer.OrdinalIgnoreCase);
        foreach (var assignment in NormalizeAssignments(command.AccountRoleAssignments))
        {
            assignments[assignment.Key] = assignment.Value;
        }

        var supportedRoles = targetPack.Definition.AccountRoles.Select(role => role.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var existing in configuration.AccountRoles.Where(role => !supportedRoles.Contains(role.RoleKey)).ToArray())
        {
            configuration.AccountRoles.Remove(existing);
        }

        foreach (var assignment in assignments.Where(assignment => supportedRoles.Contains(assignment.Key)))
        {
            var existing = configuration.AccountRoles.FirstOrDefault(role => string.Equals(role.RoleKey, assignment.Key, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                var accountRole = new AccountingConfigurationAccountRole(
                    Guid.NewGuid(), command.CompanyId, configuration.Id, assignment.Key, assignment.Value, nowUtc);
                configuration.AccountRoles.Add(accountRole);
                _dbContext.AccountingConfigurationAccountRoles.Add(accountRole);
            }
            else if (existing.FinanceAccountId != assignment.Value)
            {
                existing.Reassign(assignment.Value, nowUtc);
            }
        }

        var openSelection = configuration.PolicyPackSelections.SingleOrDefault(selection => selection.EffectiveTo == null)
            ?? throw new AccountingConfigurationException(
                AccountingConfigurationReasonCodes.InvalidUpgrade,
                "The current accounting policy-pack history is incomplete and must be repaired before an upgrade can be applied.");
        openSelection.EndBefore(command.EffectiveFrom);
        var newSelection = new AccountingPolicyPackSelection(
            Guid.NewGuid(),
            command.CompanyId,
            configuration.Id,
            targetPack.Definition.PackKey,
            targetPack.Definition.Version,
            targetPack.DefinitionHash,
            targetPack.Definition.IsStatutoryComplianceValidated,
            command.EffectiveFrom,
            command.ActorUserId,
            nowUtc);
        configuration.PolicyPackSelections.Add(newSelection);
        _dbContext.AccountingPolicyPackSelections.Add(newSelection);
        configuration.ApplyPolicyPack(
            targetPack.Definition.PackKey,
            targetPack.Definition.Version,
            command.EffectiveFrom,
            command.ActorUserId,
            nowUtc);
        configuration.SetSetupState(AccountingSetupStateValues.Ready, command.ActorUserId, nowUtc);

        await WriteAuditAsync(
            command.CompanyId,
            command.ActorUserId,
            AuditEventActions.AccountingPolicyPackUpgraded,
            configuration.Id,
            "The accounting policy-pack selection was upgraded for future activity.",
            targetPack,
            command.CorrelationId,
            nowUtc,
            new Dictionary<string, string?>
            {
                ["previousPackKey"] = oldPackKey,
                ["previousPackVersion"] = oldPackVersion,
                ["effectiveFrom"] = command.EffectiveFrom.ToString("yyyy-MM-dd")
            },
            cancellationToken);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw ConcurrencyConflict();
        }

        return BuildStatus(configuration);
    }

    public async Task<AccountingSetupStatusDto> ValidateAsync(
        ValidateAccountingConfigurationQuery query,
        CancellationToken cancellationToken)
    {
        ValidateCompanyId(query.CompanyId);
        var configuration = await LoadConfigurationAsync(query.CompanyId, tracking: false, cancellationToken);
        return configuration is null
            ? BuildMissingStatus(query.CompanyId)
            : BuildStatus(configuration);
    }

    public async Task<AccountingCapabilityDecisionDto> GetCapabilityAsync(
        GetAccountingCapabilityQuery query,
        CancellationToken cancellationToken)
    {
        ValidateCompanyId(query.CompanyId);
        var capabilityKey = NormalizeCapability(query.CapabilityKey);
        var configuration = await LoadConfigurationAsync(query.CompanyId, tracking: false, cancellationToken)
            ?? throw MissingConfiguration();
        var pack = _packResolver.Resolve(configuration.PolicyPackKey, configuration.PolicyPackVersion);
        var available = pack.Definition.SupportedCapabilities.Contains(capabilityKey, StringComparer.OrdinalIgnoreCase);
        var isCountrySpecific = capabilityKey is
            AccountingPolicyCapabilityKeys.CountrySpecificReporting or
            AccountingPolicyCapabilityKeys.CountrySpecificTax or
            AccountingPolicyCapabilityKeys.StatutoryExport;

        return new AccountingCapabilityDecisionDto(
            query.CompanyId,
            capabilityKey,
            available,
            available ? null : isCountrySpecific ? AccountingConfigurationReasonCodes.CountrySpecificCapabilityUnavailable : AccountingConfigurationReasonCodes.IncompleteConfiguration,
            available
                ? "This capability is available in the selected accounting policy pack."
                : isCountrySpecific
                    ? "Country-specific rules for this capability are not available in the selected policy pack. No rules were guessed."
                    : "This capability is not available in the selected accounting policy pack.",
            pack.Definition.PackKey,
            pack.Definition.Version);
    }

    private AccountingSetupStatusDto BuildStatus(AccountingConfiguration configuration)
    {
        var pack = _packResolver.Resolve(configuration.PolicyPackKey, configuration.PolicyPackVersion);
        var issues = BuildIssues(pack, configuration.AccountRoles.Select(role => role.RoleKey));
        var warnings = BuildWarnings(pack);
        var rolesByKey = configuration.AccountRoles.ToDictionary(role => role.RoleKey, StringComparer.OrdinalIgnoreCase);
        var roleDtos = pack.Definition.AccountRoles
            .OrderBy(role => role.Key, StringComparer.OrdinalIgnoreCase)
            .Select(role =>
            {
                rolesByKey.TryGetValue(role.Key, out var assigned);
                return new AccountingAccountRoleReferenceDto(
                    role.Key,
                    role.DisplayName,
                    role.IsRequired,
                    role.IsControlAccount,
                    assigned?.FinanceAccountId,
                    assigned?.FinanceAccount?.Code,
                    assigned?.FinanceAccount?.Name);
            })
            .ToArray();
        var history = configuration.PolicyPackSelections
            .OrderBy(selection => selection.EffectiveFrom)
            .Select(selection => new AccountingPolicyPackSelectionDto(
                selection.Id,
                selection.PackKey,
                selection.PackVersion,
                selection.DefinitionHash,
                selection.IsStatutoryComplianceValidated,
                selection.EffectiveFrom,
                selection.EffectiveTo,
                selection.SelectedByUserId,
                selection.SelectedUtc))
            .ToArray();
        var dto = new AccountingConfigurationDto(
            configuration.Id,
            configuration.CompanyId,
            configuration.BaseCurrency,
            configuration.FiscalYearStartMonth,
            configuration.FiscalYearStartDay,
            configuration.Authority,
            configuration.SetupState,
            configuration.PolicyPackKey,
            configuration.PolicyPackVersion,
            configuration.PolicyPackEffectiveFrom,
            configuration.RoundingPrecision,
            configuration.RoundingMode,
            configuration.Version,
            pack.Definition.IsCountryNeutral,
            pack.Definition.IsStatutoryComplianceValidated,
            pack.Definition.ComplianceNotice,
            roleDtos,
            history,
            configuration.CreatedUtc,
            configuration.UpdatedUtc);

        return new AccountingSetupStatusDto(
            configuration.CompanyId,
            IsConfigured: true,
            CanUseInternalLedger: string.Equals(configuration.Authority, AccountingAuthorityValues.InternalLedger, StringComparison.Ordinal),
            IsReady: issues.All(issue => !issue.IsBlocking),
            IsCountrySpecificComplianceConfigured: pack.Definition.IsStatutoryComplianceValidated,
            configuration.Authority,
            configuration.SetupState,
            dto,
            issues,
            warnings);
    }

    private async Task<AccountingConfiguration?> LoadConfigurationAsync(
        Guid companyId,
        bool tracking,
        CancellationToken cancellationToken)
    {
        IQueryable<AccountingConfiguration> query = _dbContext.AccountingConfigurations
            .Include(configuration => configuration.AccountRoles)
                .ThenInclude(role => role.FinanceAccount)
            .Include(configuration => configuration.PolicyPackSelections)
            .Where(configuration => configuration.CompanyId == companyId);
        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query.SingleOrDefaultAsync(cancellationToken);
    }

    private async Task ValidateRoleAssignmentsAsync(
        Guid companyId,
        IAccountingPolicyPack pack,
        IReadOnlyDictionary<string, Guid> assignments,
        CancellationToken cancellationToken)
    {
        var issues = await BuildRoleAssignmentIssuesAsync(companyId, pack, assignments, cancellationToken);
        var issue = issues.FirstOrDefault(item => item.IsBlocking && item.ReasonCode == AccountingConfigurationReasonCodes.InvalidAccountRole);
        if (issue is not null)
        {
            throw new AccountingConfigurationException(issue.ReasonCode, issue.Explanation);
        }
    }

    private async Task<IReadOnlyList<AccountingConfigurationIssueDto>> BuildRoleAssignmentIssuesAsync(
        Guid companyId,
        IAccountingPolicyPack pack,
        IReadOnlyDictionary<string, Guid> assignments,
        CancellationToken cancellationToken)
    {
        var issues = new List<AccountingConfigurationIssueDto>();
        var supportedRoles = pack.Definition.AccountRoles.Select(role => role.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var assignment in assignments.Where(assignment => !supportedRoles.Contains(assignment.Key)))
        {
            issues.Add(new(
                AccountingConfigurationReasonCodes.InvalidAccountRole,
                $"Account role '{assignment.Key}' is not defined by the selected policy pack.",
                assignment.Key));
        }

        var accountIds = assignments.Values.Distinct().ToArray();
        var validAccountIds = accountIds.Length == 0
            ? new HashSet<Guid>()
            : (await _dbContext.FinanceAccounts
                .AsNoTracking()
                .Where(account => account.CompanyId == companyId && accountIds.Contains(account.Id))
                .Select(account => account.Id)
                .ToListAsync(cancellationToken))
                .ToHashSet();
        foreach (var assignment in assignments.Where(assignment => !validAccountIds.Contains(assignment.Value)))
        {
            issues.Add(new(
                AccountingConfigurationReasonCodes.InvalidAccountRole,
                $"The account assigned to role '{assignment.Key}' is not available for this company.",
                assignment.Key));
        }

        foreach (var role in pack.Definition.AccountRoles.Where(role => role.IsRequired && !assignments.ContainsKey(role.Key)))
        {
            issues.Add(new(
                AccountingConfigurationReasonCodes.MissingRequiredAccountRole,
                $"Assign an account to the required role '{role.DisplayName}'.",
                role.Key));
        }

        return issues;
    }

    private static IReadOnlyList<AccountingConfigurationIssueDto> BuildIssues(
        IAccountingPolicyPack pack,
        IEnumerable<string> assignedRoleKeys)
    {
        var assigned = assignedRoleKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return pack.Definition.AccountRoles
            .Where(role => role.IsRequired && !assigned.Contains(role.Key))
            .Select(role => new AccountingConfigurationIssueDto(
                AccountingConfigurationReasonCodes.MissingRequiredAccountRole,
                $"Assign an account to the required role '{role.DisplayName}'.",
                role.Key))
            .ToArray();
    }

    private static IReadOnlyList<AccountingConfigurationIssueDto> BuildWarnings(IAccountingPolicyPack pack) =>
        pack.Definition.IsStatutoryComplianceValidated
            ? []
            : [new AccountingConfigurationIssueDto(
                AccountingConfigurationReasonCodes.CountrySpecificCapabilityUnavailable,
                pack.Definition.ComplianceNotice,
                AccountingPolicyCapabilityKeys.CountrySpecificReporting,
                IsBlocking: false)];

    private static AccountingSetupStatusDto BuildMissingStatus(Guid companyId) =>
        new(
            companyId,
            IsConfigured: false,
            CanUseInternalLedger: false,
            IsReady: false,
            IsCountrySpecificComplianceConfigured: false,
            AccountingAuthorityValues.InternalLedger,
            AccountingSetupStateValues.Incomplete,
            Configuration: null,
            Issues: [new AccountingConfigurationIssueDto(
                AccountingConfigurationReasonCodes.IncompleteConfiguration,
                "Create the accounting configuration before using the internal ledger.")],
            Warnings: []);

    private Task WriteAuditAsync(
        Guid companyId,
        Guid actorUserId,
        string action,
        Guid configurationId,
        string rationale,
        IAccountingPolicyPack pack,
        string? correlationId,
        DateTime occurredUtc,
        IReadOnlyDictionary<string, string?> metadata,
        CancellationToken cancellationToken) =>
        _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                companyId,
                AuditActorTypes.User,
                actorUserId,
                action,
                AuditTargetTypes.AccountingConfiguration,
                configurationId.ToString("D"),
                AuditEventOutcomes.Succeeded,
                rationale,
                DataSources: ["accounting_policy_pack"],
                Metadata: new Dictionary<string, string?>(metadata, StringComparer.OrdinalIgnoreCase)
                {
                    ["packKey"] = pack.Definition.PackKey,
                    ["packVersion"] = pack.Definition.Version,
                    ["definitionHash"] = pack.DefinitionHash,
                    ["statutoryComplianceValidated"] = pack.Definition.IsStatutoryComplianceValidated ? "true" : "false"
                },
                CorrelationId: correlationId,
                OccurredUtc: occurredUtc),
            cancellationToken);

    private static Dictionary<string, Guid> NormalizeAssignments(IReadOnlyDictionary<string, Guid>? assignments)
    {
        var normalized = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        if (assignments is null)
        {
            return normalized;
        }

        foreach (var assignment in assignments)
        {
            if (string.IsNullOrWhiteSpace(assignment.Key) || assignment.Value == Guid.Empty)
            {
                throw new AccountingConfigurationException(
                    AccountingConfigurationReasonCodes.InvalidAccountRole,
                    "Every account role assignment requires a role and an account.");
            }

            var roleKey = assignment.Key.Trim().Replace('-', '_').ToLowerInvariant();
            if (!normalized.TryAdd(roleKey, assignment.Value))
            {
                throw new AccountingConfigurationException(
                    AccountingConfigurationReasonCodes.InvalidAccountRole,
                    $"Account role '{roleKey}' was assigned more than once.");
            }
        }

        return normalized;
    }

    private static string NormalizeCapability(string capabilityKey) =>
        string.IsNullOrWhiteSpace(capabilityKey)
            ? throw new ArgumentException("Capability key is required.", nameof(capabilityKey))
            : capabilityKey.Trim().Replace('-', '_').ToLowerInvariant();

    private static void ValidateCompanyId(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }
    }

    private static void ValidateActor(Guid actorUserId)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("A resolved company user is required for accounting configuration changes.");
        }
    }

    private static AccountingConfigurationException MissingConfiguration() =>
        new(
            AccountingConfigurationReasonCodes.ConfigurationNotFound,
            "Accounting has not been configured for this company.");

    private static AccountingConfigurationException ConcurrencyConflict() =>
        new(
            AccountingConfigurationReasonCodes.ConcurrencyConflict,
            "Accounting configuration changed after it was loaded. Reload the setup status and try again.",
            isConflict: true);

    private static bool IsConfigurationUniqueConflict(DbUpdateException exception)
    {
        if (exception.InnerException is SqlException { Number: 2601 or 2627 } sqlException)
        {
            return sqlException.Message.Contains(
                "IX_accounting_configurations_company_id",
                StringComparison.OrdinalIgnoreCase);
        }

        var message = exception.ToString();
        return message.Contains(
                   "UNIQUE constraint failed: accounting_configurations.company_id",
                   StringComparison.OrdinalIgnoreCase) ||
               message.Contains(
                   "IX_accounting_configurations_company_id",
                   StringComparison.OrdinalIgnoreCase);
    }
}
