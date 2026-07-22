using System.Text.Json.Nodes;
using VirtualCompany.Application.Finance;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FortnoxWriteCommandServiceTests
{
    [Fact]
    public void Sanitizer_Redacts_Tokens_And_Secrets()
    {
        var payload = new
        {
            Customer = new
            {
                Name = "Acme AB",
                access_token = "secret-token",
                ClientSecret = "secret-client",
                Email = "finance@example.test"
            }
        };

        var sanitized = FortnoxWritePayloadSanitizer.CreateSanitizedJson(payload);

        Assert.Contains("*** redacted ***", sanitized, StringComparison.Ordinal);
        Assert.Contains("Acme AB", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-client", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void Sanitizer_Hash_Is_Stable_For_Same_Sanitized_Payload()
    {
        var first = new { Customer = new { Name = "Acme AB", access_token = "one" } };
        var second = new { Customer = new { Name = "Acme AB", access_token = "two" } };

        Assert.Equal(
            FortnoxWritePayloadSanitizer.CreatePayloadHash(first),
            FortnoxWritePayloadSanitizer.CreatePayloadHash(second));
    }

    [Fact]
    public void Write_Request_Carries_Approval_And_Duplicate_Prevention_Context()
    {
        var companyId = Guid.NewGuid();
        var payload = new { Invoice = new { CustomerNumber = "100", Total = 1200m } };
        var hash = FortnoxWritePayloadSanitizer.CreatePayloadHash(payload);

        var request = new FinanceIntegrationWriteCommand(
            FinanceIntegrationProviderKeys.Fortnox,
            companyId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            FinanceIntegrationWriteCommandTypes.InvoiceExport,
            "POST",
            "invoices",
            "Example company",
            FortnoxWritePayloadSanitizer.CreateSummary(payload),
            hash,
            new FinanceIntegrationWritePayload(
                FortnoxWritePayloadSanitizer.CreateSanitizedJson(payload),
                payload.GetType().Name),
            Guid.NewGuid(),
            "correlation-1");

        Assert.Equal(FinanceIntegrationProviderKeys.Fortnox, request.ProviderKey);
        Assert.Equal(companyId, request.CompanyId);
        Assert.Equal(FinanceIntegrationWriteCommandTypes.InvoiceExport, request.CommandType);
        Assert.Equal(hash, request.PayloadHash);
        Assert.DoesNotContain("access_token", request.Payload.SanitizedJson, StringComparison.Ordinal);
        Assert.Equal("POST", request.HttpMethod);
        Assert.Equal("invoices", request.Path);
    }

    [Fact]
    public async Task Api_client_creates_approval_before_external_write_call()
    {
        var handler = new CapturingHandler();
        var approval = new CapturingApprovalService();
        var client = FortnoxApiClientTestFactory.Create(handler, approval);

        var exception = await Assert.ThrowsAsync<FortnoxApprovalRequiredException>(() =>
            client.PostAsync<object, Dictionary<string, bool>>(
                new FortnoxRequestContext(Guid.NewGuid(), Guid.NewGuid(), ActorUserId: Guid.NewGuid()),
                "customers",
                new { Customer = new { Name = "Acme AB" } },
                CancellationToken.None));

        Assert.Equal(approval.ApprovalId, exception.ApprovalId);
        Assert.Equal(1, approval.EnsureApprovedCalls);
        Assert.Empty(handler.Requests);
        Assert.NotNull(approval.LastCheck);
        Assert.Equal("Fortnox company", approval.LastCheck!.TargetCompany);
        Assert.Contains("Acme AB", approval.LastCheck.PayloadSummary, StringComparison.Ordinal);
    }

    private sealed class CapturingApprovalService : IFinanceIntegrationWriteApprovalService
    {
        public Guid ApprovalId { get; } = Guid.NewGuid();
        public int EnsureApprovedCalls { get; private set; }
        public FinanceIntegrationWriteApprovalCheck? LastCheck { get; private set; }

        public Task EnsureApprovedAsync(FinanceIntegrationWriteApprovalCheck check, CancellationToken cancellationToken)
        {
            EnsureApprovedCalls++;
            LastCheck = check;
            throw new FortnoxApprovalRequiredException(ApprovalId, "Approve this action before data is sent to the accounting system.");
        }

        public Task RecordExecutionSucceededAsync(FinanceIntegrationWriteApprovalCheck check, object? responsePayload, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RecordExecutionFailedAsync(FinanceIntegrationWriteApprovalCheck check, Exception exception, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
