using System.Text.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Policies;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class MemoryContentSafetyPolicyTests
{
    [Fact]
    public void Memory_item_accepts_sanitized_summary_and_safe_metadata()
    {
        var item = new MemoryItem(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            MemoryType.CompanyMemory,
            "Controller approval is required for refunds above ten thousand dollars.",
            null,
            null,
            0.75m,
            DateTime.UtcNow,
            null,
            new Dictionary<string, JsonNode?>
            {
                ["category"] = JsonValue.Create("finance"),
                ["source"] = JsonValue.Create("policy")
            });

        Assert.Equal("Controller approval is required for refunds above ten thousand dollars.", item.Summary);
        Assert.Equal("finance", item.Metadata["category"]!.GetValue<string>());
    }

    [Fact]
    public void Memory_item_rejects_summary_that_contains_hidden_reasoning_markers()
    {
        var exception = Assert.Throws<ArgumentException>(() => new MemoryItem(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            MemoryType.Summary,
            "Chain-of-thought: inspect the ledger, compare the invoices, then privately reason about the discrepancy.",
            null,
            null,
            0.60m,
            DateTime.UtcNow,
            null));

        Assert.Contains("sanitized memory summary", exception.Message);
    }

    [Fact]
    public void Memory_item_rejects_metadata_that_uses_reasoning_fields()
    {
        var exception = Assert.Throws<ArgumentException>(() => new MemoryItem(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            MemoryType.Summary,
            "Vendor escalation preference is captured in a safe summary.",
            null,
            null,
            0.60m,
            DateTime.UtcNow,
            null,
            new Dictionary<string, JsonNode?>
            {
                ["reasoning"] = JsonValue.Create("private internal notes")
            }));

        Assert.Contains("Metadata field 'reasoning' is not allowed", exception.Message);
    }

    [Fact]
    public void Policy_flags_unsafe_extension_fields()
    {
        var properties = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["chainOfThought"] = JsonSerializer.SerializeToElement("private scratchpad"),
            ["category"] = JsonSerializer.SerializeToElement("finance")
        };

        Assert.Equal(new[] { "chainOfThought" }, MemoryContentSafetyPolicy.FindUnsafeAdditionalProperties(properties));
    }
}
