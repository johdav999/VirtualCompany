using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingProviderSwitchInternalReadinessPolicy(
    VirtualCompanyDbContext db,
    IAccountingConfigurationService accountingConfigurationService,
    IAccountingProviderSwitchRehearsalService rehearsalService,
    IAccountingPolicyPackResolver policyPackResolver)
    : IAccountingProviderSwitchInternalReadinessPolicy
{
    public async Task<AccountingProviderSwitchInternalReadinessDto> EvaluateAsync(
        EvaluateAccountingProviderSwitchInternalReadinessQuery query,
        CancellationToken cancellationToken)
    {
        if (query.CompanyId == Guid.Empty || query.SwitchId == Guid.Empty)
            throw new ArgumentException("Company and accounting-system switch are required.");

        var providerSwitch = await db.AccountingProviderSwitches.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId && x.Id == query.SwitchId, cancellationToken)
            ?? throw new AccountingAuthorityException(AccountingProviderSwitchReasonCodes.NotFound,
                "The accounting-system switch was not found for this company.");
        var checks = new List<AccountingProviderSwitchReadinessCheckDto>();

        Add(checks, "migration_direction",
            providerSwitch.TargetKind == AccountingProviderEndpointKinds.Internal &&
            providerSwitch.SourceKind == AccountingProviderEndpointKinds.External,
            true,
            providerSwitch.TargetKind != AccountingProviderEndpointKinds.Internal
                ? AccountingProviderSwitchPreparationReasonCodes.TargetMustBeInternal
                : AccountingProviderSwitchPreparationReasonCodes.SourceMustBeExternal,
            "Preparation requires an external source and Virtual Company as the target.",
            new { providerSwitch.SourceKind, providerSwitch.SourceProviderKey, providerSwitch.TargetKind });

        var planReadiness = await rehearsalService.GetPlanReadinessAsync(
            new(query.CompanyId, query.SwitchId, query.PlanId), cancellationToken);
        var planReady = planReadiness.IsReady && planReadiness.Plan is not null;
        Add(checks, "approved_current_plan", planReady, true,
            planReady ? null : planReadiness.BlockingReasonCode == AccountingProviderSwitchRehearsalReasonCodes.PlanStale
                ? AccountingProviderSwitchPreparationReasonCodes.PlanStale
                : AccountingProviderSwitchPreparationReasonCodes.PlanNotApproved,
            planReadiness.Explanation,
            new { planId = planReadiness.Plan?.Id, planHash = planReadiness.Plan?.PlanHash,
                approvalStatus = planReadiness.Plan?.ApprovalStatus, isCurrent = planReadiness.Plan?.IsCurrent });

        var setup = await accountingConfigurationService.ValidateAsync(
            new ValidateAccountingConfigurationQuery(query.CompanyId), cancellationToken);
        Add(checks, "accounting_configuration", setup.IsConfigured, true,
            setup.IsConfigured ? null : AccountingProviderSwitchPreparationReasonCodes.ConfigurationMissing,
            setup.IsConfigured ? "Accounting configuration exists." : "Complete accounting configuration before preparing native records.",
            new { setup.IsConfigured, setup.Authority, setup.SetupState });
        var setupReady = setup.IsReady && setup.SetupState == AccountingSetupStateValues.Ready;
        Add(checks, "accounting_setup", setupReady, true,
            setupReady ? null : AccountingProviderSwitchPreparationReasonCodes.ConfigurationIncomplete,
            setupReady ? "Accounting setup passed its configured policy checks." : "Resolve the blocking accounting setup issues and mark setup ready before preparation.",
            new { setup.SetupState, issues = setup.Issues.Select(x => new { x.ReasonCode, x.SubjectKey }) });

        var period = await db.FiscalPeriods.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId && x.Id == providerSwitch.EffectiveFiscalPeriodId,
                cancellationToken);
        var periodReady = period is not null && !period.IsClosed && !period.IsReportingLocked;
        Add(checks, "effective_fiscal_period", periodReady, true,
            periodReady ? null : AccountingProviderSwitchPreparationReasonCodes.FiscalPeriodMissing,
            period is null ? "The effective monthly accounting period is missing."
                : periodReady ? "The effective monthly accounting period is open and ready for candidate validation."
                : "The effective monthly accounting period is closed or reporting-locked.",
            new { periodId = period?.Id, period?.StartUtc, period?.EndUtc, period?.IsClosed, period?.IsReportingLocked });

        var configuration = setup.Configuration;
        var activeSeries = await db.VoucherSeries.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.IsActive).Select(x => x.Code).ToListAsync(cancellationToken);
        Add(checks, "voucher_series", activeSeries.Count > 0, true,
            activeSeries.Count > 0 ? null : AccountingProviderSwitchPreparationReasonCodes.VoucherSeriesMissing,
            activeSeries.Count > 0 ? "At least one active voucher series is available." : "Create an active voucher series before preparation.",
            new { activeSeries });

        var currencyReady = configuration is not null && configuration.BaseCurrency.Length == 3 &&
                            configuration.BaseCurrency.All(character => character is >= 'A' and <= 'Z');
        Add(checks, "base_currency", currencyReady, true,
            currencyReady ? null : AccountingProviderSwitchPreparationReasonCodes.BaseCurrencyInvalid,
            currencyReady ? $"Base currency is {configuration!.BaseCurrency}." : "Configure a valid three-letter base currency.",
            new { baseCurrency = configuration?.BaseCurrency });

        var roleRows = await db.AccountingConfigurationAccountRoles.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId)
            .Select(x => new { x.RoleKey, x.FinanceAccountId, x.FinanceAccount.IsPostingEnabled,
                x.FinanceAccount.ControlAccountRole })
            .ToListAsync(cancellationToken);
        var roles = roleRows.Select(x => x.RoleKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var requiredRoles = configuration is null
            ? []
            : policyPackResolver.Resolve(configuration.PolicyPackKey, configuration.PolicyPackVersion)
                .Definition.AccountRoles.Where(x => x.IsRequired).Select(x => x.Key).ToArray();
        var missingRoles = requiredRoles.Where(x => !roles.Contains(x)).ToArray();
        var invalidRoleAccounts = roleRows.Where(x => !x.IsPostingEnabled).Select(x => x.RoleKey).ToArray();
        Add(checks, "chart_roles", missingRoles.Length == 0 && invalidRoleAccounts.Length == 0, true,
            missingRoles.Length == 0 && invalidRoleAccounts.Length == 0 ? null : AccountingProviderSwitchPreparationReasonCodes.ChartRolesMissing,
            missingRoles.Length == 0 && invalidRoleAccounts.Length == 0
                ? "Required chart roles point to posting-enabled accounts."
                : "Assign every required chart role to a posting-enabled account.",
            new { missingRoles, invalidRoleAccounts });

        var requiredControlRoles = new[] { AccountingAccountRoleKeys.AccountsReceivable, AccountingAccountRoleKeys.AccountsPayable };
        var missingControlRoles = requiredControlRoles.Where(x => !roles.Contains(x)).ToList();
        if (!roles.Contains(AccountingAccountRoleKeys.Bank) && !roles.Contains(AccountingAccountRoleKeys.Cash))
            missingControlRoles.Add(AccountingAccountRoleKeys.Bank);
        Add(checks, "control_and_payment_accounts", missingControlRoles.Count == 0, true,
            missingControlRoles.Count == 0 ? null : AccountingProviderSwitchPreparationReasonCodes.ControlAccountsMissing,
            missingControlRoles.Count == 0
                ? "Receivables, payables, and bank or cash account roles are configured."
                : "Configure receivables, payables, and bank or cash account roles before preparation.",
            new { missingControlRoles });

        var hasTaxSource = await db.AccountingProviderSwitchStagedRecords.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.CompanyId == query.CompanyId && x.SwitchId == query.SwitchId && x.IsCurrent &&
                           x.Dataset == AccountingProviderSwitchStagingDatasets.TaxTreatments, cancellationToken);
        var taxRules = configuration is null ? [] : policyPackResolver
            .Resolve(configuration.PolicyPackKey, configuration.PolicyPackVersion).Definition.TaxRules;
        var taxReady = !hasTaxSource || taxRules.Count > 0;
        Add(checks, "tax_rules", taxReady, true,
            taxReady ? null : AccountingProviderSwitchPreparationReasonCodes.TaxRulesMissing,
            taxReady ? "The selected policy pack has deterministic tax rules for staged tax data."
                : "Staged tax data exists but the selected accounting policy has no tax rules.",
            new { hasTaxSource, configuredRuleCount = taxRules.Count });

        var hasTargetDimensions = await db.AccountingProviderSwitchStagedRecords.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.CompanyId == query.CompanyId && x.SwitchId == query.SwitchId && x.IsCurrent &&
                           x.Dataset == AccountingProviderSwitchStagingDatasets.Dimensions &&
                           x.Disposition != AccountingProviderSwitchDispositions.ExcludedWithApproval &&
                           x.Disposition != AccountingProviderSwitchDispositions.Unsupported,
                cancellationToken);
        Add(checks, "dimensions", !hasTargetDimensions, true,
            hasTargetDimensions ? AccountingProviderSwitchPreparationReasonCodes.DimensionsUnsupported : null,
            hasTargetDimensions
                ? "Staged accounting dimensions require a supported native target representation or an approved archive dependency."
                : "No unsupported accounting dimensions require native representation.",
            new { stagedNativeDimensions = hasTargetDimensions });

        var latestAssessmentId = await db.AccountingProviderSwitchAssessments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.SwitchId == query.SwitchId &&
                        x.Status == AccountingProviderSwitchAssessmentStatuses.Completed)
            .OrderByDescending(x => x.CompletedUtc).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(cancellationToken);
        var gaps = latestAssessmentId.HasValue
            ? await db.AccountingProviderSwitchGaps.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == query.CompanyId && x.SwitchId == query.SwitchId && x.AssessmentId == latestAssessmentId)
                .OrderByDescending(x => x.IsBlocking).ThenBy(x => x.ReasonCode).ToListAsync(cancellationToken)
            : [];
        var blockingGaps = gaps.Where(x => x.IsBlocking).ToArray();
        Add(checks, "unresolved_gaps", blockingGaps.Length == 0, true,
            blockingGaps.Length == 0 ? null : AccountingProviderSwitchPreparationReasonCodes.BlockingGap,
            blockingGaps.Length == 0 ? "No deterministic blocking assessment gap remains."
                : $"{blockingGaps.Length} deterministic assessment gap(s) still block preparation.",
            new { blockingReasonCodes = blockingGaps.Select(x => x.ReasonCode) });

        var complianceValidated = setup.IsCountrySpecificComplianceConfigured;
        var disclosure = configuration?.ComplianceNotice ??
                         "Country-specific accounting compliance has not been configured.";
        Add(checks, "policy_compliance", complianceValidated, false,
            complianceValidated ? null : AccountingProviderSwitchPreparationReasonCodes.PolicyComplianceDisclosure,
            complianceValidated ? "The selected accounting policy pack declares statutory compliance validation." : disclosure,
            new { complianceValidated, configuration?.PolicyPackKey, configuration?.PolicyPackVersion });

        return new(query.CompanyId, query.SwitchId, planReadiness.Plan?.Id, planReadiness.Plan?.PlanHash,
            checks.All(x => !x.IsBlocking || x.IsReady), complianceValidated, disclosure, checks,
            gaps.Select(x => new AccountingProviderSwitchGapDto(x.Id, x.Category, x.DatasetKey, x.Severity,
                x.IsBlocking, x.ReasonCode, x.Explanation, x.EvidenceJson, x.OperatorAction, x.CreatedUtc)).ToArray());
    }

    private static void Add(ICollection<AccountingProviderSwitchReadinessCheckDto> checks, string key,
        bool ready, bool blocking, string? reasonCode, string explanation, object evidence) =>
        checks.Add(new(key, ready, blocking, ready ? null : reasonCode, explanation,
            JsonSerializer.Serialize(evidence)));
}
