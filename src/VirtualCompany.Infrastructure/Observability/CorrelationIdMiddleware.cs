using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auth;

namespace VirtualCompany.Infrastructure.Observability;

public sealed class CorrelationIdMiddleware : IMiddleware
{
    public const string HttpContextItemKey = "VirtualCompany.CorrelationId";
    private const int MaxCorrelationIdLength = 128;

    private readonly IOptions<ObservabilityOptions> _options;
    private readonly ICorrelationContextAccessor _correlationContextAccessor;
    private readonly ICompanyContextAccessor _companyContextAccessor;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(
        IOptions<ObservabilityOptions> options,
        ICorrelationContextAccessor correlationContextAccessor,
        ICompanyContextAccessor companyContextAccessor,
        ICurrentUserAccessor currentUserAccessor,
        ILogger<CorrelationIdMiddleware> logger)
    {
        _options = options;
        _correlationContextAccessor = correlationContextAccessor;
        _companyContextAccessor = companyContextAccessor;
        _currentUserAccessor = currentUserAccessor;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var headerName = GetHeaderName();
        var correlationId = ResolveCorrelationId(context, headerName);

        _correlationContextAccessor.CorrelationId = correlationId;
        context.Items[HttpContextItemKey] = correlationId;
        context.TraceIdentifier = correlationId;
        context.Response.Headers[headerName] = correlationId;

        using var scope = _logger.BeginScope(ExecutionLogScope.ForRequest(_correlationContextAccessor, _companyContextAccessor, _currentUserAccessor));

        await next(context);
    }

    public static string? GetCorrelationId(HttpContext context)
    {
        if (context.Items.TryGetValue(HttpContextItemKey, out var value) && value is string correlationId)
        {
            return correlationId;
        }

        return null;
    }

    private string GetHeaderName() =>
        string.IsNullOrWhiteSpace(_options.Value.CorrelationId.HeaderName)
            ? "X-Correlation-ID"
            : _options.Value.CorrelationId.HeaderName.Trim();

    private static string ResolveCorrelationId(HttpContext context, string headerName)
    {
        var incomingValue = context.Request.Headers[headerName].FirstOrDefault();
        var sanitizedValue = SanitizeIncomingCorrelationId(incomingValue);
        if (string.IsNullOrWhiteSpace(sanitizedValue))
        {
            return Guid.NewGuid().ToString("N");
        }

        return sanitizedValue;
    }

    private static string? SanitizeIncomingCorrelationId(string? incomingValue)
    {
        if (string.IsNullOrWhiteSpace(incomingValue))
        {
            return null;
        }

        var builder = new StringBuilder(Math.Min(incomingValue.Length, MaxCorrelationIdLength));

        foreach (var character in incomingValue.Trim())
        {
            if (char.IsLetterOrDigit(character) || character is '-' or '_' or '.')
            {
                builder.Append(character);
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }
}