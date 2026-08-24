using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FinanceBillFortnoxRegistrationCompletionService
{
    private const string CorrelationPrefix = "finance-bill-inbox:";
    private const string CorrelationSuffix = ":fortnox-registration";

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FinanceBillFortnoxRegistrationCompletionService> _logger;

    public FinanceBillFortnoxRegistrationCompletionService(
        VirtualCompanyDbContext dbContext,
        IAuditEventWriter auditEventWriter,
        TimeProvider timeProvider,
        ILogger<FinanceBillFortnoxRegistrationCompletionService> logger)
    {
        _dbContext = dbContext;
        _auditEventWriter = auditEventWriter;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<bool> CompleteAsync(
        FinanceIntegrationWriteCommandRecord command,
        CancellationToken cancellationToken)
    {
        if (!TryResolveCompletedBillId(command, out var billId))
        {
            return false;
        }

        var executionStrategy = _dbContext.Database.CreateExecutionStrategy();
        return await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            return await CompleteInTransactionAsync(command, billId, transaction, cancellationToken);
        });
    }

    private async Task<bool> CompleteInTransactionAsync(
        FinanceIntegrationWriteCommandRecord command,
        Guid billId,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        var state = await _dbContext.FinanceBillReviewStates
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.CompanyId == command.CompanyId && x.DetectedBillId == billId,
                cancellationToken);

        if (state is null)
        {
            _logger.LogWarning(
                "Fortnox accepted a supplier bill but no review state was available to complete. CompanyId: {CompanyId}. BillId: {BillId}. WriteRequestId: {WriteRequestId}.",
                command.CompanyId,
                billId,
                command.Id);
            return false;
        }

        if (state.Status == FinanceBillInboxStatuses.SentToPaymentExported)
        {
            _logger.LogDebug(
                "Supplier bill review was already completed after Fortnox registration. CompanyId: {CompanyId}. BillId: {BillId}. WriteRequestId: {WriteRequestId}.",
                command.CompanyId,
                billId,
                command.Id);
            return false;
        }

        if (state.Status != FinanceBillInboxStatuses.Approved)
        {
            _logger.LogWarning(
                "Fortnox accepted a supplier bill but its review state could not be completed because it was not approved. CompanyId: {CompanyId}. BillId: {BillId}. WriteRequestId: {WriteRequestId}. ReviewStatus: {ReviewStatus}.",
                command.CompanyId,
                billId,
                command.Id,
                state.Status);
            return false;
        }

        var occurredUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var action = state.MarkRegisteredInAccountingSystem(
            command.ActorUserId,
            "Fortnox integration",
            "Fortnox accepted the supplier invoice registration.",
            occurredUtc);
        _dbContext.FinanceBillReviewActions.Add(action);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                command.CompanyId,
                command.ActorUserId.HasValue ? AuditActorTypes.User : AuditActorTypes.System,
                command.ActorUserId,
                "finance.bill_inbox.fortnox_registered",
                "finance_bill_inbox_item",
                billId.ToString("D"),
                AuditEventOutcomes.Succeeded,
                action.Rationale,
                Metadata: new Dictionary<string, string?>
                {
                    ["priorStatus"] = "Approved",
                    ["newStatus"] = "Sent to Fortnox",
                    ["reviewActionId"] = action.Id.ToString("D"),
                    ["writeRequestId"] = command.Id.ToString("D"),
                    ["externalId"] = command.ExternalId
                },
                CorrelationId: command.CorrelationId,
                OccurredUtc: occurredUtc),
            cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _logger.LogInformation(
            "Supplier bill review completed after successful Fortnox registration. CompanyId: {CompanyId}. BillId: {BillId}. WriteRequestId: {WriteRequestId}. ExternalId: {ExternalId}.",
            command.CompanyId,
            billId,
            command.Id,
            command.ExternalId);
        return true;
    }

    internal static bool TryResolveCompletedBillId(
        FinanceIntegrationWriteCommandRecord command,
        out Guid billId)
    {
        billId = Guid.Empty;
        if (command.Status != FinanceIntegrationWriteCommandRecordStatuses.Executed ||
            !string.Equals(command.Path, "supplierinvoices", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(command.CorrelationId) ||
            !command.CorrelationId.StartsWith(CorrelationPrefix, StringComparison.OrdinalIgnoreCase) ||
            !command.CorrelationId.EndsWith(CorrelationSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var value = command.CorrelationId[
            CorrelationPrefix.Length..
            ^CorrelationSuffix.Length];
        return Guid.TryParseExact(value, "N", out billId);
    }
}

public sealed class FinanceBillRegistrationReconciliationOptions
{
    public const string SectionName = "FinanceBillRegistrationReconciliation";
    public bool Enabled { get; set; } = true;
    public int BatchSize { get; set; } = 500;
}

public sealed class FinanceBillFortnoxRegistrationReconciliationBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FinanceBillFortnoxRegistrationReconciliationBackgroundService> _logger;
    private readonly IOptions<FinanceBillRegistrationReconciliationOptions> _options;

    public FinanceBillFortnoxRegistrationReconciliationBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<FinanceBillRegistrationReconciliationOptions> options,
        ILogger<FinanceBillFortnoxRegistrationReconciliationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            _logger.LogInformation("Supplier bill registration reconciliation is disabled.");
            return;
        }
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
            var completionService = scope.ServiceProvider.GetRequiredService<FinanceBillFortnoxRegistrationCompletionService>();
            var commands = await dbContext.FinanceIntegrationWriteCommands
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x =>
                    x.Status == FinanceIntegrationWriteCommandRecordStatuses.Executed &&
                    x.Path == "supplierinvoices" &&
                    x.CorrelationId != null &&
                    x.CorrelationId.StartsWith("finance-bill-inbox:") &&
                    x.CorrelationId.EndsWith(":fortnox-registration"))
                .OrderBy(x => x.ExecutedUtc)
                .Take(Math.Max(1, _options.Value.BatchSize))
                .ToListAsync(stoppingToken);

            var repaired = 0;
            foreach (var command in commands)
            {
                repaired += await completionService.CompleteAsync(command, stoppingToken) ? 1 : 0;
            }

            _logger.LogInformation(
                "Supplier bill Fortnox completion reconciliation finished. CandidateCount: {CandidateCount}. RepairedCount: {RepairedCount}.",
                commands.Count,
                repaired);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Supplier bill Fortnox completion reconciliation failed. It will be retried on the next application start.");
        }
    }
}
