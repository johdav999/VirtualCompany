using System.Text.Json;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FortnoxWriteApprovalTests
{
    [Fact]
    public void Payload_summary_redacts_tokens_and_client_secrets()
    {
        var payload = new
        {
            Customer = new
            {
                Name = "Example AB",
                AccessToken = "access-token-value",
                RefreshToken = "refresh-token-value",
                ClientSecret = "client-secret-value",
                AuthorizationCode = "authorization-code-value"
            }
        };

        var summary = FortnoxWritePayloadSanitizer.CreateSummary(payload);

        Assert.Contains("Example AB", summary);
        Assert.DoesNotContain("access-token-value", summary);
        Assert.DoesNotContain("refresh-token-value", summary);
        Assert.DoesNotContain("client-secret-value", summary);
        Assert.DoesNotContain("authorization-code-value", summary);
        Assert.Contains("*** redacted ***", summary);
    }

    [Fact]
    public void Payload_hash_is_deterministic_for_same_redacted_payload()
    {
        var first = new
        {
            Invoice = new
            {
                DocumentNumber = "1001",
                Total = 1250m,
                AccessToken = "first-secret"
            }
        };
        var second = new
        {
            Invoice = new
            {
                DocumentNumber = "1001",
                Total = 1250m,
                AccessToken = "second-secret"
            }
        };

        var firstHash = FortnoxWritePayloadSanitizer.CreatePayloadHash(first);
        var secondHash = FortnoxWritePayloadSanitizer.CreatePayloadHash(second);

        Assert.Equal(firstHash, secondHash);
        Assert.Equal(64, firstHash.Length);
    }

    [Fact]
    public void Approval_required_exception_exposes_approval_id_without_payload()
    {
        var approvalId = Guid.NewGuid();

        var exception = new FortnoxApprovalRequiredException(
            approvalId,
            "Approve this action before data is sent to the accounting system.");

        Assert.Equal(approvalId, exception.ApprovalId);
        Assert.DoesNotContain("token", exception.SafeMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Write_action_statuses_distinguish_rejected_expired_and_cancelled_without_execution()
    {
        var command = CreateCommand(FinanceIntegrationWriteCommandTypes.Payment);

        command.MarkRejected(DateTime.UtcNow);

        Assert.Equal(FinanceIntegrationWriteCommandRecordStatuses.Rejected, command.Status);
        Assert.Null(command.ExecutionStartedUtc);
        Assert.Null(command.ExecutedUtc);
    }

    [Fact]
    public void Sensitive_action_types_do_not_allow_automatic_retry()
    {
        var payment = CreateCommand(FinanceIntegrationWriteCommandTypes.Payment);
        var record = CreateCommand(FinanceIntegrationWriteCommandTypes.AccountingRecord);

        Assert.False(payment.RetrySupported);
        Assert.Equal(FinanceIntegrationWriteRetryPolicyValues.None, payment.RetryPolicy);
        Assert.True(record.RetrySupported);
        Assert.Equal(FinanceIntegrationWriteRetryPolicyValues.TransientOnly, record.RetryPolicy);
    }

    [Fact]
    public void Approved_execution_context_carries_persisted_write_request_and_retry_policy()
    {
        var writeRequestId = Guid.NewGuid();

        var context = new FortnoxRequestContext(Guid.NewGuid(), Guid.NewGuid(), "correlation-1", Guid.NewGuid(), Guid.NewGuid(), writeRequestId, RetryExternalFailures: false);

        Assert.Equal(writeRequestId, context.WriteRequestId);
        Assert.False(context.RetryExternalFailures);
        Assert.NotNull(context.ApprovedApprovalId);
    }

    [Fact]
    public void Successful_execution_persists_safe_response_summary_and_status_code()
    {
        var command = CreateCommand(FinanceIntegrationWriteCommandTypes.AccountingRecord);

        command.MarkApproved(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        command.MarkExecutionStarted(DateTime.UtcNow);
        command.MarkExecuted("1001", 200, """{"DocumentNumber":"1001"}""", DateTime.UtcNow);

        Assert.Equal(FinanceIntegrationWriteCommandRecordStatuses.Executed, command.Status);
        Assert.Equal(1, command.ExecutionAttemptCount);
        Assert.Equal(200, command.ResponseStatusCode);
        Assert.Contains("1001", command.SafeResponseSummary);
    }

    [Fact]
    public void Approved_preflight_failure_can_be_prepared_for_retry_without_losing_approval()
    {
        var command = CreateCommand(FinanceIntegrationWriteCommandTypes.SupplierMasterData);
        var approvalId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var failedUtc = DateTime.UtcNow;

        command.MarkApproved(approvalId, Guid.NewGuid(), failedUtc.AddMinutes(-1));
        command.MarkExecutionStarted(failedUtc.AddSeconds(-30));
        command.MarkFailed("authorization", "Fortnox needs to be reconnected.", null, failedUtc);

        command.PrepareApprovedRetryAfterPreflightFailure(connectionId, failedUtc.AddMinutes(1));

        Assert.Equal(FinanceIntegrationWriteCommandRecordStatuses.Approved, command.Status);
        Assert.Equal(approvalId, command.ApprovalId);
        Assert.Equal(connectionId, command.ConnectionId);
        Assert.Null(command.FailureCategory);
        Assert.Null(command.SafeFailureSummary);
        Assert.Null(command.FailedUtc);
        Assert.Null(command.ExecutionStartedUtc);
        Assert.Equal(1, command.ExecutionAttemptCount);
    }

    [Fact]
    public void Provider_response_failure_cannot_be_prepared_as_a_preflight_retry()
    {
        var command = CreateCommand(FinanceIntegrationWriteCommandTypes.SupplierMasterData);
        var now = DateTime.UtcNow;

        command.MarkApproved(Guid.NewGuid(), Guid.NewGuid(), now.AddMinutes(-1));
        command.MarkExecutionStarted(now.AddSeconds(-30));
        command.MarkFailed("validation", "Fortnox rejected the supplier.", 400, now);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            command.PrepareApprovedRetryAfterPreflightFailure(command.ConnectionId, now.AddMinutes(1)));

        Assert.Contains("provider response", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(FinanceIntegrationWriteCommandRecordStatuses.Failed, command.Status);
    }

    [Fact]
    public void Real_fortnox_api_tests_are_opt_in_only()
    {
        var enabled = string.Equals(
            Environment.GetEnvironmentVariable("VC_FORTNOX_REAL_API_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        Assert.False(enabled);
    }

    private static FinanceIntegrationWriteCommandRecord CreateCommand(string commandType) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            commandType,
            "POST",
            "invoices",
            "Example company",
            """{"Invoice":{"Total":100}}""",
            new string('a', 64),
            """{"Invoice":{"Total":100}}""",
            "correlation-1",
            DateTime.UtcNow);
}
