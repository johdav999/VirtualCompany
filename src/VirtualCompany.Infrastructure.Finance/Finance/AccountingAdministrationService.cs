using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingAdministrationService : IAccountingAdministrationService
{
    private static readonly IReadOnlyList<AccountingVoucherSeriesPreviewDto> DefaultVoucherSeries =
    [
        new("G", "General journal", "G"),
        new("CI", "Customer invoices", "CI"),
        new("SB", "Supplier bills", "SB"),
        new("B", "Bank", "B"),
        new("CR", "Corrections", "CR")
    ];

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAccountingPolicyPackResolver _packResolver;
    private readonly IAccountingConfigurationService _configurationService;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly TimeProvider _timeProvider;

    public AccountingAdministrationService(
        VirtualCompanyDbContext dbContext,
        IAccountingPolicyPackResolver packResolver,
        IAccountingConfigurationService configurationService,
        IAuditEventWriter auditEventWriter,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _packResolver = packResolver;
        _configurationService = configurationService;
        _auditEventWriter = auditEventWriter;
        _timeProvider = timeProvider;
    }

    public Task<IReadOnlyList<AccountingPolicyPackOptionDto>> GetPolicyPacksAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<AccountingPolicyPackOptionDto> result = _packResolver.GetAll()
            .Select(pack => new AccountingPolicyPackOptionDto(
                pack.Definition.PackKey,
                pack.Definition.Version,
                pack.Definition.DisplayName,
                pack.Definition.CountryOrRegion,
                pack.Definition.IsCountryNeutral,
                pack.Definition.IsStatutoryComplianceValidated,
                pack.Definition.ComplianceNotice,
                pack.Definition.ChartTemplates.Select(template => new AccountingChartTemplateOptionDto(
                    template.Key,
                    template.DisplayName,
                    template.Accounts.Count)).ToArray()))
            .OrderBy(option => option.IsCountryNeutral ? 0 : 1)
            .ThenBy(option => option.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Task.FromResult(result);
    }

    public async Task<AccountingSetupPreviewDto> PreviewSetupAsync(
        PreviewAccountingSetupQuery query,
        CancellationToken cancellationToken)
    {
        ValidateCompanyId(query.CompanyId);
        var currency = NormalizeCurrency(query.BaseCurrency);
        var pack = _packResolver.Resolve(query.PolicyPackKey, query.PolicyPackVersion);
        var templateChart = ResolveChart(pack, query.ChartTemplateKey);
        var periods = BuildMonthlyPeriods(query.FiscalYearStart);
        var issues = new List<AccountingConfigurationIssueDto>();
        var warnings = new List<AccountingConfigurationIssueDto>();
        var chart = await ResolveSetupChartAsync(
            query.CompanyId,
            currency,
            templateChart,
            warnings,
            cancellationToken);
        var roleAssignments = BuildRoleAssignments(pack, chart, query.AccountRoleCodeAssignments, issues);
        var endExclusive = query.FiscalYearStart.AddYears(1);

        var existingConfiguration = await _dbContext.AccountingConfigurations
            .AsNoTracking()
            .AnyAsync(configuration => configuration.CompanyId == query.CompanyId, cancellationToken);

        var existingPeriods = await _dbContext.FiscalPeriods
            .AsNoTracking()
            .Where(period =>
                period.CompanyId == query.CompanyId &&
                period.StartUtc < ToUtc(endExclusive) &&
                period.EndUtc > ToUtc(query.FiscalYearStart))
            .ToListAsync(cancellationToken);
        AddPeriodConflicts(periods, existingPeriods, issues);

        var defaultSeriesCodes = DefaultVoucherSeries.Select(series => series.Code).ToArray();
        var existingSeries = await _dbContext.VoucherSeries
            .AsNoTracking()
            .Where(series => series.CompanyId == query.CompanyId && defaultSeriesCodes.Contains(series.Code))
            .ToListAsync(cancellationToken);
        foreach (var series in DefaultVoucherSeries)
        {
            var existing = existingSeries.FirstOrDefault(item => string.Equals(item.Code, series.Code, StringComparison.OrdinalIgnoreCase));
            if (existing is not null &&
                (!string.Equals(existing.DisplayName, series.DisplayName, StringComparison.Ordinal) ||
                 !string.Equals(existing.NumberPrefix, series.NumberPrefix, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(new AccountingConfigurationIssueDto(
                    AccountingConfigurationReasonCodes.SetupConflict,
                    $"Voucher series {series.DisplayName} already exists with different numbering settings.",
                    series.Code));
            }
        }

        if (pack.Definition.IsCountryNeutral)
        {
            warnings.Add(new AccountingConfigurationIssueDto(
                AccountingConfigurationReasonCodes.CountrySpecificCapabilityUnavailable,
                pack.Definition.ComplianceNotice,
                IsBlocking: false));
        }

        var roleByCode = roleAssignments
            .GroupBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(pair => pair.Key).First(), StringComparer.OrdinalIgnoreCase);
        var previewAccounts = chart.Accounts.Select(account =>
        {
            var roleKey = roleByCode.GetValueOrDefault(account.Code);
            var role = pack.Definition.AccountRoles.FirstOrDefault(candidate =>
                string.Equals(candidate.Key, roleKey, StringComparison.OrdinalIgnoreCase));
            return new AccountingSetupAccountPreviewDto(
                account.Code,
                account.Label,
                DisplayAccountClass(account.AccountClass),
                DisplayNormalBalance(account.NormalBalance),
                role?.DisplayName,
                role?.IsControlAccount == true,
                ResolveReportingPlacement(pack, account.AccountClass));
        }).ToArray();

        return new AccountingSetupPreviewDto(
            query.CompanyId,
            currency,
            query.FiscalYearStart,
            endExclusive.AddDays(-1),
            pack.Definition.DisplayName,
            templateChart.DisplayName,
            pack.Definition.IsCountryNeutral,
            pack.Definition.IsStatutoryComplianceValidated,
            pack.Definition.ComplianceNotice,
            pack.Definition.TaxRules.Count == 0 ? "No tax rules are configured." : $"{pack.Definition.TaxRules.Count} policy-pack tax rules will be applied.",
            issues.All(issue => !issue.IsBlocking),
            existingConfiguration,
            previewAccounts,
            pack.Definition.TaxRules.Select(rule => new AccountingSetupTaxPreviewDto(rule.DisplayName, rule.Rate, rule.EffectiveFrom)).ToArray(),
            periods,
            DefaultVoucherSeries,
            issues,
            warnings);
    }

    public async Task<AccountingSetupCompletionDto> CompleteSetupAsync(
        CompleteAccountingSetupCommand command,
        CancellationToken cancellationToken)
    {
        ValidateCompanyId(command.CompanyId);
        ValidateActor(command.ActorUserId);
        var preview = await PreviewSetupAsync(
            new PreviewAccountingSetupQuery(
                command.CompanyId,
                command.BaseCurrency,
                command.FiscalYearStart,
                command.PolicyPackKey,
                command.PolicyPackVersion,
                command.ChartTemplateKey,
                command.AccountRoleCodeAssignments),
            cancellationToken);
        var blockingIssue = preview.Issues.FirstOrDefault(issue => issue.IsBlocking);
        if (blockingIssue is not null)
        {
            throw new AccountingConfigurationException(blockingIssue.ReasonCode, blockingIssue.Explanation, isConflict: true);
        }

        if (preview.IsAlreadyConfigured)
        {
            if (await MatchesCompletedSetupAsync(command, cancellationToken))
            {
                return await BuildCompletionAsync(command.CompanyId, wasAlreadyApplied: true, cancellationToken);
            }

            throw SetupConflict("Accounting is already configured with different setup choices. Review the existing setup before making changes.");
        }

        var pack = _packResolver.Resolve(command.PolicyPackKey, command.PolicyPackVersion);
        var templateChart = ResolveChart(pack, command.ChartTemplateKey);
        var chart = await ResolveSetupChartAsync(
            command.CompanyId,
            preview.BaseCurrency,
            templateChart,
            warnings: null,
            cancellationToken);
        var roleIssues = new List<AccountingConfigurationIssueDto>();
        var roleAssignments = BuildRoleAssignments(pack, chart, command.AccountRoleCodeAssignments, roleIssues);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        // SQL Server retrying execution strategies require explicit transactions to be
        // created inside the retriable delegate. This keeps the complete setup write
        // atomic while allowing transient SQL failures to be retried safely.
        var executionStrategy = _dbContext.Database.CreateExecutionStrategy();
        return await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            try
            {
            var chartCodes = chart.Accounts.Select(account => account.Code).ToArray();
            var accounts = await _dbContext.FinanceAccounts
                .Where(account => account.CompanyId == command.CompanyId && chartCodes.Contains(account.Code))
                .ToListAsync(cancellationToken);
            var mappings = await _dbContext.FinancialStatementMappings
                .Where(mapping => mapping.CompanyId == command.CompanyId && chartCodes.Contains(mapping.FinanceAccount.Code))
                .ToListAsync(cancellationToken);

            foreach (var template in chart.Accounts)
            {
                var account = accounts.FirstOrDefault(item => string.Equals(item.Code, template.Code, StringComparison.OrdinalIgnoreCase));
                if (account is null)
                {
                    var role = pack.Definition.AccountRoles.FirstOrDefault(candidate =>
                        string.Equals(candidate.Key, template.DefaultRoleKey, StringComparison.OrdinalIgnoreCase));
                    account = new FinanceAccount(
                        Guid.NewGuid(),
                        command.CompanyId,
                        template.Code,
                        template.Label,
                        template.AccountClass,
                        preview.BaseCurrency,
                        0m,
                        ToUtc(command.FiscalYearStart),
                        nowUtc,
                        nowUtc,
                        template.AccountClass,
                        template.NormalBalance,
                        command.FiscalYearStart,
                        null,
                        isPostingEnabled: true,
                        role?.Key,
                        restrictManualPosting: role?.IsControlAccount == true);
                    accounts.Add(account);
                    _dbContext.FinanceAccounts.Add(account);
                }
                else
                {
                    var role = pack.Definition.AccountRoles.FirstOrDefault(candidate =>
                        string.Equals(candidate.Key, template.DefaultRoleKey, StringComparison.OrdinalIgnoreCase));
                    account.ApplyAccountingSemantics(
                        template.AccountClass,
                        template.NormalBalance,
                        account.EffectiveFrom ?? command.FiscalYearStart,
                        account.EffectiveTo,
                        isPostingEnabled: true,
                        role?.Key,
                        restrictManualPosting: role?.IsControlAccount == true,
                        nowUtc);
                }

                if (!mappings.Any(mapping => mapping.FinanceAccountId == account.Id && mapping.IsActive))
                {
                    var mapping = CreateDefaultStatementMapping(command.CompanyId, account, nowUtc);
                    mappings.Add(mapping);
                    _dbContext.FinancialStatementMappings.Add(mapping);
                }
            }

            var existingSeries = await _dbContext.VoucherSeries
                .Where(series => series.CompanyId == command.CompanyId)
                .ToListAsync(cancellationToken);
            foreach (var definition in DefaultVoucherSeries)
            {
                if (existingSeries.All(series => !string.Equals(series.Code, definition.Code, StringComparison.OrdinalIgnoreCase)))
                {
                    var series = new VoucherSeries(
                        Guid.NewGuid(),
                        command.CompanyId,
                        definition.Code,
                        definition.DisplayName,
                        definition.NumberPrefix,
                        isActive: true,
                        nowUtc);
                    existingSeries.Add(series);
                    _dbContext.VoucherSeries.Add(series);
                }
            }

            var expectedPeriods = BuildMonthlyPeriods(command.FiscalYearStart);
            var existingPeriods = await LoadPeriodsInFiscalYearAsync(command.CompanyId, command.FiscalYearStart, cancellationToken);
            foreach (var period in expectedPeriods)
            {
                if (existingPeriods.All(existing => !MatchesPeriod(period, existing)))
                {
                    _dbContext.FiscalPeriods.Add(CreatePeriodEntity(command.CompanyId, period, nowUtc));
                }
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            var accountByCode = accounts.ToDictionary(account => account.Code, account => account.Id, StringComparer.OrdinalIgnoreCase);
            var accountRoleIds = roleAssignments.ToDictionary(
                assignment => assignment.Key,
                assignment => accountByCode[assignment.Value],
                StringComparer.OrdinalIgnoreCase);
            var setupStatus = await _configurationService.CreateInitialAsync(
                new CreateInitialAccountingConfigurationCommand(
                    command.CompanyId,
                    preview.BaseCurrency,
                    command.FiscalYearStart.Month,
                    command.FiscalYearStart.Day,
                    pack.Definition.PackKey,
                    pack.Definition.Version,
                    command.FiscalYearStart,
                    2,
                    AccountingRoundingModeValues.MidpointToEven,
                    accountRoleIds,
                    command.ActorUserId,
                    command.CorrelationId),
                cancellationToken);

            await WriteAuditAsync(
                command.CompanyId,
                command.ActorUserId,
                AuditEventActions.AccountingFiscalYearCreated,
                AuditTargetTypes.FiscalPeriod,
                command.FiscalYearStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                "The initial fiscal year and monthly accounting periods were created.",
                command.CorrelationId,
                nowUtc,
                new Dictionary<string, string?>
                {
                    ["fiscalYearStart"] = command.FiscalYearStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["periodCount"] = expectedPeriods.Count.ToString(CultureInfo.InvariantCulture),
                    ["idempotencyKey"] = NormalizeOptional(command.IdempotencyKey, 128)
                },
                cancellationToken);
            await WriteAuditAsync(
                command.CompanyId,
                command.ActorUserId,
                AuditEventActions.AccountingSetupCompleted,
                AuditTargetTypes.AccountingConfiguration,
                setupStatus.Configuration!.Id.ToString("D"),
                "Native accounting setup was completed and validated.",
                command.CorrelationId,
                nowUtc,
                new Dictionary<string, string?>
                {
                    ["baseCurrency"] = preview.BaseCurrency,
                    ["chartTemplate"] = chart.Key,
                    ["accountCount"] = chart.Accounts.Count.ToString(CultureInfo.InvariantCulture),
                    ["voucherSeriesCount"] = DefaultVoucherSeries.Count.ToString(CultureInfo.InvariantCulture),
                    ["countryNeutral"] = pack.Definition.IsCountryNeutral ? "true" : "false",
                    ["idempotencyKey"] = NormalizeOptional(command.IdempotencyKey, 128)
                },
                cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new AccountingSetupCompletionDto(
                setupStatus,
                chart.Accounts.Count,
                expectedPeriods.Count,
                DefaultVoucherSeries.Count,
                WasAlreadyApplied: false);
            }
            catch (Exception exception) when (exception is DbUpdateException or AccountingConfigurationException)
            {
                await transaction.RollbackAsync(cancellationToken);
                _dbContext.ChangeTracker.Clear();
                if (await MatchesCompletedSetupAsync(command, cancellationToken))
                {
                    return await BuildCompletionAsync(command.CompanyId, wasAlreadyApplied: true, cancellationToken);
                }

                if (exception is AccountingConfigurationException configurationException &&
                    configurationException.ReasonCode != AccountingConfigurationReasonCodes.ConfigurationAlreadyExists)
                {
                    throw;
                }

                throw SetupConflict("Accounting setup conflicted with another change. Reload the setup preview and try again.");
            }
        });
    }

    public async Task<IReadOnlyList<AccountingAccountListItemDto>> GetAccountsAsync(
        GetAccountingAccountsQuery query,
        CancellationToken cancellationToken)
    {
        ValidateCompanyId(query.CompanyId);
        var accountsQuery = _dbContext.FinanceAccounts
            .AsNoTracking()
            .Where(account => account.CompanyId == query.CompanyId && account.AccountClass != null && account.NormalBalance != null);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            accountsQuery = accountsQuery.Where(account => account.Code.Contains(search) || account.Name.Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(query.AccountClass))
        {
            var accountClass = FinanceAccountClassValues.NormalizeOptional(query.AccountClass);
            accountsQuery = accountsQuery.Where(account => account.AccountClass == accountClass);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            accountsQuery = query.Status.Trim().ToLowerInvariant() switch
            {
                "active" => accountsQuery.Where(account => account.IsPostingEnabled),
                "inactive" => accountsQuery.Where(account => !account.IsPostingEnabled),
                _ => throw new ArgumentOutOfRangeException(nameof(query.Status), "Account status must be active or inactive.")
            };
        }

        var accounts = await accountsQuery.OrderBy(account => account.Code).ToListAsync(cancellationToken);
        var context = await LoadAccountPresentationContextAsync(query.CompanyId, accounts.Select(account => account.Id), cancellationToken);
        return accounts.Select(account => BuildAccountListItem(account, context)).ToArray();
    }

    public async Task<AccountingAccountDetailDto> GetAccountAsync(
        GetAccountingAccountQuery query,
        CancellationToken cancellationToken)
    {
        ValidateCompanyId(query.CompanyId);
        var account = await _dbContext.FinanceAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == query.CompanyId && item.Id == query.AccountId, cancellationToken)
            ?? throw MissingAccount();
        var context = await LoadAccountPresentationContextAsync(query.CompanyId, [account.Id], cancellationToken);
        return BuildAccountDetail(account, context);
    }

    public async Task<AccountingAccountDetailDto> CreateAccountAsync(
        CreateAccountingAccountCommand command,
        CancellationToken cancellationToken)
    {
        ValidateCompanyId(command.CompanyId);
        ValidateActor(command.ActorUserId);
        var configuration = await RequireConfigurationAsync(command.CompanyId, cancellationToken);
        var accountClass = FinanceAccountClassValues.NormalizeOptional(command.AccountClass)
            ?? throw new ArgumentException("Account class is required.", nameof(command.AccountClass));
        var normalBalance = FinanceNormalBalanceValues.NormalizeOptional(command.NormalBalance)
            ?? throw new ArgumentException("Normal balance is required.", nameof(command.NormalBalance));
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (await _dbContext.FinanceAccounts.AnyAsync(
                account => account.CompanyId == command.CompanyId && account.Code == command.Code.Trim(),
                cancellationToken))
        {
            throw new AccountingConfigurationException(
                AccountingConfigurationReasonCodes.AccountCodeConflict,
                "An account with this code already exists.",
                isConflict: true);
        }

        var account = new FinanceAccount(
            Guid.NewGuid(),
            command.CompanyId,
            command.Code,
            command.Name,
            accountClass,
            configuration.BaseCurrency,
            0m,
            ToUtc(command.EffectiveFrom),
            nowUtc,
            nowUtc,
            accountClass,
            normalBalance,
            command.EffectiveFrom,
            null,
            isPostingEnabled: true);
        _dbContext.FinanceAccounts.Add(account);
        _dbContext.FinancialStatementMappings.Add(CreateDefaultStatementMapping(command.CompanyId, account, nowUtc));
        await WriteAuditAsync(
            command.CompanyId,
            command.ActorUserId,
            AuditEventActions.AccountingAccountCreated,
            AuditTargetTypes.FinanceAccount,
            account.Id.ToString("D"),
            $"Account {account.Code} was created for future postings.",
            command.CorrelationId,
            nowUtc,
            new Dictionary<string, string?>
            {
                ["code"] = account.Code,
                ["name"] = account.Name,
                ["accountClass"] = account.AccountClass
            },
            cancellationToken);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new AccountingConfigurationException(
                AccountingConfigurationReasonCodes.AccountCodeConflict,
                "An account with this code already exists.",
                isConflict: true);
        }

        return await GetAccountAsync(new GetAccountingAccountQuery(command.CompanyId, account.Id), cancellationToken);
    }

    public async Task<AccountingAccountDetailDto> RenameAccountAsync(
        RenameAccountingAccountCommand command,
        CancellationToken cancellationToken)
    {
        ValidateCompanyId(command.CompanyId);
        ValidateActor(command.ActorUserId);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var account = await LoadAccountForMutationAsync(command.CompanyId, command.AccountId, cancellationToken);
        EnsureExpectedVersion(account, command.ExpectedUpdatedUtc);
        var previousName = account.Name;
        account.Rename(command.Name, nowUtc);
        await WriteAuditAsync(
            command.CompanyId,
            command.ActorUserId,
            AuditEventActions.AccountingAccountRenamed,
            AuditTargetTypes.FinanceAccount,
            account.Id.ToString("D"),
            $"Account {account.Code} was renamed.",
            command.CorrelationId,
            nowUtc,
            new Dictionary<string, string?> { ["previousName"] = previousName, ["name"] = account.Name },
            cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAccountAsync(new GetAccountingAccountQuery(command.CompanyId, account.Id), cancellationToken);
    }

    public async Task<AccountingAccountDetailDto> DeactivateAccountAsync(
        DeactivateAccountingAccountCommand command,
        CancellationToken cancellationToken)
    {
        ValidateCompanyId(command.CompanyId);
        ValidateActor(command.ActorUserId);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var account = await LoadAccountForMutationAsync(command.CompanyId, command.AccountId, cancellationToken);
        EnsureExpectedVersion(account, command.ExpectedUpdatedUtc);

        var configuredRole = await _dbContext.AccountingConfigurationAccountRoles
            .AsNoTracking()
            .AnyAsync(role => role.CompanyId == command.CompanyId && role.FinanceAccountId == account.Id, cancellationToken);
        if (configuredRole || !string.IsNullOrWhiteSpace(account.ControlAccountRole))
        {
            throw new AccountingConfigurationException(
                AccountingConfigurationReasonCodes.AccountProtected,
                "This account is required by the accounting setup. Assign the role to another compatible account before deactivating it.",
                isConflict: true);
        }

        var hasPostedHistory = await HasPostedHistoryAsync(command.CompanyId, account.Id, cancellationToken);
        if (hasPostedHistory)
        {
            throw new AccountingConfigurationException(
                AccountingConfigurationReasonCodes.AccountHasPostedHistory,
                "This account has posted history and cannot be deactivated. Create a replacement account for future postings.",
                isConflict: true);
        }

        account.Deactivate(command.EffectiveTo, nowUtc);
        await WriteAuditAsync(
            command.CompanyId,
            command.ActorUserId,
            AuditEventActions.AccountingAccountDeactivated,
            AuditTargetTypes.FinanceAccount,
            account.Id.ToString("D"),
            $"Account {account.Code} was deactivated for future postings.",
            command.CorrelationId,
            nowUtc,
            new Dictionary<string, string?> { ["effectiveTo"] = command.EffectiveTo.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
            cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAccountAsync(new GetAccountingAccountQuery(command.CompanyId, account.Id), cancellationToken);
    }

    public async Task<IReadOnlyList<AccountingFiscalYearDto>> GetFiscalYearsAsync(
        GetAccountingPeriodsQuery query,
        CancellationToken cancellationToken)
    {
        ValidateCompanyId(query.CompanyId);
        var configuration = await RequireConfigurationAsync(query.CompanyId, cancellationToken);
        var periods = await _dbContext.FiscalPeriods
            .AsNoTracking()
            .Where(period => period.CompanyId == query.CompanyId)
            .OrderBy(period => period.StartUtc)
            .ToListAsync(cancellationToken);

        return periods
            .GroupBy(period => ResolveFiscalYearStart(DateOnly.FromDateTime(period.StartUtc), configuration.FiscalYearStartMonth, configuration.FiscalYearStartDay))
            .OrderByDescending(group => group.Key)
            .Select(group => BuildFiscalYear(group.Key, group.OrderBy(period => period.StartUtc).ToArray()))
            .ToArray();
    }

    public async Task<AccountingPeriodDto> GetPeriodAsync(
        GetAccountingPeriodQuery query,
        CancellationToken cancellationToken)
    {
        ValidateCompanyId(query.CompanyId);
        var period = await _dbContext.FiscalPeriods
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == query.CompanyId && item.Id == query.PeriodId, cancellationToken)
            ?? throw new AccountingConfigurationException(
                AccountingConfigurationReasonCodes.PeriodNotFound,
                "The accounting period was not found.");
        return MapPeriod(period);
    }

    public async Task<AccountingFiscalYearPreviewDto> PreviewFiscalYearAsync(
        PreviewAccountingFiscalYearQuery query,
        CancellationToken cancellationToken)
    {
        ValidateCompanyId(query.CompanyId);
        await RequireConfigurationAsync(query.CompanyId, cancellationToken);
        var periods = BuildMonthlyPeriods(query.FiscalYearStart);
        var existing = await LoadPeriodsInFiscalYearAsync(query.CompanyId, query.FiscalYearStart, cancellationToken);
        var issues = new List<AccountingConfigurationIssueDto>();
        AddPeriodConflicts(periods, existing, issues);
        return new AccountingFiscalYearPreviewDto(
            query.CompanyId,
            query.FiscalYearStart,
            query.FiscalYearStart.AddYears(1).AddDays(-1),
            issues.All(issue => !issue.IsBlocking),
            periods,
            issues);
    }

    public async Task<AccountingFiscalYearCreationDto> CreateFiscalYearAsync(
        CreateAccountingFiscalYearCommand command,
        CancellationToken cancellationToken)
    {
        ValidateCompanyId(command.CompanyId);
        ValidateActor(command.ActorUserId);
        var preview = await PreviewFiscalYearAsync(
            new PreviewAccountingFiscalYearQuery(command.CompanyId, command.FiscalYearStart),
            cancellationToken);
        var blocking = preview.Issues.FirstOrDefault(issue => issue.IsBlocking);
        if (blocking is not null)
        {
            throw new AccountingConfigurationException(blocking.ReasonCode, blocking.Explanation, isConflict: true);
        }

        var existing = await LoadPeriodsInFiscalYearAsync(command.CompanyId, command.FiscalYearStart, cancellationToken);
        var missingPeriods = preview.Periods
            .Where(period => existing.All(item => !MatchesPeriod(period, item)))
            .ToArray();
        if (missingPeriods.Length == 0)
        {
            return new AccountingFiscalYearCreationDto(BuildFiscalYear(command.FiscalYearStart, existing), WasAlreadyPresent: true);
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        foreach (var period in missingPeriods)
        {
            _dbContext.FiscalPeriods.Add(CreatePeriodEntity(command.CompanyId, period, nowUtc));
        }

        await WriteAuditAsync(
            command.CompanyId,
            command.ActorUserId,
            AuditEventActions.AccountingFiscalYearCreated,
            AuditTargetTypes.FiscalPeriod,
            command.FiscalYearStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            "A fiscal year and its monthly accounting periods were created.",
            command.CorrelationId,
            nowUtc,
            new Dictionary<string, string?>
            {
                ["fiscalYearStart"] = command.FiscalYearStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["periodCount"] = missingPeriods.Length.ToString(CultureInfo.InvariantCulture),
                ["idempotencyKey"] = NormalizeOptional(command.IdempotencyKey, 128)
            },
            cancellationToken);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            _dbContext.ChangeTracker.Clear();
            existing = await LoadPeriodsInFiscalYearAsync(command.CompanyId, command.FiscalYearStart, cancellationToken);
            if (preview.Periods.All(period => existing.Any(item => MatchesPeriod(period, item))))
            {
                return new AccountingFiscalYearCreationDto(BuildFiscalYear(command.FiscalYearStart, existing), WasAlreadyPresent: true);
            }

            throw SetupConflict("The fiscal year conflicted with another change. Reload periods and try again.");
        }

        var created = await LoadPeriodsInFiscalYearAsync(command.CompanyId, command.FiscalYearStart, cancellationToken);
        return new AccountingFiscalYearCreationDto(BuildFiscalYear(command.FiscalYearStart, created), WasAlreadyPresent: false);
    }

    private async Task<AccountingSetupCompletionDto> BuildCompletionAsync(
        Guid companyId,
        bool wasAlreadyApplied,
        CancellationToken cancellationToken)
    {
        var status = await _configurationService.GetSetupStatusAsync(new GetAccountingSetupStatusQuery(companyId), cancellationToken);
        var accountCount = await _dbContext.FinanceAccounts.CountAsync(account => account.CompanyId == companyId && account.AccountClass != null, cancellationToken);
        var periodCount = await _dbContext.FiscalPeriods.CountAsync(period => period.CompanyId == companyId, cancellationToken);
        var seriesCount = await _dbContext.VoucherSeries.CountAsync(series => series.CompanyId == companyId, cancellationToken);
        return new AccountingSetupCompletionDto(status, accountCount, periodCount, seriesCount, wasAlreadyApplied);
    }

    private async Task<bool> MatchesCompletedSetupAsync(
        CompleteAccountingSetupCommand command,
        CancellationToken cancellationToken)
    {
        var configuration = await _dbContext.AccountingConfigurations
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == command.CompanyId, cancellationToken);
        if (configuration is null ||
            !string.Equals(configuration.BaseCurrency, NormalizeCurrency(command.BaseCurrency), StringComparison.OrdinalIgnoreCase) ||
            configuration.FiscalYearStartMonth != command.FiscalYearStart.Month ||
            configuration.FiscalYearStartDay != command.FiscalYearStart.Day ||
            !string.Equals(configuration.PolicyPackKey, command.PolicyPackKey, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(configuration.PolicyPackVersion, command.PolicyPackVersion, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var pack = _packResolver.Resolve(command.PolicyPackKey, command.PolicyPackVersion);
        var templateChart = ResolveChart(pack, command.ChartTemplateKey);
        var chart = await ResolveSetupChartAsync(
            command.CompanyId,
            NormalizeCurrency(command.BaseCurrency),
            templateChart,
            warnings: null,
            cancellationToken);
        var codes = chart.Accounts.Select(account => account.Code).ToArray();
        var accountCount = await _dbContext.FinanceAccounts.CountAsync(
            account => account.CompanyId == command.CompanyId && codes.Contains(account.Code),
            cancellationToken);
        var periods = await LoadPeriodsInFiscalYearAsync(command.CompanyId, command.FiscalYearStart, cancellationToken);
        var seriesCodes = DefaultVoucherSeries.Select(series => series.Code).ToArray();
        var seriesCount = await _dbContext.VoucherSeries.CountAsync(
            series => series.CompanyId == command.CompanyId && seriesCodes.Contains(series.Code),
            cancellationToken);
        return accountCount == chart.Accounts.Count &&
               BuildMonthlyPeriods(command.FiscalYearStart).All(period => periods.Any(item => MatchesPeriod(period, item))) &&
               seriesCount == DefaultVoucherSeries.Count;
    }

    private async Task<AccountingConfiguration> RequireConfigurationAsync(Guid companyId, CancellationToken cancellationToken) =>
        await _dbContext.AccountingConfigurations
            .AsNoTracking()
            .SingleOrDefaultAsync(configuration => configuration.CompanyId == companyId, cancellationToken)
        ?? throw new AccountingConfigurationException(
            AccountingConfigurationReasonCodes.ConfigurationNotFound,
            "Complete accounting setup before administering accounts or periods.");

    private async Task<FinanceAccount> LoadAccountForMutationAsync(Guid companyId, Guid accountId, CancellationToken cancellationToken) =>
        await _dbContext.FinanceAccounts.SingleOrDefaultAsync(
            account => account.CompanyId == companyId && account.Id == accountId,
            cancellationToken) ?? throw MissingAccount();

    private static void EnsureExpectedVersion(FinanceAccount account, DateTime expectedUpdatedUtc)
    {
        if (expectedUpdatedUtc == default || account.UpdatedUtc != expectedUpdatedUtc)
        {
            throw new AccountingConfigurationException(
                AccountingConfigurationReasonCodes.ConcurrencyConflict,
                "This account changed after it was loaded. Reload the account and try again.",
                isConflict: true);
        }
    }

    private async Task<AccountPresentationContext> LoadAccountPresentationContextAsync(
        Guid companyId,
        IEnumerable<Guid> accountIds,
        CancellationToken cancellationToken)
    {
        var ids = accountIds.Distinct().ToArray();
        var postedIds = await _dbContext.LedgerEntryLines
            .AsNoTracking()
            .Where(line => line.CompanyId == companyId && ids.Contains(line.FinanceAccountId) && line.LedgerEntry.Status == LedgerEntryStatuses.Posted)
            .Select(line => line.FinanceAccountId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var roles = await _dbContext.AccountingConfigurationAccountRoles
            .AsNoTracking()
            .Where(role => role.CompanyId == companyId && ids.Contains(role.FinanceAccountId))
            .ToListAsync(cancellationToken);
        var mappings = await _dbContext.FinancialStatementMappings
            .AsNoTracking()
            .Where(mapping => mapping.CompanyId == companyId && ids.Contains(mapping.FinanceAccountId) && mapping.IsActive)
            .ToListAsync(cancellationToken);
        var configuration = await _dbContext.AccountingConfigurations.AsNoTracking().SingleOrDefaultAsync(item => item.CompanyId == companyId, cancellationToken);
        var roleNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (configuration is not null && _packResolver.TryResolve(configuration.PolicyPackKey, configuration.PolicyPackVersion, out var pack) && pack is not null)
        {
            foreach (var role in pack.Definition.AccountRoles)
            {
                roleNames[role.Key] = role.DisplayName;
            }
        }

        return new AccountPresentationContext(postedIds.ToHashSet(), roles, mappings, roleNames);
    }

    private static AccountingAccountListItemDto BuildAccountListItem(FinanceAccount account, AccountPresentationContext context)
    {
        var protection = ResolveProtection(account, context);
        return new AccountingAccountListItemDto(
            account.Id,
            account.Code,
            account.Name,
            DisplayAccountClass(account.AccountClass),
            DisplayNormalBalance(account.NormalBalance),
            account.Currency,
            account.EffectiveFrom,
            account.EffectiveTo,
            account.IsPostingEnabled,
            context.PostedAccountIds.Contains(account.Id),
            protection.IsProtected,
            protection.Reason,
            ResolveRoleName(account, context),
            ResolveReportingPlacement(account.Id, context.Mappings),
            account.UpdatedUtc);
    }

    private static AccountingAccountDetailDto BuildAccountDetail(FinanceAccount account, AccountPresentationContext context)
    {
        var protection = ResolveProtection(account, context);
        return new AccountingAccountDetailDto(
            account.Id,
            account.Code,
            account.Name,
            DisplayAccountClass(account.AccountClass),
            DisplayNormalBalance(account.NormalBalance),
            account.Currency,
            account.EffectiveFrom,
            account.EffectiveTo,
            account.IsPostingEnabled,
            account.RestrictManualPosting,
            context.PostedAccountIds.Contains(account.Id),
            protection.IsProtected,
            protection.Reason,
            ResolveRoleName(account, context),
            ResolveReportingPlacement(account.Id, context.Mappings),
            account.CreatedUtc,
            account.UpdatedUtc);
    }

    private static (bool IsProtected, string? Reason) ResolveProtection(FinanceAccount account, AccountPresentationContext context)
    {
        if (context.PostedAccountIds.Contains(account.Id))
        {
            return (true, "This account has posted history and cannot be deleted or deactivated.");
        }

        if (context.Roles.Any(role => role.FinanceAccountId == account.Id) || !string.IsNullOrWhiteSpace(account.ControlAccountRole))
        {
            return (true, "This account is required by the accounting setup. Assign its role elsewhere before deactivating it.");
        }

        return (false, null);
    }

    private static string? ResolveRoleName(FinanceAccount account, AccountPresentationContext context)
    {
        var roleKey = context.Roles.FirstOrDefault(role => role.FinanceAccountId == account.Id)?.RoleKey ?? account.ControlAccountRole;
        return string.IsNullOrWhiteSpace(roleKey)
            ? null
            : context.RoleNames.GetValueOrDefault(roleKey) ?? Humanize(roleKey);
    }

    private static string? ResolveReportingPlacement(Guid accountId, IReadOnlyList<FinancialStatementMapping> mappings)
    {
        var mapping = mappings.FirstOrDefault(item => item.FinanceAccountId == accountId && item.IsActive);
        return mapping is null
            ? null
            : $"{DisplayStatement(mapping.StatementType)} — {Humanize(mapping.ReportSection.ToStorageValue())}";
    }

    private async Task<bool> HasPostedHistoryAsync(Guid companyId, Guid accountId, CancellationToken cancellationToken) =>
        await _dbContext.LedgerEntryLines.AsNoTracking().AnyAsync(
            line => line.CompanyId == companyId && line.FinanceAccountId == accountId && line.LedgerEntry.Status == LedgerEntryStatuses.Posted,
            cancellationToken);

    private static AccountingChartTemplateDefinition ResolveChart(IAccountingPolicyPack pack, string chartTemplateKey) =>
        pack.Definition.ChartTemplates.FirstOrDefault(template =>
            string.Equals(template.Key, chartTemplateKey, StringComparison.OrdinalIgnoreCase))
        ?? throw new AccountingConfigurationException(
            AccountingConfigurationReasonCodes.InvalidChartTemplate,
            "The selected chart template is not available for this accounting policy.");

    private async Task<AccountingChartTemplateDefinition> ResolveSetupChartAsync(
        Guid companyId,
        string currency,
        AccountingChartTemplateDefinition template,
        List<AccountingConfigurationIssueDto>? warnings,
        CancellationToken cancellationToken)
    {
        var existingAccounts = await _dbContext.FinanceAccounts
            .AsNoTracking()
            .Where(account => account.CompanyId == companyId)
            .ToListAsync(cancellationToken);
        var accountsByCode = existingAccounts.ToDictionary(account => account.Code, StringComparer.OrdinalIgnoreCase);
        var resolvedAccounts = new List<AccountingChartAccountTemplate>(template.Accounts.Count);

        foreach (var templateAccount in template.Accounts)
        {
            if (!accountsByCode.TryGetValue(templateAccount.Code, out var existing) ||
                IsCompatibleSetupAccount(existing, templateAccount, currency))
            {
                resolvedAccounts.Add(templateAccount);
                continue;
            }

            var resolvedCode = ResolveAvailableSetupCode(templateAccount, currency, accountsByCode);
            resolvedAccounts.Add(templateAccount with { Code = resolvedCode });
            warnings?.Add(new AccountingConfigurationIssueDto(
                AccountingConfigurationReasonCodes.SetupConflict,
                $"Account {templateAccount.Code} is already used by {existing.Name}. It will be preserved, and {templateAccount.Label} will use account {resolvedCode}.",
                templateAccount.Code,
                IsBlocking: false));
        }

        return template with { Accounts = resolvedAccounts };
    }

    private static string ResolveAvailableSetupCode(
        AccountingChartAccountTemplate template,
        string currency,
        IReadOnlyDictionary<string, FinanceAccount> accountsByCode)
    {
        var baseCode = $"VC-{template.Code}";
        for (var suffix = 1; ; suffix++)
        {
            var candidate = suffix == 1 ? baseCode : $"{baseCode}-{suffix}";
            if (!accountsByCode.TryGetValue(candidate, out var existing) ||
                IsCompatibleSetupAccount(existing, template, currency))
            {
                return candidate;
            }
        }
    }

    private static bool IsCompatibleSetupAccount(
        FinanceAccount account,
        AccountingChartAccountTemplate template,
        string currency)
    {
        if (!string.Equals(account.Currency, currency, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var expectedClass = FinanceAccountClassValues.NormalizeOptional(template.AccountClass);
        var existingClass = FinanceAccountClassValues.NormalizeOptional(account.AccountClass);
        if (existingClass is null)
        {
            existingClass = TryNormalizeAccountClass(account.AccountType);
        }

        var expectedBalance = FinanceNormalBalanceValues.NormalizeOptional(template.NormalBalance);
        return string.Equals(existingClass, expectedClass, StringComparison.OrdinalIgnoreCase) &&
               (account.NormalBalance is null ||
                string.Equals(account.NormalBalance, expectedBalance, StringComparison.OrdinalIgnoreCase));
    }

    private static string? TryNormalizeAccountClass(string? value)
    {
        try
        {
            return FinanceAccountClassValues.NormalizeOptional(value);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static Dictionary<string, string> BuildRoleAssignments(
        IAccountingPolicyPack pack,
        AccountingChartTemplateDefinition chart,
        IReadOnlyDictionary<string, string>? overrides,
        List<AccountingConfigurationIssueDto> issues)
    {
        var assignments = chart.Accounts
            .Where(account => !string.IsNullOrWhiteSpace(account.DefaultRoleKey))
            .ToDictionary(account => account.DefaultRoleKey!, account => account.Code, StringComparer.OrdinalIgnoreCase);
        if (overrides is not null)
        {
            foreach (var assignment in overrides)
            {
                assignments[assignment.Key.Trim()] = assignment.Value.Trim();
            }
        }

        foreach (var role in pack.Definition.AccountRoles.Where(role => role.IsRequired))
        {
            if (!assignments.TryGetValue(role.Key, out var accountCode) || string.IsNullOrWhiteSpace(accountCode))
            {
                issues.Add(new AccountingConfigurationIssueDto(
                    AccountingConfigurationReasonCodes.MissingRequiredAccountRole,
                    $"Choose an account for {role.DisplayName}.",
                    role.Key));
                continue;
            }

            if (chart.Accounts.All(account => !string.Equals(account.Code, accountCode, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(new AccountingConfigurationIssueDto(
                    AccountingConfigurationReasonCodes.InvalidAccountRole,
                    $"The selected account for {role.DisplayName} is not in this chart template.",
                    role.Key));
            }
        }

        return assignments;
    }

    private static FinancialStatementMapping CreateDefaultStatementMapping(Guid companyId, FinanceAccount account, DateTime nowUtc)
    {
        var accountClass = FinanceAccountClassValues.NormalizeOptional(account.AccountClass)
            ?? throw new InvalidOperationException("An accounting account requires a class before it can be mapped.");
        var (statement, section, line) = accountClass switch
        {
            FinanceAccountClassValues.Asset => (FinancialStatementType.BalanceSheet, FinancialStatementReportSection.BalanceSheetAssets, FinancialStatementLineClassification.CurrentAsset),
            FinanceAccountClassValues.Liability => (FinancialStatementType.BalanceSheet, FinancialStatementReportSection.BalanceSheetLiabilities, FinancialStatementLineClassification.CurrentLiability),
            FinanceAccountClassValues.Equity => (FinancialStatementType.BalanceSheet, FinancialStatementReportSection.BalanceSheetEquity, FinancialStatementLineClassification.Equity),
            FinanceAccountClassValues.Income => (FinancialStatementType.ProfitAndLoss, FinancialStatementReportSection.ProfitAndLossRevenue, FinancialStatementLineClassification.Revenue),
            FinanceAccountClassValues.Expense => (FinancialStatementType.ProfitAndLoss, FinancialStatementReportSection.ProfitAndLossOperatingExpenses, FinancialStatementLineClassification.OperatingExpense),
            _ => throw new ArgumentOutOfRangeException(nameof(account.AccountClass), "Account class is not supported for financial reporting.")
        };
        return new FinancialStatementMapping(Guid.NewGuid(), companyId, account.Id, statement, section, line, true, nowUtc, nowUtc);
    }

    private static IReadOnlyList<AccountingSetupPeriodPreviewDto> BuildMonthlyPeriods(DateOnly fiscalYearStart)
    {
        var periods = new List<AccountingSetupPeriodPreviewDto>(12);
        for (var index = 0; index < 12; index++)
        {
            var start = fiscalYearStart.AddMonths(index);
            var endExclusive = fiscalYearStart.AddMonths(index + 1);
            periods.Add(new AccountingSetupPeriodPreviewDto(
                start.ToString("MMM yyyy", CultureInfo.InvariantCulture),
                start,
                endExclusive.AddDays(-1)));
        }

        return periods;
    }

    private static void AddPeriodConflicts(
        IReadOnlyList<AccountingSetupPeriodPreviewDto> expected,
        IReadOnlyList<FiscalPeriod> existing,
        List<AccountingConfigurationIssueDto> issues)
    {
        foreach (var period in existing)
        {
            if (expected.Any(item => MatchesPeriod(item, period)))
            {
                continue;
            }

            issues.Add(new AccountingConfigurationIssueDto(
                AccountingConfigurationReasonCodes.PeriodOverlap,
                $"The proposed fiscal year overlaps the existing period {period.Name}. Choose dates that do not overlap.",
                period.Id.ToString("D")));
        }
    }

    private async Task<List<FiscalPeriod>> LoadPeriodsInFiscalYearAsync(
        Guid companyId,
        DateOnly fiscalYearStart,
        CancellationToken cancellationToken)
    {
        var startUtc = ToUtc(fiscalYearStart);
        var endUtc = ToUtc(fiscalYearStart.AddYears(1));
        return await _dbContext.FiscalPeriods
            .AsNoTracking()
            .Where(period => period.CompanyId == companyId && period.StartUtc < endUtc && period.EndUtc > startUtc)
            .OrderBy(period => period.StartUtc)
            .ToListAsync(cancellationToken);
    }

    private static FiscalPeriod CreatePeriodEntity(Guid companyId, AccountingSetupPeriodPreviewDto period, DateTime nowUtc) =>
        new(
            Guid.NewGuid(),
            companyId,
            period.Name,
            ToUtc(period.StartDate),
            ToUtc(period.EndDate.AddDays(1)),
            createdUtc: nowUtc,
            updatedUtc: nowUtc);

    private static bool MatchesPeriod(AccountingSetupPeriodPreviewDto expected, FiscalPeriod actual) =>
        actual.StartUtc == ToUtc(expected.StartDate) && actual.EndUtc == ToUtc(expected.EndDate.AddDays(1));

    private static AccountingFiscalYearDto BuildFiscalYear(DateOnly startDate, IReadOnlyList<FiscalPeriod> periods)
    {
        var mapped = periods.OrderBy(period => period.StartUtc).Select(MapPeriod).ToArray();
        return new AccountingFiscalYearDto(
            startDate,
            startDate.AddYears(1).AddDays(-1),
            mapped.Count(period => !period.IsClosed && !period.IsReportingLocked),
            mapped.Count(period => period.IsClosed),
            mapped.Count(period => period.IsReportingLocked),
            mapped);
    }

    private static AccountingPeriodDto MapPeriod(FiscalPeriod period) =>
        new(
            period.Id,
            period.Name,
            DateOnly.FromDateTime(period.StartUtc),
            DateOnly.FromDateTime(period.EndUtc).AddDays(-1),
            period.IsClosed,
            period.IsReportingLocked,
            period.ClosedUtc,
            period.ReportingLockedUtc,
            period.LastCloseValidatedUtc,
            period.CreatedUtc,
            period.UpdatedUtc);

    private static DateOnly ResolveFiscalYearStart(DateOnly date, int startMonth, int startDay)
    {
        var day = Math.Min(startDay, DateTime.DaysInMonth(date.Year, startMonth));
        var boundary = new DateOnly(date.Year, startMonth, day);
        if (date < boundary)
        {
            var previousDay = Math.Min(startDay, DateTime.DaysInMonth(date.Year - 1, startMonth));
            return new DateOnly(date.Year - 1, startMonth, previousDay);
        }

        return boundary;
    }

    private static string ResolveReportingPlacement(IAccountingPolicyPack pack, string accountClass)
    {
        var mapping = pack.Definition.ReportingMappings.FirstOrDefault(item =>
            string.Equals(item.AccountClass, accountClass, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(FinanceAccountClassValues.NormalizeOptional(item.AccountClass), FinanceAccountClassValues.NormalizeOptional(accountClass), StringComparison.OrdinalIgnoreCase));
        return mapping is null ? "Not mapped" : $"{Humanize(mapping.Statement)} — {Humanize(mapping.SectionKey)}";
    }

    private static string DisplayAccountClass(string? value) =>
        FinanceAccountClassValues.NormalizeOptional(value) switch
        {
            FinanceAccountClassValues.Asset => "Asset",
            FinanceAccountClassValues.Liability => "Liability",
            FinanceAccountClassValues.Equity => "Equity",
            FinanceAccountClassValues.Income => "Income",
            FinanceAccountClassValues.Expense => "Expense",
            _ => "Not classified"
        };

    private static string DisplayNormalBalance(string? value) =>
        FinanceNormalBalanceValues.NormalizeOptional(value) switch
        {
            FinanceNormalBalanceValues.Debit => "Debit",
            FinanceNormalBalanceValues.Credit => "Credit",
            _ => "Not set"
        };

    private static string DisplayStatement(FinancialStatementType type) =>
        type switch
        {
            FinancialStatementType.BalanceSheet => "Balance sheet",
            FinancialStatementType.ProfitAndLoss => "Profit and loss",
            FinancialStatementType.CashFlow => "Cash flow",
            _ => "Financial statement"
        };

    private static string Humanize(string value) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.Replace('_', ' ').Replace('-', ' ').ToLowerInvariant());

    private static string NormalizeCurrency(string value)
    {
        var currency = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();
        if (currency.Length != 3 || currency.Any(character => !char.IsAsciiLetter(character)))
        {
            throw new ArgumentException("Base currency must be a three-letter currency code.", nameof(value));
        }

        return currency;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static DateTime ToUtc(DateOnly value) => value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

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
            throw new UnauthorizedAccessException("A resolved company user is required for accounting administration.");
        }
    }

    private static AccountingConfigurationException MissingAccount() =>
        new(AccountingConfigurationReasonCodes.AccountNotFound, "The accounting account was not found.");

    private static AccountingConfigurationException SetupConflict(string message) =>
        new(AccountingConfigurationReasonCodes.SetupConflict, message, isConflict: true);

    private Task WriteAuditAsync(
        Guid companyId,
        Guid actorUserId,
        string action,
        string targetType,
        string targetId,
        string rationale,
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
                targetType,
                targetId,
                AuditEventOutcomes.Succeeded,
                rationale,
                DataSources: ["native_accounting"],
                Metadata: metadata,
                CorrelationId: correlationId,
                OccurredUtc: occurredUtc),
            cancellationToken);

    private sealed record AccountPresentationContext(
        HashSet<Guid> PostedAccountIds,
        IReadOnlyList<AccountingConfigurationAccountRole> Roles,
        IReadOnlyList<FinancialStatementMapping> Mappings,
        IReadOnlyDictionary<string, string> RoleNames);
}
