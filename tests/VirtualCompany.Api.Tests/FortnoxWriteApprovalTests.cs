using System.Text.Json;
using VirtualCompany.Application.Finance;
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
            "Fortnox writes require approval before data is sent to Fortnox.");

        Assert.Equal(approvalId, exception.ApprovalId);
        Assert.DoesNotContain("token", exception.SafeMessage, StringComparison.OrdinalIgnoreCase);
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
}
