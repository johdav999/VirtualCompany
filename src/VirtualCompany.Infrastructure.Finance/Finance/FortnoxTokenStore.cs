using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Security;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FortnoxTokenStore : IFortnoxTokenStore
{
    private const string AccessTokenPurpose = "fortnox:access_token";
    private const string RefreshTokenPurpose = "fortnox:refresh_token";
    private const string TokenEncryptionAlgorithm = "aspnet-data-protection-v1";
    private const string TokenEncryptionKeyId = "tenant-scoped-data-protection";

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IFieldEncryptionService _fieldEncryption;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FortnoxTokenStore>? _logger;

    public FortnoxTokenStore(
        VirtualCompanyDbContext dbContext,
        IFieldEncryptionService fieldEncryption,
        TimeProvider? timeProvider = null,
        ILogger<FortnoxTokenStore>? logger = null)
    {
        _dbContext = dbContext;
        _fieldEncryption = fieldEncryption;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    public async Task<FortnoxTokenSnapshot?> GetAsync(Guid companyId, Guid? connectionId, CancellationToken cancellationToken)
    {
        var query = _dbContext.FortnoxConnections
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId);

        if (connectionId.HasValue)
        {
            query = query.Where(x => x.Id == connectionId.Value);
        }

        var connection = await query.SingleOrDefaultAsync(cancellationToken);
        if (connection is null)
        {
            return null;
        }

        try
        {
            return ToSnapshot(connection, includeTokens: true);
        }
        catch (CryptographicException)
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            connection.SetStatus(
                FortnoxConnectionStatus.NeedsReconnect,
                "Fortnox credentials can no longer be decrypted. Reconnect Fortnox to continue.",
                now);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger?.LogWarning(
                "Stored Fortnox credentials could not be decrypted and the connection was marked for reconnection. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}.",
                companyId,
                connection.Id);

            return ToSnapshot(connection, includeTokens: false);
        }
    }

    public async Task<FortnoxTokenSnapshot?> GetStatusAsync(Guid companyId, Guid? connectionId, CancellationToken cancellationToken)
    {
        var query = _dbContext.FortnoxConnections
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId);

        if (connectionId.HasValue)
        {
            query = query.Where(x => x.Id == connectionId.Value);
        }

        var connection = await query.SingleOrDefaultAsync(cancellationToken);

        // Status surfaces must never decrypt or expose token material.
        return connection is null
            ? null
            : ToSnapshot(connection, includeTokens: false);
    }

    public async Task<FortnoxTokenSnapshot> UpsertConnectedAsync(
        Guid companyId,
        Guid userId,
        FortnoxOAuthTokenResult tokenResult,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var connection = await _dbContext.FortnoxConnections
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);

        if (connection is null)
        {
            connection = new FortnoxConnection(Guid.NewGuid(), companyId, userId, nowUtc);
            _dbContext.FortnoxConnections.Add(connection);
        }

        connection.StoreEncryptedTokens(
            EncryptAccess(companyId, tokenResult.AccessToken),
            EncryptRefresh(companyId, tokenResult.RefreshToken),
            tokenResult.AccessTokenExpiresUtc,
            tokenResult.GrantedScopes,
            tokenResult.ProviderTenantId,
            nowUtc,
            tokenEncryptionKeyId: TokenEncryptionKeyId,
            tokenEncryptionAlgorithm: TokenEncryptionAlgorithm);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToSnapshot(connection, includeTokens: false);
    }

    public async Task<FortnoxTokenSnapshot> StoreRefreshResultAsync(
        Guid companyId,
        Guid connectionId,
        FortnoxOAuthTokenResult tokenResult,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var connection = await _dbContext.FortnoxConnections
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == connectionId, cancellationToken)
            ?? throw new FortnoxOAuthException("Fortnox is not connected.", requiresReconnect: true);

        connection.RecordRefreshAttempt(nowUtc);
        connection.StoreRefreshedTokens(
            EncryptAccess(companyId, tokenResult.AccessToken),
            EncryptRefresh(companyId, tokenResult.RefreshToken),
            tokenResult.AccessTokenExpiresUtc,
            tokenResult.GrantedScopes,
            nowUtc);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToSnapshot(connection, includeTokens: false);
    }

    public async Task MarkAsync(
        Guid companyId,
        Guid connectionId,
        string status,
        string safeReason,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var connection = await _dbContext.FortnoxConnections
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == connectionId, cancellationToken);

        if (connection is null)
        {
            return;
        }

        connection.RecordRefreshAttempt(nowUtc);
        connection.SetStatus(FortnoxConnectionStatusValues.Parse(status), safeReason, nowUtc);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<FortnoxTokenSnapshot?> DisconnectAsync(Guid companyId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var connection = await _dbContext.FortnoxConnections
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);

        if (connection is null)
        {
            return null;
        }

        connection.SetStatus(
            FortnoxConnectionStatus.Disconnected,
            "Fortnox was disconnected by a company administrator.",
            nowUtc);
        connection.ClearTokenMaterial(nowUtc);

        // Stored token material is no longer returned by the application after disconnect.
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ToSnapshot(connection, includeTokens: false);
    }

    private FortnoxTokenSnapshot ToSnapshot(FortnoxConnection connection, bool includeTokens)
    {
        var mayReturnTokens = includeTokens && connection.Status is not FortnoxConnectionStatus.Disconnected and not FortnoxConnectionStatus.Revoked and not FortnoxConnectionStatus.NeedsReconnect;
        var accessToken = mayReturnTokens && !string.IsNullOrWhiteSpace(connection.EncryptedAccessToken)
            ? DecryptAccess(connection.CompanyId, connection.EncryptedAccessToken)
            : null;
        var refreshToken = mayReturnTokens && !string.IsNullOrWhiteSpace(connection.EncryptedRefreshToken)
            ? DecryptRefresh(connection.CompanyId, connection.EncryptedRefreshToken)
            : null;

        return new FortnoxTokenSnapshot(
            connection.Id,
            connection.CompanyId,
            connection.Status.ToStorageValue(),
            accessToken,
            refreshToken,
            connection.AccessTokenExpiresUtc,
            connection.GrantedScopes,
            connection.ProviderTenantId,
            connection.ConnectedUtc,
            connection.LastRefreshAttemptUtc,
            connection.LastErrorSummary,
            connection.LastSyncUtc);
    }

    private string EncryptAccess(Guid companyId, string plaintext) =>
        _fieldEncryption.Encrypt(companyId, AccessTokenPurpose, plaintext);

    private string EncryptRefresh(Guid companyId, string plaintext) =>
        _fieldEncryption.Encrypt(companyId, RefreshTokenPurpose, plaintext);

    private string DecryptAccess(Guid companyId, string ciphertext) =>
        _fieldEncryption.Decrypt(companyId, AccessTokenPurpose, ciphertext);

    private string DecryptRefresh(Guid companyId, string ciphertext) =>
        _fieldEncryption.Decrypt(companyId, RefreshTokenPurpose, ciphertext);
}
