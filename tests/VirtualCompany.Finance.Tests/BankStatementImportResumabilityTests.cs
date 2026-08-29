using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Finance.Tests;

public sealed class BankStatementImportResumabilityTests
{
    [Fact]
    public async Task Commit_checkpoints_each_chunk_and_resume_does_not_replay_completed_rows()
    {
        await using var db = CreateDb(); var company = Guid.NewGuid(); var user = Guid.NewGuid();
        var account = Seed(db, company, user); var now = Utc(2026, 8, 28, 12);
        var job = new BankStatementImportJob(Guid.NewGuid(), company, account.Id, "statement.xml", "application/xml", 100,
            $"companies/{company:N}/finance/statement-imports/source.xml", new string('a', 64), user, now);
        job.CompletePreview(BankStatementImportFormats.Camt053, "camt.053.001.08", "test-v1", "statement-1",
            "0003", "SEK", 100m, 120m, 0m, 20m, 2, 2, 0, 0, null, null, false, now);
        db.BankStatementImportJobs.Add(job);
        db.BankStatementImportJobRows.AddRange(Row(company, job.Id, 1, "row-1", 10m, now), Row(company, job.Id, 2, "row-2", 10m, now));
        await db.SaveChangesAsync();
        var bank = new RecordingBankTransactions(); var service = Service(db, bank, company, user, batchSize: 1);

        var first = await service.CommitAsync(new(company, job.Id, job.Version, user), default);
        Assert.Equal(BankStatementImportJobStatuses.PartiallyImported, first.Status); Assert.Equal(1, first.ImportedRowCount);
        var second = await service.CommitAsync(new(company, job.Id, first.Version, user), default);
        Assert.Equal(BankStatementImportJobStatuses.Completed, second.Status); Assert.Equal(2, second.ImportedRowCount);
        var replay = await service.CommitAsync(new(company, job.Id, second.Version, user), default);
        Assert.Equal(BankStatementImportJobStatuses.Completed, replay.Status);
        Assert.Collection(bank.Commands,
            command => Assert.Equal("row-1", Assert.Single(command.Rows).RowIdentity),
            command => Assert.Equal("row-2", Assert.Single(command.Rows).RowIdentity));
        Assert.Equal(2, await db.BankStatementImportJobRows.IgnoreQueryFilters().CountAsync(x => x.ProcessedUtc != null));
    }

    [Fact]
    public async Task Active_company_context_rejects_cross_company_reads_and_writes_before_storage_or_parsing()
    {
        await using var db = CreateDb(); var company = Guid.NewGuid(); var other = Guid.NewGuid(); var user = Guid.NewGuid();
        Seed(db, company, user); var service = Service(db, new RecordingBankTransactions(), company, user, 10);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetWorkspaceAsync(other, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.PreviewAsync(new(other, Guid.NewGuid(),
            "cross.csv", "text/csv", 1, new MemoryStream([1]), null, null, user), default));
    }

    [Fact]
    public async Task Preview_scans_then_parses_the_recoverable_object_storage_copy()
    {
        await using var db = CreateDb(); var company = Guid.NewGuid(); var user = Guid.NewGuid();
        var account = Seed(db, company, user); var storage = new RecoverableStorage();
        var scanner = new RecordingScanner(storage); var parser = new RecordingParser(scanner);
        var service = new BankStatementImportCenterService(db, [parser], storage, scanner,
            new RecordingBankTransactions(), new AuditStub(), Options.Create(new BankStatementImportOptions()),
            new FixedTimeProvider(Utc(2026, 8, 28, 13)), new Context(company, user));
        var source = Encoding.UTF8.GetBytes("durable-statement-payload");

        var preview = await service.PreviewAsync(new(company, account.Id, "statement.xml", "application/xml",
            source.Length, new MemoryStream(source), null, null, user), default);

        Assert.True(scanner.WasCalled);
        Assert.Equal("durable-statement-payload", parser.SeenContent);
        Assert.StartsWith($"companies/{company:N}/finance/statement-imports/", storage.StorageKey,
            StringComparison.Ordinal);
        Assert.Equal(64, preview.Checksum.Length);
        Assert.Equal(BankStatementImportJobStatuses.ReadyToImport, preview.Status);
        Assert.Single(preview.Rows);
    }

