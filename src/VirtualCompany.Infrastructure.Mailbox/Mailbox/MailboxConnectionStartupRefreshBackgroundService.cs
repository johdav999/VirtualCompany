using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Security;

namespace VirtualCompany.Infrastructure.Mailbox;

public sealed class MailboxConnectionStartupRefreshBackgroundService : BackgroundService
{
    private static readonly TimeSpan HealthInterval = TimeSpan.FromMinutes(15);
    internal static readonly string[] TransientHealthFailureCodes =
    [
        "mail_server_unavailable",
        "mailbox_capacity_limited",
        "provider_unavailable",
        "throttled",
        "timeout"
    ];
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MailboxConnectionStartupRefreshBackgroundService> _logger;

    public MailboxConnectionStartupRefreshBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<MailboxConnectionStartupRefreshBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                var companyIds = await GetCompaniesWithActiveMailboxesAsync(stoppingToken);
                foreach (var companyId in companyIds)
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var companyScopeFactory = scope.ServiceProvider.GetRequiredService<ICompanyExecutionScopeFactory>();
                        using var companyScope = companyScopeFactory.BeginScope(companyId);
                        var refresher = scope.ServiceProvider.GetRequiredService<MailboxConnectionCredentialRefresher>();
                        await refresher.RefreshExpiringConnectionsAsync(companyId, stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex, "Mailbox restoration failed for company {CompanyId}; remaining companies will continue.", companyId);
                    }
                }

                await Task.Delay(HealthInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mailbox connections could not be restored during application startup.");
        }
    }

    private async Task<Guid[]> GetCompaniesWithActiveMailboxesAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        return await dbContext.MailboxConnections
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(connection => connection.Status == MailboxConnectionStatus.Active ||
                (connection.Status == MailboxConnectionStatus.Failed &&
                 TransientHealthFailureCodes.Contains(connection.LastErrorCode!)))
            .Select(connection => connection.CompanyId)
            .Distinct()
            .ToArrayAsync(cancellationToken);
    }
}

public sealed class MailboxConnectionCredentialRefresher
{
    private static readonly TimeSpan RefreshWindow = TimeSpan.FromMinutes(10);
    private const string ReconnectMessage = "Automatic mailbox authentication could not be restored. Reconnect this mailbox.";

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IMailboxProviderRegistry _providerRegistry;
    private readonly IFieldEncryptionService _fieldEncryption;
    private readonly IMailboxOAuthAccessTokenLeaseService? _tokenLeaseService;
    private readonly IMailboxTransportRegistry? _transportRegistry;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MailboxConnectionCredentialRefresher> _logger;

