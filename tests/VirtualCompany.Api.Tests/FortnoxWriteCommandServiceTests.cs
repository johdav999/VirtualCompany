using System.Text.Json.Nodes;
using FluentAssertions;
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

        sanitized.Should().Contain("*** redacted ***");
        sanitized.Should().Contain("Acme AB");
        sanitized.Should().NotContain("secret-token");
        sanitized.Should().NotContain("secret-client");
    }

    [Fact]
    public void Sanitizer_Hash_Is_Stable_For_Same_Sanitized_Payload()
    {
        var first = new { Customer = new { Name = "Acme AB", access_token = "one" } };
        var second = new { Customer = new { Name = "Acme AB", access_token = "two" } };

        FortnoxWritePayloadSanitizer.CreatePayloadHash(first)
            .Should()
            .Be(FortnoxWritePayloadSanitizer.CreatePayloadHash(second));
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

        request.ProviderKey.Should().Be(FinanceIntegrationProviderKeys.Fortnox);
        request.CompanyId.Should().Be(companyId);
        request.CommandType.Should().Be(FinanceIntegrationWriteCommandTypes.InvoiceExport);
        request.PayloadHash.Should().Be(hash);
        request.Payload.SanitizedJson.Should().NotContain("access_token");
        request.HttpMethod.Should().Be("POST");
        request.Path.Should().Be("invoices");
    }

    [Fact]
    public async Task Api_client_creates_approval_before_external_write_call()
    {
        var handler = new CapturingHandler();
        var approval = new CapturingApprovalService();
        var client = FortnoxApiClientTestFactory.Create(handler, approval);

        var exception = await Assert.ThrowsAsync<FortnoxApprovalRequiredException>(() =>
            client.PostAsync<object, Dictionary<string, bool>>(
                new FortnoxRequestContext(Guid.NewGuid(), Guid.NewGuid(), actorUserId: Guid.NewGuid()),
                "customers",
                new { Customer = new { Name = "Acme AB" } },
                CancellationToken.None));

        exception.ApprovalId.Should().Be(approval.ApprovalId);
        approval.EnsureApprovedCalls.Should().Be(1);
        handler.Requests.Should().BeEmpty();
        approval.LastCheck.Should().NotBeNull();
        approval.LastCheck!.TargetCompany.Should().Be("Fortnox company");
        approval.LastCheck.PayloadSummary.Should().Contain("Acme AB");
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