    private static BankStatementImportJobRow Row(Guid company, Guid job, int number, string identity, decimal amount, DateTime now) =>
        new(Guid.NewGuid(), company, job, number, identity, new string((char)('a' + number), 64), now, now,
            amount, "SEK", identity, "Counterparty", identity, BankStatementImportRowOutcomes.Accepted,
            null, null, null, null, now);
    private static CompanyBankAccount Seed(VirtualCompanyDbContext db, Guid company, Guid user)
    {
        db.Companies.Add(new Company(company, "Statement import company"));
        db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), company, user,
            CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
        var finance = new FinanceAccount(Guid.NewGuid(), company, "1930", "Operating bank", "asset", "SEK", 0, DateTime.UtcNow);
        var account = new CompanyBankAccount(Guid.NewGuid(), company, finance.Id, "Operating account", "Test bank", "•••• 0003", "SEK");
        db.FinanceAccounts.Add(finance); db.CompanyBankAccounts.Add(account); db.SaveChanges(); return account;
    }
    private static BankStatementImportCenterService Service(VirtualCompanyDbContext db, RecordingBankTransactions bank,
        Guid company, Guid user, int batchSize) => new(db, [], new MemoryStorage(), new CleanScanner(), bank,
            new AuditStub(), Options.Create(new BankStatementImportOptions { CommitBatchSize = batchSize }),
            new FixedTimeProvider(Utc(2026, 8, 28, 13)), new Context(company, user));
    private static VirtualCompanyDbContext CreateDb()
    {
        var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlite("Data Source=:memory:;Foreign Keys=False").Options);
        db.Database.OpenConnection(); db.Database.EnsureCreated(); return db;
    }
    private static DateTime Utc(int y, int m, int d, int h) => new(y, m, d, h, 0, 0, DateTimeKind.Utc);
    private sealed class FixedTimeProvider(DateTime now) : TimeProvider { public override DateTimeOffset GetUtcNow() => new(now); }
    private sealed class AuditStub : IAuditEventWriter { public Task WriteAsync(AuditEventWriteRequest auditEvent, CancellationToken cancellationToken) => Task.CompletedTask; }
    private sealed class CleanScanner : ICompanyDocumentVirusScanner { public Task<CompanyDocumentVirusScanResult> ScanAsync(CompanyDocumentVirusScanRequest request, CancellationToken cancellationToken) => Task.FromResult(CompanyDocumentVirusScanResult.CleanPlaceholder()); }
    private sealed class RecordingScanner(RecoverableStorage storage) : ICompanyDocumentVirusScanner
    {
        public bool WasCalled { get; private set; }
        public Task<CompanyDocumentVirusScanResult> ScanAsync(CompanyDocumentVirusScanRequest request,
            CancellationToken cancellationToken)
        {
            Assert.Equal(storage.StorageKey, request.StorageKey); Assert.NotEmpty(storage.Content);
            WasCalled = true; return Task.FromResult(CompanyDocumentVirusScanResult.CleanPlaceholder());
        }
    }
    private sealed class RecordingParser(RecordingScanner scanner) : IBankStatementFileParser
    {
        public string? SeenContent { get; private set; }
        public bool Supports(string fileName, string? contentType) => true;
        public async Task<ParsedBankStatement> ParseAsync(BankStatementParseRequest request, Stream content,
            CancellationToken cancellationToken)
        {
            Assert.True(scanner.WasCalled);
            using var reader = new StreamReader(content, Encoding.UTF8, leaveOpen: true);
            SeenContent = await reader.ReadToEndAsync(cancellationToken);
            var booked = Utc(2026, 8, 27, 0);
            return new(BankStatementImportFormats.Camt053, "camt.053.001.08", "test-v1", "statement-1",
                "0003", "SEK", 100m, 110m,
                [new ParsedBankStatementRow(1, "row-1", booked, booked, 10m, "SEK", "Invoice 1",
                    "Customer", "external-1", null, [])], [], false);
        }
    }
    private sealed class RecoverableStorage : ICompanyDocumentStorage
    {
        public string StorageKey { get; private set; } = string.Empty;
        public byte[] Content { get; private set; } = [];
        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
        {
            Assert.Equal(StorageKey, storageKey);
            return Task.FromResult<Stream>(new MemoryStream(Content, writable: false));
        }
        public async Task<DocumentStorageWriteResult> WriteAsync(DocumentStorageWriteRequest request,
            CancellationToken cancellationToken)
        {
            StorageKey = request.StorageKey;
            using var copy = new MemoryStream(); await request.Content.CopyToAsync(copy, cancellationToken);
            Content = copy.ToArray();
            return new(StorageKey, null);
        }
    }
    private sealed class MemoryStorage : ICompanyDocumentStorage
    {
        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken) => Task.FromResult<Stream>(new MemoryStream());
        public Task<DocumentStorageWriteResult> WriteAsync(DocumentStorageWriteRequest request, CancellationToken cancellationToken) => Task.FromResult(new DocumentStorageWriteResult(request.StorageKey, null));
    }
    private sealed class Context(Guid company, Guid user) : ICompanyContextAccessor
    {
        public Guid? CompanyId { get; private set; } = company; public Guid? UserId { get; } = user; public bool IsResolved => true;
        public ResolvedCompanyMembershipContext? Membership => null;
        public void SetCompanyId(Guid? companyId) => CompanyId = companyId;
        public void SetCompanyContext(ResolvedCompanyMembershipContext? companyContext) => CompanyId = companyContext?.CompanyId;
    }
    private sealed class RecordingBankTransactions : IBankTransactionCommandService
    {
        public List<ImportBankStatementCommand> Commands { get; } = [];
        public Task<BankStatementImportResultDto> ImportStatementAsync(ImportBankStatementCommand command, CancellationToken cancellationToken)
        { Commands.Add(command); return Task.FromResult(new BankStatementImportResultDto(Guid.NewGuid(), command.Rows.Count, 0, 0, false, [])); }
        public Task<BankTransactionDetailDto> ReconcileAsync(ReconcileBankTransactionCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BankReconciliationDetailDto> ReclassifySuspenseAsync(ReclassifyBankSuspenseCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
