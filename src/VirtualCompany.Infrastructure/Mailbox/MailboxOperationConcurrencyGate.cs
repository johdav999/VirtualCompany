using System.Collections.Concurrent;

namespace VirtualCompany.Infrastructure.Mailbox;

public interface IMailboxOperationConcurrencyGate
{
    ValueTask<IAsyncDisposable?> TryAcquireAsync(
        Guid companyId,
        Guid connectionId,
        string destinationHost,
        CancellationToken cancellationToken);
}

public sealed class MailboxOperationConcurrencyGate : IMailboxOperationConcurrencyGate, IDisposable
{
    private const int MaximumTrackedKeys = 10_000;
    private readonly SemaphoreSlim _global = new(64, 64);
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _companies = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _connections = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _destinations = new(StringComparer.OrdinalIgnoreCase);

    public async ValueTask<IAsyncDisposable?> TryAcquireAsync(
        Guid companyId,
        Guid connectionId,
        string destinationHost,
        CancellationToken cancellationToken)
    {
        var company = GetBounded(_companies, companyId, Guid.Empty, 8);
        var connection = GetBounded(_connections, connectionId, Guid.Empty, 2);
        var destination = GetBounded(
            _destinations,
            destinationHost.Trim().ToLowerInvariant(),
            "__shared_overflow__",
            16);
        var acquired = new List<SemaphoreSlim>(4);
        foreach (var semaphore in new[] { _global, company, connection, destination })
        {
            if (!await semaphore.WaitAsync(0, cancellationToken))
            {
                Release(acquired);
                return null;
            }

            acquired.Add(semaphore);
        }

        return new Lease(acquired);
    }

    public void Dispose()
    {
        _global.Dispose();
        foreach (var semaphore in _companies.Values.Concat(_connections.Values).Concat(_destinations.Values).Distinct())
        {
            semaphore.Dispose();
        }
    }

    private static SemaphoreSlim GetBounded<TKey>(
        ConcurrentDictionary<TKey, SemaphoreSlim> entries,
        TKey key,
        TKey overflowKey,
        int limit)
        where TKey : notnull
    {
        var effectiveKey = entries.Count < MaximumTrackedKeys || entries.ContainsKey(key) ? key : overflowKey;
        return entries.GetOrAdd(effectiveKey, _ => new SemaphoreSlim(limit, limit));
    }

    private static void Release(IReadOnlyList<SemaphoreSlim> acquired)
    {
        for (var index = acquired.Count - 1; index >= 0; index--)
        {
            acquired[index].Release();
        }
    }

    private sealed class Lease(IReadOnlyList<SemaphoreSlim> acquired) : IAsyncDisposable
    {
        private int _released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                Release(acquired);
            }

            return ValueTask.CompletedTask;
        }
    }
}
