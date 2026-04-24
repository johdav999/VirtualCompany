using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CompanyFinanceSeedingStateResolver : IFinanceSeedingStateResolver, IFinanceSeedingStateService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyContextAccessor? _companyContextAccessor;
    private readonly TimeProvider _timeProvider;

    public CompanyFinanceSeedingStateResolver(VirtualCompanyDbContext dbContext)
        : this(dbContext, null, TimeProvider.System)
    {
    }

    public CompanyFinanceSeedingStateResolver(
        VirtualCompanyDbContext dbContext,
        ICompanyContextAccessor? companyContextAccessor)
        : this(dbContext, companyContextAccessor, TimeProvider.System)
    {
    }

    public CompanyFinanceSeedingStateResolver(
        VirtualCompanyDbContext dbContext,
        ICompanyContextAccessor? companyContextAccessor,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _companyContextAccessor = companyContextAccessor;
        _timeProvider = timeProvider;
    }

    public async Task<FinanceSeedingStateResultDto> ResolveAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Company id is required.", nameof(companyId));
        }

        EnsureTenant(companyId);

        var company = await _dbContext.Companies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == companyId, cancellationToken);

        if (company is null)
        {
            throw new KeyNotFoundException($"Company '{companyId}' was not found.");
        }

        var metadata = FinanceSeedingMetadata.Read(company);
        var checkedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        if (TryResolveMetadataFastPath(companyId, company.FinanceSeedStatus, metadata, checkedAtUtc, out var fastPathResult))
        {
            return fastPathResult;
        }

        var execution = await LoadSeedExecutionAsync(companyId, cancellationToken);
        var recordChecks = await LoadRecordChecksAsync(companyId, cancellationToken);
        var state = ResolveState(company.FinanceSeedStatus, metadata, recordChecks, execution);
        var reason = BuildReason(company.FinanceSeedStatus, metadata, recordChecks, execution, state);
        var derivedFrom = metadata?.HasExtensionMetadata == true || company.FinanceSeedStatus != FinanceSeedingState.NotSeeded
            ? FinanceSeedingStateDerivedFromValues.Metadata
            : FinanceSeedingStateDerivedFromValues.RecordChecks;

        return BuildResult(
            companyId,
            state,
            derivedFrom,
            checkedAtUtc,
            metadata,
            company.FinanceSeedStatus,
            recordChecks,
            usedFastPath: metadata?.HasExtensionMetadata == true,
            reason);
    }

    public Task<FinanceSeedingStateResultDto> GetCompanyFinanceSeedingStateAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        ResolveAsync(companyId, cancellationToken);

    private FinanceSeedingStateResultDto BuildResult(
        Guid companyId,
        FinanceSeedingState state,
        string derivedFrom,
        DateTime checkedAtUtc,
        FinanceSeedingMetadataSnapshot? metadata,
        FinanceSeedingState persistedState,
        FinanceSeedRecordChecks recordChecks,
        bool usedFastPath,
        string reason)
    {
        var metadataPresent = metadata?.HasExtensionMetadata == true || persistedState != FinanceSeedingState.NotSeeded || metadata?.SeededAtUtc is not null;

        return new FinanceSeedingStateResultDto(
            companyId,
            state,
            derivedFrom,
            checkedAtUtc,
            new FinanceSeedingStateDiagnosticsDto(
                metadataPresent,
                persistedState,
                metadata?.State,
                metadata?.State == FinanceSeedingState.Seeded || persistedState == FinanceSeedingState.Seeded,
                usedFastPath,
                reason,
                recordChecks.HasAccounts,
                recordChecks.HasCounterparties,
                recordChecks.HasTransactions,
                recordChecks.HasBalances,
                recordChecks.HasPolicyConfiguration,
                recordChecks.HasInvoices,
                recordChecks.HasBills));
    }

    private void EnsureTenant(Guid companyId)
    {
        if (_companyContextAccessor?.CompanyId is Guid scopedCompanyId && scopedCompanyId != companyId)
        {
            throw new UnauthorizedAccessException("Finance seeding state can only be resolved for the active company context.");
        }
    }

    private async Task<BackgroundExecution?> LoadSeedExecutionAsync(Guid companyId, CancellationToken cancellationToken) =>
        await _dbContext.BackgroundExecutions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.ExecutionType == BackgroundExecutionType.FinanceSeed)
            .OrderByDescending(x => x.CreatedUtc)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<FinanceSeedRecordChecks> LoadRecordChecksAsync(Guid companyId, CancellationToken cancellationToken)
    {
        try
        {
            var hasAccounts = await _dbContext.FinanceAccounts
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId, cancellationToken);
            var hasCounterparties = await _dbContext.FinanceCounterparties
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId, cancellationToken);
            var hasTransactions = await _dbContext.FinanceTransactions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId, cancellationToken);
            var hasBalances = await _dbContext.FinanceBalances
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId, cancellationToken);
            var hasPolicyConfiguration = await _dbContext.FinancePolicyConfigurations
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId, cancellationToken);
            var hasInvoices = await _dbContext.FinanceInvoices
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId, cancellationToken);
            var hasBills = await _dbContext.FinanceBills
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId, cancellationToken);

            return new FinanceSeedRecordChecks(
                hasAccounts,
                hasCounterparties,
                hasTransactions,
                hasBalances,
                hasPolicyConfiguration,
                hasInvoices,
                hasBills);
        }
        catch (SqlException ex) when (IsMissingFinanceSchema(ex))
        {
            return new FinanceSeedRecordChecks(false, false, false, false, false, false, false);
        }
    }

    private static bool IsMissingFinanceSchema(SqlException exception) =>
        exception.Number == 208 &&
        exception.Message.Contains("finance_", StringComparison.OrdinalIgnoreCase);

    private static FinanceSeedingState ResolveState(
        FinanceSeedingState persistedState,
        FinanceSeedingMetadataSnapshot? metadata,
        FinanceSeedRecordChecks recordChecks,
        BackgroundExecution? execution)
    {
        if (recordChecks.IsComplete)
        {
            return FinanceSeedingState.Seeded;
        }

        if (execution?.Status is BackgroundExecutionStatus.Pending or BackgroundExecutionStatus.InProgress or BackgroundExecutionStatus.RetryScheduled)
        {
            return FinanceSeedingState.Seeding;
        }

        if (execution?.Status is BackgroundExecutionStatus.Failed or BackgroundExecutionStatus.Blocked)
        {
            return FinanceSeedingState.Failed;
        }

        if (persistedState == FinanceSeedingState.Failed || metadata?.State == FinanceSeedingState.Failed)
        {
            return FinanceSeedingState.Failed;
        }

        if (persistedState == FinanceSeedingState.Seeding ||
            metadata?.State == FinanceSeedingState.Seeding ||
            persistedState == FinanceSeedingState.Seeded ||
            metadata?.State == FinanceSeedingState.Seeded ||
            recordChecks.HasAnyRecords)
        {
            return FinanceSeedingState.Seeding;
        }

        return FinanceSeedingState.NotSeeded;
    }

    private bool TryResolveMetadataFastPath(
        Guid companyId,
        FinanceSeedingState persistedState,
        FinanceSeedingMetadataSnapshot? metadata,
        DateTime checkedAtUtc,
        out FinanceSeedingStateResultDto result)
    {
        result = default!;

        if (metadata?.HasExtensionMetadata != true)
        {
            return false;
        }

        if (metadata.State == FinanceSeedingState.Seeded && metadata.HasCompleteFoundationalChecks)
        {
            var recordChecks = new FinanceSeedRecordChecks(
                metadata.Accounts == true,
                metadata.Counterparties == true,
                metadata.Transactions == true,
                metadata.Balances == true,
                metadata.PolicyConfiguration == true,
                metadata.Invoices == true,
                metadata.Bills == true);
            result = BuildResult(
                companyId,
                FinanceSeedingState.Seeded,
                FinanceSeedingStateDerivedFromValues.Metadata,
                checkedAtUtc,
                metadata,
                persistedState,
                recordChecks,
                usedFastPath: true,
                "Finance seed metadata confirms the foundational dataset is ready.");
            return true;
        }

        if (metadata.State == FinanceSeedingState.NotSeeded && !metadata.HasAnyPositiveChecks)
        {
            result = BuildResult(
                companyId,
                FinanceSeedingState.NotSeeded,
                FinanceSeedingStateDerivedFromValues.Metadata,
                checkedAtUtc,
                metadata,
                persistedState,
                new FinanceSeedRecordChecks(false, false, false, false, false, false, false),
                usedFastPath: true,
                "No finance seed metadata or finance indicators were found.");
            return true;
        }

        return false;
    }

    private static string BuildReason(
        FinanceSeedingState persistedState,
        FinanceSeedingMetadataSnapshot? metadata,
        FinanceSeedRecordChecks recordChecks,
        BackgroundExecution? execution,
        FinanceSeedingState state)
    {
        if (state == FinanceSeedingState.Seeded)
        {
            return "Foundational finance indicators were confirmed through lightweight record existence checks.";
        }

        if (execution?.Status is BackgroundExecutionStatus.Pending or BackgroundExecutionStatus.InProgress or BackgroundExecutionStatus.RetryScheduled)
        {
            return "A finance seed background execution is active for this company.";
        }

        if (execution?.Status is BackgroundExecutionStatus.Failed or BackgroundExecutionStatus.Blocked)
        {
            return string.IsNullOrWhiteSpace(execution.FailureMessage)
                ? "The latest finance seed attempt failed and can be retried."
                : execution.FailureMessage!;
        }

        if (state == FinanceSeedingState.Failed)
        {
            return "The latest finance seed attempt failed and can be retried.";
        }

        if (persistedState == FinanceSeedingState.Seeded || metadata?.State == FinanceSeedingState.Seeded)
        {
            return "Metadata indicates finance seeding started, but foundational records could not be fully confirmed yet.";
        }

        if (persistedState == FinanceSeedingState.Seeding || metadata?.State == FinanceSeedingState.Seeding || recordChecks.HasAnyRecords)
        {
            return "Finance seed initialization is in progress for this company.";
        }

        return "No finance seed metadata or finance indicators were found.";
    }

    private sealed record FinanceSeedRecordChecks(
        bool HasAccounts,
        bool HasCounterparties,
        bool HasTransactions,
        bool HasBalances,
        bool HasPolicyConfiguration,
        bool HasInvoices,
        bool HasBills)
    {
        public bool IsComplete => HasAccounts && HasCounterparties && HasTransactions && HasBalances && HasPolicyConfiguration;
        public bool HasAnyRecords => HasAccounts || HasCounterparties || HasTransactions || HasBalances || HasPolicyConfiguration || HasInvoices || HasBills;
    }
}
