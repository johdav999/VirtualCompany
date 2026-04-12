using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Companies;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Documents;
using VirtualCompany.Infrastructure.Observability;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Api.Tests;

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly TimeProvider _timeProvider;
    private SqliteConnection? _connection;

    public TestWebApplicationFactory()
        : this(TimeProvider.System)
    {
    }

    internal TestWebApplicationFactory(TimeProvider timeProvider) => _timeProvider = timeProvider;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{CompanyOutboxDispatcherOptions.SectionName}:Enabled"] = "false",
                [$"{WorkflowSchedulerOptions.SectionName}:Enabled"] = "false",
                [$"{CompanyOutboxDispatcherOptions.SectionName}:RetryDelaySeconds"] = "0",
                [$"{ObservabilityOptions.SectionName}:RateLimiting:Enabled"] = "false",
                [$"{KnowledgeIndexingOptions.SectionName}:Enabled"] = "false",
                [$"{KnowledgeEmbeddingOptions.SectionName}:Provider"] = "deterministic",
                [$"{ObservabilityOptions.SectionName}:Redis:ConnectionString"] = "",
                [$"{KnowledgeEmbeddingOptions.SectionName}:Dimensions"] = "256",
                [$"{GroundedContextRetrievalCacheOptions.SectionName}:Enabled"] = "true",
                [$"{GroundedContextRetrievalCacheOptions.SectionName}:KeyVersion"] = "tests-v1",
                [$"{GroundedContextRetrievalCacheOptions.SectionName}:KnowledgeTtlSeconds"] = "300",
                [$"{GroundedContextRetrievalCacheOptions.SectionName}:MemoryTtlSeconds"] = "300",
                [$"{ObservabilityOptions.SectionName}:ObjectStorage:Enabled"] = "false"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<VirtualCompanyDbContext>>();
            services.RemoveAll<TimeProvider>();
            services.AddSingleton(_timeProvider);
            services.RemoveAll<VirtualCompanyDbContext>();

            _connection ??= CreateOpenConnection();
            services.AddDbContext<VirtualCompanyDbContext>(options =>
                options.UseSqlite(_connection));

            services.RemoveAll<ICompanyInvitationSender>();
            services.AddSingleton<TestCompanyInvitationSender>();
            services.RemoveAll<ICompanyToolExecutor>();
            services.AddSingleton<TestCompanyToolExecutor>();

            services.AddSingleton<ICompanyToolExecutor>(provider => provider.GetRequiredService<TestCompanyToolExecutor>());
            services.RemoveAll<ICompanyDocumentStorage>();
            services.AddSingleton<TestCompanyDocumentStorage>();
            services.AddSingleton<ICompanyDocumentStorage>(provider => provider.GetRequiredService<TestCompanyDocumentStorage>());
            services.RemoveAll<ICompanyDocumentVirusScanner>();
            services.AddSingleton<TestCompanyDocumentVirusScanner>();
            services.AddSingleton<ICompanyDocumentVirusScanner>(provider => provider.GetRequiredService<TestCompanyDocumentVirusScanner>());

            services.AddSingleton<ICompanyInvitationSender>(provider => provider.GetRequiredService<TestCompanyInvitationSender>());
        });
    }

    public TestCompanyInvitationSender InvitationSender =>
        Services.GetRequiredService<TestCompanyInvitationSender>();

    public TestCompanyToolExecutor ToolExecutor =>
        Services.GetRequiredService<TestCompanyToolExecutor>();

    public TestCompanyDocumentStorage DocumentStorage =>
        Services.GetRequiredService<TestCompanyDocumentStorage>();

    public TestCompanyDocumentVirusScanner DocumentVirusScanner =>
        Services.GetRequiredService<TestCompanyDocumentVirusScanner>();

    public async Task SeedAsync(Func<VirtualCompanyDbContext, Task> seed)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        await dbContext.Database.EnsureCreatedAsync();
        await seed(dbContext);
        await dbContext.SaveChangesAsync();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _connection?.Dispose();
            _connection = null;
        }
    }

    private static SqliteConnection CreateOpenConnection()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        return connection;
    }

    public sealed class TestCompanyInvitationSender : ICompanyInvitationSender
    {
        private readonly ConcurrentQueue<CompanyInvitationDeliveryRequestedMessage> _sent = new();
        private int _attemptCount;
        private int _remainingFailures;

        public IReadOnlyList<CompanyInvitationDeliveryRequestedMessage> Sent => _sent.ToArray();
        public int AttemptCount => Volatile.Read(ref _attemptCount);

        public void Reset()
        {
            while (_sent.TryDequeue(out _))
            {
            }

            Interlocked.Exchange(ref _attemptCount, 0);
            Interlocked.Exchange(ref _remainingFailures, 0);
        }

        public void FailNext(int count = 1) =>
            Interlocked.Exchange(ref _remainingFailures, Math.Max(0, count));

        public Task<CompanyInvitationSendResult> SendAsync(CompanyInvitationDeliveryRequestedMessage invitation, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _attemptCount);

            if (TryConsumeFailure())
            {
                throw new InvalidOperationException("Configured invitation delivery failure.");
            }

            _sent.Enqueue(invitation);
            return Task.FromResult(new CompanyInvitationSendResult($"test:{invitation.InvitationId:N}:{AttemptCount}"));
        }

        private bool TryConsumeFailure()
        {
            while (true)
            {
                var remaining = Volatile.Read(ref _remainingFailures);
                if (remaining <= 0)
                {
                    return false;
                }

                if (Interlocked.CompareExchange(ref _remainingFailures, remaining - 1, remaining) == remaining)
                {
                    return true;
                }
            }
        }
    }

    public sealed class TestCompanyToolExecutor : ICompanyToolExecutor
    {
        private readonly ConcurrentQueue<ToolExecutionRequest> _requests = new();

        public IReadOnlyList<ToolExecutionRequest> Requests => _requests.ToArray();
        public int ExecutionCount => _requests.Count;

        public void Reset()
        {
            while (_requests.TryDequeue(out _))
            {
            }
        }

        public Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken)
        {
            _requests.Enqueue(request);

            var payload = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["toolName"] = JsonValue.Create(request.ToolName),
                ["actionType"] = JsonValue.Create(request.ActionType),
                ["scope"] = string.IsNullOrWhiteSpace(request.Scope) ? null : JsonValue.Create(request.Scope),
                ["executed"] = JsonValue.Create(true)
            };

            return Task.FromResult(new ToolExecutionResult(
                $"Executed '{request.ToolName}' in the test tool executor.",
                payload));
        }
    }

    public sealed class TestCompanyDocumentStorage : ICompanyDocumentStorage
    {
        private readonly ConcurrentDictionary<string, byte[]> _storedObjects = new(StringComparer.OrdinalIgnoreCase);
        private int _remainingFailures;

        public IReadOnlyDictionary<string, byte[]> StoredObjects => _storedObjects;

        public void Reset()
        {
            _storedObjects.Clear();
            Interlocked.Exchange(ref _remainingFailures, 0);
        }

        public void FailNext(int count = 1) =>
            Interlocked.Exchange(ref _remainingFailures, Math.Max(0, count));

        public void Seed(string storageKey, string content) =>
            _storedObjects[storageKey] = Encoding.UTF8.GetBytes(content);

        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_storedObjects.TryGetValue(storageKey, out var bytes))
            {
                throw new FileNotFoundException("Configured document object was not found.", storageKey);
            }

            Stream stream = new MemoryStream(bytes, writable: false);
            return Task.FromResult(stream);
        }

        public async Task<DocumentStorageWriteResult> WriteAsync(DocumentStorageWriteRequest request, CancellationToken cancellationToken)
        {
            if (TryConsumeFailure())
            {
                throw new IOException("Configured document storage failure.");
            }

            await using var buffer = new MemoryStream();
            await request.Content.CopyToAsync(buffer, cancellationToken);
            _storedObjects[request.StorageKey] = buffer.ToArray();
            return new DocumentStorageWriteResult(request.StorageKey, null);
        }

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _storedObjects.TryRemove(storageKey, out _);
            return Task.CompletedTask;
        }

        private bool TryConsumeFailure()
        {
            while (true)
            {
                var remaining = Volatile.Read(ref _remainingFailures);
                if (remaining <= 0)
                {
                    return false;
                }

                if (Interlocked.CompareExchange(ref _remainingFailures, remaining - 1, remaining) == remaining)
                {
                    return true;
                }
            }
        }
    }

    public sealed class TestCompanyDocumentVirusScanner : ICompanyDocumentVirusScanner
    {
        private readonly ConcurrentQueue<CompanyDocumentVirusScanResult> _results = new();
        private Exception? _nextException;
        private int _scanCount;

        public int ScanCount => Volatile.Read(ref _scanCount);

        public void Reset()
        {
            while (_results.TryDequeue(out _))
            {
            }

            _nextException = null;
            Interlocked.Exchange(ref _scanCount, 0);
        }

        public void EnqueueResult(CompanyDocumentVirusScanResult result) =>
            _results.Enqueue(result);

        public void ThrowNext(Exception exception) =>
            _nextException = exception;

        public Task<CompanyDocumentVirusScanResult> ScanAsync(CompanyDocumentVirusScanRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _scanCount);

            var exception = Interlocked.Exchange(ref _nextException, null);
            if (exception is not null)
            {
                throw exception;
            }

            if (_results.TryDequeue(out var configuredResult))
            {
                return Task.FromResult(configuredResult);
            }

            return Task.FromResult(
                CompanyDocumentVirusScanResult.CleanPlaceholder(
                    "test_placeholder_scanner",
                    "1.0",
                    "Test placeholder virus scan completed."));
        }
    }
}
