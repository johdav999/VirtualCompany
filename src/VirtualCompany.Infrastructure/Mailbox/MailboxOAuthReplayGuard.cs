using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Mailbox;

public sealed class MailboxOAuthReplayGuard : IMailboxOAuthReplayGuard
{
    private readonly VirtualCompanyDbContext _dbContext;

    public MailboxOAuthReplayGuard(VirtualCompanyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task RegisterAsync(
        Guid companyId,
        Guid userId,
        MailboxPurpose purpose,
        MailboxProvider provider,
        string nonce,
        DateTime expiresUtc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(nonce)) throw new ArgumentException("OAuth nonce is required.", nameof(nonce));
        var createdUtc = DateTime.UtcNow;
        _dbContext.Set<MailboxOAuthAuthorizationState>().Add(new MailboxOAuthAuthorizationState(
            Guid.NewGuid(),
            companyId,
            userId,
            purpose,
            provider,
            Hash(nonce),
            createdUtc,
            expiresUtc));
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> TryConsumeAsync(
        Guid companyId,
        Guid userId,
        MailboxPurpose purpose,
        MailboxProvider provider,
        string nonce,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(nonce)) return false;
        var hash = Hash(nonce);
        var affected = await _dbContext.Set<MailboxOAuthAuthorizationState>()
            .IgnoreQueryFilters()
            .Where(state => state.CompanyId == companyId &&
                state.UserId == userId &&
                state.Purpose == purpose &&
                state.Provider == provider &&
                state.NonceHash == hash &&
                state.ConsumedUtc == null &&
                state.ExpiresUtc > nowUtc)
            .ExecuteUpdateAsync(
                updates => updates.SetProperty(state => state.ConsumedUtc, nowUtc),
                cancellationToken);

        return affected == 1;
    }

    private static string Hash(string nonce) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(nonce))).ToLowerInvariant();
}
