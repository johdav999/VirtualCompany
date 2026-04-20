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
using VirtualCompany.Application.Finance;
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
    private readonly IReadOnlyDictionary<string, string?> _configurationOverrides;

    public TestWebApplicationFactory()
        : this(TimeProvider.System, null)
    {
    }

    internal TestWebApplicationFactory(TimeProvider timeProvider)
        : this(timeProvider, null)
    {
    }

    internal TestWebApplicationFactory(IReadOnlyDictionary<string, string?> configurationOverrides)
        : this(TimeProvider.System, configurationOverrides)
    {
    }

    internal TestWebApplicationFactory(TimeProvider timeProvider, IReadOnlyDictionary<string, string?>? configurationOverrides) { _timeProvider = timeProvider; _configurationOverrides = configurationOverrides ?? new Dictionary<string, string?>(); }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            var settings = new Dictionary<string, string?>
            {
                [$"{CompanyOutboxDispatcherOptions.SectionName}:Enabled"] = "false",
                [$"{WorkflowSchedulerOptions.SectionName}:Enabled"] = "false",
                [$"{WorkflowProgressionOptions.SectionName}:Enabled"] = "false",
                [$"{TriggerWorkerOptions.SectionName}:Enabled"] = "false",
                [$"{BriefingUpdateJobWorkerOptions.SectionName}:Enabled"] = "false",
                [$"{CompanySimulationOptions.SectionName}:DefaultAutoAdvanceIntervalSeconds"] = "0",
                [$"{CompanySimulationProgressionWorkerOptions.SectionName}:Enabled"] = "false",
                [$"{BriefingSchedulerOptions.SectionName}:Enabled"] = "false",
                [$"{CompanyOutboxDispatcherOptions.SectionName}:RetryDelaySeconds"] = "0",
                [$"{ObservabilityOptions.SectionName}:RateLimiting:Enabled"] = "false",
                [$"{KnowledgeIndexingOptions.SectionName}:Enabled"] = "false",
                [$"{KnowledgeEmbeddingOptions.SectionName}:Provider"] = "deterministic",
                [$"{ObservabilityOptions.SectionName}:Redis:ConnectionString"] = "",
                [$"{FinanceSeedWorkerOptions.SectionName}:Enabled"] = "false",
                [$"{KnowledgeEmbeddingOptions.SectionName}:Dimensions"] = "256",
                [$"{GroundedContextRetrievalCacheOptions.SectionName}:Enabled"] = "true",
                [$"{GroundedContextRetrievalCacheOptions.SectionName}:KeyVersion"] = "tests-v1",
                [$"{GroundedContextRetrievalCacheOptions.SectionName}:KnowledgeTtlSeconds"] = "300",
                [$"{GroundedContextRetrievalCacheOptions.SectionName}:MemoryTtlSeconds"] = "300",
                [$"{ObservabilityOptions.SectionName}:ObjectStorage:Enabled"] = "false"
            };

            foreach (var pair in _configurationOverrides)
            {
                settings[pair.Key] = pair.Value;
            }

            configurationBuilder.AddInMemoryCollection(settings);
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
            services.RemoveAll<IInternalCompanyToolContract>();
            services.AddSingleton<TestCompanyToolExecutor>();
            services.AddSingleton<IInternalCompanyToolContract>(provider => provider.GetRequiredService<TestCompanyToolExecutor>());

            services.RemoveAll<ICompanyDocumentStorage>();
            services.AddSingleton<TestCompanyDocumentStorage>();
            services.AddSingleton<ICompanyDocumentStorage>(provider => provider.GetRequiredService<TestCompanyDocumentStorage>());
            services.RemoveAll<ICompanyDocumentVirusScanner>();
            services.AddSingleton<TestCompanyDocumentVirusScanner>();
            services.AddSingleton<ICompanyDocumentVirusScanner>(provider => provider.GetRequiredService<TestCompanyDocumentVirusScanner>());
            services.RemoveAll<IFinanceSeedTelemetry>();
            services.AddSingleton<TestFinanceSeedTelemetry>();
            services.AddSingleton<IFinanceSeedTelemetry>(provider => provider.GetRequiredService<TestFinanceSeedTelemetry>());


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

    public TestFinanceSeedTelemetry FinanceSeedTelemetry =>
        Services.GetRequiredService<TestFinanceSeedTelemetry>();

    public async Task SeedAsync(Func<VirtualCompanyDbContext, Task> seed)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        await dbContext.Database.EnsureCreatedAsync();
        await seed(dbContext);
        await dbContext.SaveChangesAsync();
    }

    public async Task<T> ExecuteDbContextAsync<T>(Func<VirtualCompanyDbContext, Task<T>> callback)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        await dbContext.Database.EnsureCreatedAsync();
        return await callback(dbContext);
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

    public sealed class TestCompanyToolExecutor : IInternalCompanyToolContract
    {
        private readonly ConcurrentQueue<InternalToolExecutionRequest> _requests = new();

        public IReadOnlyList<InternalToolExecutionRequest> Requests => _requests.ToArray();
        public int ExecutionCount => _requests.Count;

        public void Reset()
        {
            while (_requests.TryDequeue(out _))
            {
            }
        }

        public Task<InternalToolExecutionResponse> ExecuteAsync(InternalToolExecutionRequest request, CancellationToken cancellationToken)
        {
            _requests.Enqueue(request);
            if (TryCreateFinanceToolData(request.ToolName, out var financeData))
            {
                return Task.FromResult(InternalToolExecutionResponse.Succeeded(
                    "Test finance tool execution completed.",
                    financeData,
                    BuildMetadata(request)));
            }

            var payload = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["toolName"] = JsonValue.Create(request.ToolName),
                ["actionType"] = JsonValue.Create(request.ActionType),
                ["scope"] = string.IsNullOrWhiteSpace(request.Scope) ? null : JsonValue.Create(request.Scope),
                ["companyId"] = JsonValue.Create(request.CompanyId),
                ["agentId"] = JsonValue.Create(request.AgentId),
                ["executionId"] = JsonValue.Create(request.ExecutionId),
                ["executed"] = JsonValue.Create(true)
            };

            return Task.FromResult(InternalToolExecutionResponse.Succeeded("Test tool execution completed.", payload, BuildMetadata(request)));
        }

        private static Dictionary<string, JsonNode?> BuildMetadata(InternalToolExecutionRequest request) =>
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["contractName"] = JsonValue.Create(nameof(TestCompanyToolExecutor)),
                ["companyId"] = JsonValue.Create(request.CompanyId),
                ["executionId"] = JsonValue.Create(request.ExecutionId),
                ["toolVersion"] = string.IsNullOrWhiteSpace(request.ToolVersion) ? null : JsonValue.Create(request.ToolVersion)
            };

        private static bool TryCreateFinanceToolData(string toolName, out Dictionary<string, JsonNode?> data)
        {
            data = toolName switch
            {
                "get_cash_balance" => new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["cashBalance"] = new JsonObject
                    {
                        ["amount"] = JsonValue.Create(1234.56m),
                        ["currency"] = JsonValue.Create("USD"),
                        ["accounts"] = new JsonArray()
                    }
                },
                "list_transactions" or "list_uncategorized_transactions" => new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["transactions"] = new JsonArray()
                },
                "list_invoices_awaiting_approval" => new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["invoices"] = new JsonArray()
                },
                "get_profit_and_loss_summary" => new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["profitAndLossSummary"] = new JsonObject
                    {
                        ["revenue"] = JsonValue.Create(1000m),
                        ["expenses"] = JsonValue.Create(750m),
                        ["netResult"] = JsonValue.Create(250m),
                        ["currency"] = JsonValue.Create("USD")
                    }
                },
                _ => []
            };

            return data.Count > 0;
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
