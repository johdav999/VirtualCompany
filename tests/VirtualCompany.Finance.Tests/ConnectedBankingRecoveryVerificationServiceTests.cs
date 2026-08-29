using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

public sealed class ConnectedBankingRecoveryVerificationServiceTests
{
    private static readonly DateTime VerifiedUtc = new(2026, 8, 29, 10, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Verification_is_deterministic_company_scoped_and_validates_retained_statement_content()
    {
        await using var db = await CreateDatabaseAsync();
        var companyId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var content = "restored-statement-content"u8.ToArray();
        const string storageKey = "companies/company/finance/statements/restored.bin";
        var checksum = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var storage = new MemoryStorage((storageKey, content));

        db.BankConnections.Add(new BankConnection(Guid.NewGuid(), companyId, "test", "SE|Restore",
            "Restore Bank", actorId, VerifiedUtc.AddDays(-30)));
        db.BankStatementImportJobs.Add(new BankStatementImportJob(Guid.NewGuid(), companyId, Guid.NewGuid(),
            "restored.bin", "application/octet-stream", content.Length, storageKey, checksum, actorId,
            VerifiedUtc.AddDays(-1)));
        db.BankConnections.Add(new BankConnection(Guid.NewGuid(), Guid.NewGuid(), "test", "SE|Foreign",
            "Foreign Bank", actorId, VerifiedUtc.AddDays(-30)));
        await db.SaveChangesAsync();

        var service = Service(db, storage, companyId);
        var first = await service.VerifyAsync(new VerifyConnectedBankingRecoveryCommand(
            companyId, true, actorId, "restore-drill-1"), default);
        var second = await service.VerifyAsync(new VerifyConnectedBankingRecoveryCommand(
            companyId, true, actorId, "restore-drill-2"), default);

        Assert.True(first.IsValid);
        Assert.True(first.ObjectContentVerified);
        Assert.Equal(1, first.ConnectionCount);
        Assert.Equal(1, first.StatementImportCount);
        Assert.Equal(64, first.EvidenceChecksum.Length);
        Assert.Equal(first.EvidenceChecksum, second.EvidenceChecksum);
        Assert.Equal(VerifiedUtc, first.VerifiedUtc);
        Assert.Empty(first.Issues);
    }

    [Fact]
    public async Task Verification_blocks_when_retained_statement_content_is_missing_or_changed()
    {
        await using var db = await CreateDatabaseAsync();
        var companyId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var expected = "expected-statement-content"u8.ToArray();
        var changed = "changed-statement-content!"u8.ToArray();
        var expectedChecksum = Convert.ToHexString(SHA256.HashData(expected)).ToLowerInvariant();
        const string changedKey = "companies/company/finance/statements/changed.bin";
        const string missingKey = "companies/company/finance/statements/missing.bin";
        db.BankStatementImportJobs.AddRange(
            new BankStatementImportJob(Guid.NewGuid(), companyId, Guid.NewGuid(), "changed.bin",
                "application/octet-stream", expected.Length, changedKey, expectedChecksum, actorId,
                VerifiedUtc.AddDays(-1)),
            new BankStatementImportJob(Guid.NewGuid(), companyId, Guid.NewGuid(), "missing.bin",
                "application/octet-stream", expected.Length, missingKey, expectedChecksum, actorId,
                VerifiedUtc.AddDays(-1)));
        await db.SaveChangesAsync();

        var result = await Service(db, new MemoryStorage((changedKey, changed)), companyId)
            .VerifyAsync(new VerifyConnectedBankingRecoveryCommand(companyId, true, actorId), default);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues,
            issue => issue.ReasonCode == ConnectedBankingRecoveryReasonCodes.StatementObjectHashMismatch);
        Assert.Contains(result.Issues,
            issue => issue.ReasonCode == ConnectedBankingRecoveryReasonCodes.StatementObjectMissing);
    }

    [Fact]
    public async Task Resolved_tenant_context_rejects_cross_company_verification()
    {
        await using var db = await CreateDatabaseAsync();
        var service = Service(db, new MemoryStorage(), Guid.NewGuid());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.VerifyAsync(
            new VerifyConnectedBankingRecoveryCommand(Guid.NewGuid(), false, Guid.NewGuid()), default));
    }

    private static ConnectedBankingRecoveryVerificationService Service(
        VirtualCompanyDbContext db, ICompanyDocumentStorage storage, Guid companyId) =>
        new(db, storage, new FixedTimeProvider(VerifiedUtc), new Context(companyId));

    private static async Task<VirtualCompanyDbContext> CreateDatabaseAsync()
    {
        var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlite("Data Source=:memory:;Foreign Keys=False").Options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }

    private sealed class Context(Guid companyId) : ICompanyContextAccessor
    {
        public Guid? CompanyId { get; private set; } = companyId;
        public Guid? UserId => null;
        public bool IsResolved => true;
        public ResolvedCompanyMembershipContext? Membership => null;
        public void SetCompanyId(Guid? value) => CompanyId = value;
        public void SetCompanyContext(ResolvedCompanyMembershipContext? value) => CompanyId = value?.CompanyId;
    }

    private sealed class MemoryStorage(params (string Key, byte[] Content)[] files) : ICompanyDocumentStorage
    {
        private readonly Dictionary<string, byte[]> _files = files.ToDictionary(
            item => item.Key, item => item.Content, StringComparer.Ordinal);

        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_files.TryGetValue(storageKey, out var content)) throw new FileNotFoundException(storageKey);
            return Task.FromResult<Stream>(new MemoryStream(content, writable: false));
        }

        public Task<DocumentStorageWriteResult> WriteAsync(
            DocumentStorageWriteRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
