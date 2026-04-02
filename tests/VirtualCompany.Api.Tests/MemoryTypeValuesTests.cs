using VirtualCompany.Domain.Enums;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class MemoryTypeValuesTests
{
    [Theory]
    [InlineData(MemoryType.Preference, "preference")]
    [InlineData(MemoryType.DecisionPattern, "decision_pattern")]
    [InlineData(MemoryType.Summary, "summary")]
    [InlineData(MemoryType.RoleMemory, "role_memory")]
    [InlineData(MemoryType.CompanyMemory, "company_memory")]
    public void ToStorageValue_returns_canonical_values(MemoryType memoryType, string expected)
    {
        Assert.Equal(expected, memoryType.ToStorageValue());
    }

    [Theory]
    [InlineData("preference", MemoryType.Preference)]
    [InlineData("decision_pattern", MemoryType.DecisionPattern)]
    [InlineData("summary", MemoryType.Summary)]
    [InlineData("role_memory", MemoryType.RoleMemory)]
    [InlineData("company_memory", MemoryType.CompanyMemory)]
    [InlineData("decisionPattern", MemoryType.DecisionPattern)]
    [InlineData("role-memory", MemoryType.RoleMemory)]
    [InlineData("companyMemory", MemoryType.CompanyMemory)]
    public void TryParse_accepts_canonical_values_and_supported_aliases(string value, MemoryType expected)
    {
        var parsed = MemoryTypeValues.TryParse(value, out var memoryType);

        Assert.True(parsed);
        Assert.Equal(expected, memoryType);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("unsupported")]
    [InlineData("role")]
    [InlineData("company")]
    public void TryParse_rejects_unsupported_values(string value)
    {
        var parsed = MemoryTypeValues.TryParse(value, out var memoryType);

        Assert.False(parsed);
        Assert.Equal(default, memoryType);
    }
}
