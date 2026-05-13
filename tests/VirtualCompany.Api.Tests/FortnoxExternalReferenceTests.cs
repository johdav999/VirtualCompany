using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FortnoxExternalReferenceTests
{
    [Fact]
    public void Refresh_updates_external_version_and_last_synced_timestamp()
    {
        var createdUtc = new DateTime(2026, 4, 30, 8, 0, 0, DateTimeKind.Utc);
        var refreshedUtc = new DateTime(2026, 4, 30, 9, 0, 0, DateTimeKind.Utc);
        var reference = new FinanceExternalReference(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            FinanceIntegrationProviderKeys.Fortnox,
            "invoice",
            Guid.NewGuid(),
            "1001",
            "1001",
            createdUtc,
            createdUtc);

        reference.Refresh("1001", refreshedUtc, refreshedUtc);

        Assert.Equal(refreshedUtc, reference.ExternalUpdatedUtc);
        Assert.Equal(refreshedUtc, reference.LastSyncedUtc);
        Assert.True(reference.IsCurrent(createdUtc));
    }

    [Fact]
    public void Repoint_repairs_mapping_without_changing_tenant_or_provider_identity()
    {
        var companyId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var replacementId = Guid.NewGuid();
        var reference = new FinanceExternalReference(Guid.NewGuid(), companyId, connectionId, FinanceIntegrationProviderKeys.Fortnox, "customer", Guid.NewGuid(), "C-1", "C-1", null, DateTime.UtcNow);

        reference.RepointToInternalRecord(replacementId, "C-1", null, new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal(companyId, reference.CompanyId);
        Assert.Equal(connectionId, reference.ConnectionId);
        Assert.Equal(FinanceIntegrationProviderKeys.Fortnox, reference.ProviderKey);
        Assert.Equal("customer", reference.EntityType);
        Assert.Equal("C-1", reference.ExternalId);
        Assert.Equal(replacementId, reference.InternalRecordId);
        Assert.Equal(new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc), reference.LastSyncedUtc);
    }
}
