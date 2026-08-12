using System.Text.Json;
using VirtualCompany.Application.Agents;
using VirtualCompany.Infrastructure.Companies;

namespace VirtualCompany.Api.Tests;

public sealed class SharedAgentReasoningGatewayTests
{
    [Fact]
    public void User_message_includes_required_result_version()
    {
        var request = new AgentReasoningRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            AgentCapabilityIds.SalesLeadIntelligence,
            "1.0.0",
            "hybrid-email-classification-v1",
            "1.0.0",
            "Classify the inbound email.",
            [new AgentAiSource("email-1", "inbound_email", "Pricing request", "Please send pricing.")],
            ["recommend"],
            []);

        using var payload = JsonDocument.Parse(SharedAgentReasoningGateway.BuildUserMessage(request));

        Assert.Equal("1.0.0", payload.RootElement.GetProperty("requiredResultVersion").GetString());
    }
}
