using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

[Trait("Category", "SqlServer")]
public sealed class PaymentExecutionSqlServerIntegrationTests
{
    [SqlServerFact]
    public async Task Restarted_submission_is_frozen_as_ambiguous_and_webhook_replay_is_rejected()
    {
        var builder = new SqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!)
        {
            InitialCatalog = $"virtualcompany_payment_execution_{Guid.NewGuid():N}",
            MultipleActiveResultSets = false
        };
        var connectionString = builder.ConnectionString;
        await using (var setup = CreateContext(connectionString)) await setup.Database.MigrateAsync();

        try
        {
            var seeded = await SeedInterruptedSubmissionAsync(connectionString);
            var recoveredUtc = new DateTime(2026, 8, 28, 16, 1, 0, DateTimeKind.Utc);

            await using (var restartedWorker = CreateContext(connectionString))
            await using (var transaction = await restartedWorker.Database.BeginTransactionAsync())
            {
                var execution = await restartedWorker.PaymentBatchExecutions.IgnoreQueryFilters()
                    .SingleAsync(x => x.CompanyId == seeded.CompanyId && x.Id == seeded.ExecutionId);
                var attempt = await restartedWorker.PaymentExecutionAttempts.IgnoreQueryFilters()
                    .SingleAsync(x => x.CompanyId == seeded.CompanyId && x.ExecutionId == seeded.ExecutionId);

                Assert.Equal(PaymentExecutionStatuses.Submitting, execution.Status);
                Assert.Equal(PaymentExecutionAttemptOutcomes.Started, attempt.Outcome);
                attempt.Complete(PaymentExecutionAttemptOutcomes.Ambiguous, "manual_reconciliation", null,
                    "submission_ambiguous",
                    "The worker stopped after provider submission began. Automatic replay is blocked.", recoveredUtc);
                execution.RequireReconciliation("submission_ambiguous",
                    "Locate the provider payment reference before continuing.", recoveredUtc);
                await restartedWorker.SaveChangesAsync();
                await transaction.CommitAsync();
            }

            await using (var verification = CreateContext(connectionString))
            {
                var execution = await verification.PaymentBatchExecutions.IgnoreQueryFilters()
                    .SingleAsync(x => x.Id == seeded.ExecutionId);
                var attempt = await verification.PaymentExecutionAttempts.IgnoreQueryFilters()
                    .SingleAsync(x => x.ExecutionId == seeded.ExecutionId);
                Assert.Equal(PaymentExecutionStatuses.ReconciliationRequired, execution.Status);
                Assert.Equal(PaymentExecutionAttemptOutcomes.Ambiguous, attempt.Outcome);
                Assert.Equal("manual_reconciliation", attempt.RetryClassification);

                verification.PaymentProviderWebhookReceipts.Add(new PaymentProviderWebhookReceipt(
                    Guid.NewGuid(), seeded.CompanyId, seeded.ExecutionId, "enable-banking",
                    "webhook-replay-1", "provider-payment-1", "ACSP", new string('b', 64),
                    recoveredUtc, recoveredUtc));
                await verification.SaveChangesAsync();
            }

            await using (var replay = CreateContext(connectionString))
            {
                replay.PaymentProviderWebhookReceipts.Add(new PaymentProviderWebhookReceipt(
                    Guid.NewGuid(), seeded.CompanyId, seeded.ExecutionId, "enable-banking",
                    "webhook-replay-1", "provider-payment-1", "ACSC", new string('c', 64),
                    recoveredUtc.AddSeconds(1), recoveredUtc.AddSeconds(1)));
                await Assert.ThrowsAsync<DbUpdateException>(() => replay.SaveChangesAsync());
            }
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private static async Task<Seeded> SeedInterruptedSubmissionAsync(string connectionString)
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var approvalId = Guid.NewGuid();
        var bindingId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var financeAccountId = Guid.NewGuid();
        var bankAccountId = Guid.NewGuid();
        var executionId = Guid.NewGuid();
        var now = new DateTime(2026, 8, 28, 16, 0, 0, DateTimeKind.Utc);

        var batch = new PaymentBatch(batchId, companyId, "SQL-PAY-001", "SQL payment execution",
            new DateOnly(2026, 8, 29), "sql-create-1", new string('a', 64), userId, now);
        var instructionSetVersion = batch.BeginInstructionSet(batch.Version, userId, now);
        var approval = ApprovalRequest.CreateForTarget(approvalId, companyId,
            ApprovalTargetEntityType.PaymentBatch, batchId, "user", userId, "payment_batch",
            new Dictionary<string, JsonNode?>
            {
                ["instructionSetVersion"] = JsonValue.Create(instructionSetVersion)
            }, null, userId, []);
        approval.ApproveCurrentStep(approval.CurrentActionableStep!.Id, userId, "SQL execution test approval");
        var binding = new PaymentBatchApprovalBinding(bindingId, companyId, batchId, approvalId,
            instructionSetVersion, new string('d', 64), userId, now);
        binding.MarkApproved(userId, "SQL execution test approval", now);
        var connection = new BankConnection(connectionId, companyId, "enable-banking", "SE|SQL",
            "SQL Test Bank", userId, now);
        var execution = new PaymentBatchExecution(executionId, companyId, batchId,
            instructionSetVersion, bindingId, connectionId, bankAccountId, "enable-banking",
            new string('e', 64), "sql-submit-1", userId, "sql-worker-restart", now);
        execution.BeginSubmission(now);
        var attempt = new PaymentExecutionAttempt(Guid.NewGuid(), companyId, executionId, 1,
            PaymentExecutionAttemptOperations.Submit, new string('e', 64), now);

        await using var db = CreateContext(connectionString);
        db.AddRange(
            new Company(companyId, "Payment execution SQL company"),
            new User(userId, "sql-payment@example.test", "SQL Payment Operator", "test", $"sql-{userId:N}"),
            new FinanceAccount(financeAccountId, companyId, "1930", "Operating cash", "asset", "SEK", 0m, now),
            new CompanyBankAccount(bankAccountId, companyId, financeAccountId, "Operating", "SQL Test Bank", "•••• 0001", "SEK"),
            connection, batch, approval, binding, execution, attempt);
        await db.SaveChangesAsync();
        return new(companyId, executionId);
    }

    private static VirtualCompanyDbContext CreateContext(string connectionString) => new(
        new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsAssembly(
                typeof(VirtualCompany.Persistence.Migrations.Persistence.MigrationAssemblyMarker)
                    .Assembly.GetName().Name))
            .Options);

    private sealed record Seeded(Guid CompanyId, Guid ExecutionId);
}
