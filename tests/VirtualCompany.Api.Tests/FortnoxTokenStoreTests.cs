using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Security;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FortnoxTokenStoreTests
{
    [Fact]
    public async Task UpsertConnectedAsync_persists_encrypted_tokens_and_returns_safe_snapshot()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        var store = new FortnoxTokenStore(dbContext, CreateEncryption());

        var snapshot = await store.UpsertConnectedAsync(
            companyId,
            userId,
            new FortnoxOAuthTokenResult(
                "plain-access-token",
                "plain-refresh-token",
                DateTime.UtcNow.AddHours(1),
                ["bookkeeping"],
                "tenant-1"),
            DateTime.UtcNow,
            CancellationToken.None);

        var persisted = await dbContext.FortnoxConnections.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(snapshot.ConnectionId, persisted.Id);
        Assert.NotEqual("plain-access-token", persisted.EncryptedAccessToken);
        Assert.NotEqual("plain-refresh-token", persisted.EncryptedRefreshToken);
        Assert.DoesNotContain("plain-access-token", persisted.EncryptedAccessToken!, StringComparison.Ordinal);
        Assert.DoesNotContain("plain-refresh-token", persisted.EncryptedRefreshToken!, StringComparison.Ordinal);
        Assert.Equal("tenant-scoped-data-protection", persisted.TokenEncryptionKeyId);
        Assert.Equal("aspnet-data-protection-v1", persisted.TokenEncryptionAlgorithm);
        Assert.Null(snapshot.AccessToken);
        Assert.Null(snapshot.RefreshToken);
    }

    [Fact]
    public async Task GetStatusAsync_returns_safe_snapshot_without_decrypted_tokens()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        var store = new FortnoxTokenStore(dbContext, CreateEncryption());
        await store.UpsertConnectedAsync(
            companyId,
            userId,
            new FortnoxOAuthTokenResult(
                "plain-access-token",
                "plain-refresh-token",
                DateTime.UtcNow.AddHours(1),
                ["bookkeeping"],
                "tenant-1"),
            DateTime.UtcNow,
            CancellationToken.None);

        var snapshot = await store.GetStatusAsync(companyId, null, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal("connected", snapshot!.Status);
        Assert.Null(snapshot.AccessToken);
        Assert.Null(snapshot.RefreshToken);
    }

    [Fact]
    public async Task DisconnectAsync_clears_active_token_material()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        var store = new FortnoxTokenStore(dbContext, CreateEncryption());
        await store.UpsertConnectedAsync(
            companyId,
            userId,
            new FortnoxOAuthTokenResult(
                "plain-access-token",
                "plain-refresh-token",
                DateTime.UtcNow.AddHours(1),
                ["bookkeeping"]),
            DateTime.UtcNow,
            CancellationToken.None);

        var disconnected = await store.DisconnectAsync(companyId, DateTime.UtcNow, CancellationToken.None);

        var persisted = await dbContext.FortnoxConnections.IgnoreQueryFilters().SingleAsync();
        Assert.NotNull(disconnected);
        Assert.Equal("disconnected", disconnected!.Status);
        Assert.Null(persisted.EncryptedAccessToken);
        Assert.Null(persisted.EncryptedRefreshToken);
        Assert.Null(persisted.AccessTokenExpiresUtc);
        Assert.Null(disconnected.AccessToken);
        Assert.Null(disconnected.RefreshToken);
    }

    private static VirtualCompanyDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new VirtualCompanyDbContext(options);
    }

    private static IFieldEncryptionService CreateEncryption() =>
        new DataProtectionFieldEncryptionService(new EphemeralDataProtectionProvider());
}