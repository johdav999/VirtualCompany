using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Finance;

namespace VirtualCompany.Finance.Tests;

public sealed class FinanceRecordSourcePolicyTests
{
    [Theory]
    [InlineData(FinanceRecordSourceTypes.Fortnox, null, false, FinanceDataSources.Fortnox)]
    [InlineData(FinanceRecordSourceTypes.Manual, "fortnox", false, FinanceDataSources.Fortnox)]
    [InlineData(FinanceRecordSourceTypes.Manual, null, true, FinanceDataSources.Fortnox)]
    [InlineData(FinanceRecordSourceTypes.Simulation, null, false, FinanceDataSources.Simulation)]
    [InlineData(FinanceRecordSourceTypes.Manual, null, false, FinanceDataSources.Manual)]
    public void ResolveSource_returns_stable_source(
        string sourceType,
        string? providerKey,
        bool hasFortnoxReference,
        string expected)
    {
        Assert.Equal(expected, FinanceRecordSourcePolicy.ResolveSource(sourceType, providerKey, hasFortnoxReference));
    }
}
