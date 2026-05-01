using Microsoft.EntityFrameworkCore;
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

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IFieldEncryptionService _fieldEncryption;

    public FortnoxTokenStore(
        VirtualCompanyDbContext dbContext,
        IFieldEncryptionService fieldEncryption)
    {
        _dbContext = dbContext;
        _fieldEncryption = fieldEncryption;
    }

    public async Task<FortnoxTokenSnapshot?> GetAsync(Guid companyId, Guid? connectionId, CancellationToken cancellationToken)
    {
        var query = _dbContext.FortnoxConnections
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId);

        if (connectionId.HasValue)
        {
            query = query.Where(x => x.Id == connectionId.Value);
        }

        var connection = await query.SingleOrDefaultAsync(cancellationToken);
        return connection is null ? null : ToSnapshot(connection, includeTokens: true);
    }

    public async Task<FortnoxTokenSnapshot> UpsertConnectedAsync(
        Guid companyId,
        Guid userId,
        FortnoxOAuthTokenResult tokenResult,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var connection = await _dbContext.FortnoxConnections
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
            nowUtc);

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
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);

        if (connection is null)
        {
            return null;
        }

        connection.SetStatus(
            FortnoxConnectionStatus.Disconnected,
            "Fortnox was disconnected by a company administrator.",
            nowUtc);

        // Stored token material is no longer returned by the application after disconnect.
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ToSnapshot(connection, includeTokens: false);
    }

    private FortnoxTokenSnapshot ToSnapshot(FortnoxConnection connection, bool includeTokens)
    {
        var accessToken = includeTokens && !string.IsNullOrWhiteSpace(connection.EncryptedAccessToken)
            ? DecryptAccess(connection.CompanyId, connection.EncryptedAccessToken)
            : null;
        var refreshToken = includeTokens && !string.IsNullOrWhiteSpace(connection.EncryptedRefreshToken)
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
            connection.ProviderTenantId);
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