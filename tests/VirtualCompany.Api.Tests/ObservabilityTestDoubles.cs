using System.Security.Claims;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auth;

namespace VirtualCompany.Api.Tests;

internal sealed class TestCurrentUserAccessor : ICurrentUserAccessor
{
    public ClaimsPrincipal Principal { get; init; } = new(new ClaimsIdentity());
    public bool IsAuthenticated => Principal.Identity?.IsAuthenticated == true;
    public Guid? UserId { get; init; }
    public AuthenticatedUserIdentity Current => new(IsAuthenticated, UserId, null);
}

internal sealed class ScopeCapturingLogger<T> : ILogger<T>
{
    private readonly List<object> _activeScopes = [];

    public IList<CapturedLogEntry> Entries { get; } = [];

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
    {
        _activeScopes.Add(state);
        return new ScopeHandle(_activeScopes);
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Entries.Add(new CapturedLogEntry(logLevel, formatter(state, exception), FlattenScopes(), exception));
    }

    private IReadOnlyDictionary<string, object?> FlattenScopes()
    {
        var flattened = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var scope in _activeScopes)
        {
            if (scope is not IEnumerable<KeyValuePair<string, object?>> scopeValues)
            {
                continue;
            }

            foreach (var scopeValue in scopeValues)
            {
                flattened[scopeValue.Key] = scopeValue.Value;
            }
        }

        return flattened;
    }

    internal sealed record CapturedLogEntry(
        LogLevel LogLevel,
        string Message,
        IReadOnlyDictionary<string, object?> Scope,
        Exception? Exception);

    private sealed class ScopeHandle : IDisposable
    {
        private readonly List<object> _activeScopes;
        private bool _disposed;

        public ScopeHandle(List<object> activeScopes)
        {
            _activeScopes = activeScopes;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _activeScopes.RemoveAt(_activeScopes.Count - 1);
        }
    }
}