    public MailboxConnectionCredentialRefresher(
        VirtualCompanyDbContext dbContext,
        IMailboxProviderRegistry providerRegistry,
        IFieldEncryptionService fieldEncryption,
        IMailboxOAuthAccessTokenLeaseService tokenLeaseService,
        IMailboxTransportRegistry transportRegistry,
        TimeProvider timeProvider,
        ILogger<MailboxConnectionCredentialRefresher> logger)
    {
        _dbContext = dbContext;
        _providerRegistry = providerRegistry;
        _fieldEncryption = fieldEncryption;
        _tokenLeaseService = tokenLeaseService;
        _transportRegistry = transportRegistry;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public MailboxConnectionCredentialRefresher(
        VirtualCompanyDbContext dbContext,
        IMailboxProviderRegistry providerRegistry,
        IFieldEncryptionService fieldEncryption,
        TimeProvider timeProvider,
        ILogger<MailboxConnectionCredentialRefresher> logger)
        : this(dbContext, providerRegistry, fieldEncryption, null!, null!, timeProvider, logger)
    {
    }

    public async Task RefreshExpiringConnectionsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var connections = await _dbContext.MailboxConnections
            .Where(connection => connection.CompanyId == companyId &&
                (connection.Status == MailboxConnectionStatus.Active ||
                 (connection.Status == MailboxConnectionStatus.Failed &&
                  MailboxConnectionStartupRefreshBackgroundService.TransientHealthFailureCodes.Contains(connection.LastErrorCode!))))
            .ToArrayAsync(cancellationToken);

        foreach (var connection in connections)
        {
            try
            {
                if (connection.Provider == MailboxProvider.StandardEmail)
                {
                    if (connection.AuthenticationType == MailboxAuthenticationType.OAuth2 && RequiresRefresh(connection, now))
                    {
                        await RefreshConnectionAsync(connection, cancellationToken);
                        if (connection.Status != MailboxConnectionStatus.Active)
                        {
                            continue;
                        }
                    }

                    var context = StandardMailboxSessionCodec.Decode(StandardMailboxSessionCodec.Create(connection, _fieldEncryption));
                    if (_transportRegistry is not null)
                    {
                        var health = await _transportRegistry.Resolve(MailKitMailboxTransport.Key).TestAsync(context, cancellationToken);
                        connection.RecordHealthCheck(
                            now,
                            health.ImapSucceeded && health.SmtpSucceeded,
                            health.SafeFailureMessage,
                            health.SafeFailureCode);
                        if (!health.ImapSucceeded || !health.SmtpSucceeded)
                        {
                            connection.SetStatus(
                                MailboxConnectionStatus.Failed,
                                health.SafeFailureMessage ?? ReconnectMessage,
                                health.SafeFailureCode);
                            continue;
                        }
                    }
                    connection.SetStatus(MailboxConnectionStatus.Active);
                    _logger.LogInformation(
                        "Hosted mailbox connection restored from encrypted credentials. CompanyId: {CompanyId}. Purpose: {Purpose}. ConnectionId: {ConnectionId}.",
                        companyId,
                        connection.Purpose,
                        connection.Id);
                    continue;
                }

                if (_tokenLeaseService is null)
                {
                    throw new InvalidOperationException("Mailbox OAuth token leasing is unavailable.");
                }

                var provider = _providerRegistry.Resolve(connection.Provider);
                await _tokenLeaseService.AcquireAsync(
                    connection.CompanyId,
                    connection.Id,
                    provider.ReadRequiredScopes,
                    cancellationToken);
                connection.SetStatus(MailboxConnectionStatus.Active);
                _logger.LogInformation(
                    "Mailbox connection restored from its external account credentials. CompanyId: {CompanyId}. Purpose: {Purpose}. Provider: {Provider}. ConnectionId: {ConnectionId}.",
                    companyId,
                    connection.Purpose,
                    connection.Provider,
                    connection.Id);
            }
            catch (CryptographicException)
            {
                connection.SetStatus(MailboxConnectionStatus.TokenExpired, ReconnectMessage);
                _logger.LogWarning(
                    "Persisted mailbox credentials were encrypted with an unavailable key and require reconnection. CompanyId: {CompanyId}. Purpose: {Purpose}. Provider: {Provider}. ConnectionId: {ConnectionId}.",
                    connection.CompanyId,
                    connection.Purpose,
                    connection.Provider,
                    connection.Id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                connection.SetStatus(MailboxConnectionStatus.Failed, ReconnectMessage);
                _logger.LogWarning(
                    ex,
                    "Mailbox restoration failed. CompanyId: {CompanyId}. Purpose: {Purpose}. Provider: {Provider}. ConnectionId: {ConnectionId}.",
                    connection.CompanyId,
                    connection.Purpose,
                    connection.Provider,
                    connection.Id);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static bool RequiresRefresh(MailboxConnection connection, DateTime now) =>
        string.IsNullOrWhiteSpace(connection.EncryptedAccessToken) ||
        (connection.AccessTokenExpiresUtc.HasValue && connection.AccessTokenExpiresUtc.Value <= now.Add(RefreshWindow));

    private void ValidateAccessToken(MailboxConnection connection)
    {
        _fieldEncryption.Decrypt(
            connection.CompanyId,
            StandardMailboxCredentialPurposes.AccessToken(connection.Id),
            connection.EncryptedAccessToken!);
    }

    private async Task RefreshConnectionAsync(MailboxConnection connection, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(connection.EncryptedRefreshToken))
        {
            connection.SetStatus(MailboxConnectionStatus.TokenExpired, ReconnectMessage);
            return;
        }

        try
        {
            var provider = _providerRegistry.Resolve(connection.Provider);
            var refreshToken = _fieldEncryption.Decrypt(
                connection.CompanyId,
                StandardMailboxCredentialPurposes.RefreshToken(connection.Id),
                connection.EncryptedRefreshToken);
            var tokenResult = await provider.RefreshTokenAsync(
                new MailboxRefreshTokenRequest(refreshToken, connection.ProfileKey),
                cancellationToken);

            connection.StoreEncryptedCredentials(
                _fieldEncryption.Encrypt(
                    connection.CompanyId,
                    StandardMailboxCredentialPurposes.AccessToken(connection.Id),
                    tokenResult.AccessToken),
                string.IsNullOrWhiteSpace(tokenResult.RefreshToken)
                    ? connection.EncryptedRefreshToken
                    : _fieldEncryption.Encrypt(
                        connection.CompanyId,
                        StandardMailboxCredentialPurposes.RefreshToken(connection.Id),
                        tokenResult.RefreshToken),
                tokenResult.AccessTokenExpiresUtc,
                tokenResult.GrantedScopes.Count > 0 ? tokenResult.GrantedScopes : connection.GrantedScopes);
            connection.SetStatus(MailboxConnectionStatus.Active);

            _logger.LogInformation(
                "Mailbox authentication refreshed during startup. CompanyId: {CompanyId}. Purpose: {Purpose}. Provider: {Provider}. ConnectionId: {ConnectionId}.",
                connection.CompanyId,
                connection.Purpose,
                connection.Provider,
                connection.Id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or MailboxProviderExecutionException)
        {
            connection.SetStatus(MailboxConnectionStatus.Failed, ReconnectMessage);
            _logger.LogWarning(
                ex,
                "Mailbox authentication refresh failed during startup. CompanyId: {CompanyId}. Purpose: {Purpose}. Provider: {Provider}. ConnectionId: {ConnectionId}.",
                connection.CompanyId,
                connection.Purpose,
                connection.Provider,
                connection.Id);
        }
    }
}
