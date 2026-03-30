using System.Collections;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Auth;

namespace VirtualCompany.Infrastructure.Observability;

internal sealed class ExecutionLogScope : IEnumerable<KeyValuePair<string, object?>>
{
    private readonly Func<string?> _getCorrelationId;
    private readonly Func<string?> _getTraceId;
    private readonly Func<Guid?> _getCompanyId;
    private readonly Func<Guid?> _getUserId;
    private readonly Func<ResolvedCompanyMembershipContext?> _getMembership;
    private readonly Func<string?> _getRequestMethod;
    private readonly Func<string?> _getRequestPath;

    private ExecutionLogScope(
        Func<string?> getCorrelationId,
        Func<string?> getTraceId,
        Func<Guid?> getCompanyId,
        Func<Guid?> getUserId,
        Func<ResolvedCompanyMembershipContext?> getMembership,
        Func<string?> getRequestMethod,
        Func<string?> getRequestPath)
    {
        _getCorrelationId = getCorrelationId;
        _getTraceId = getTraceId;
        _getCompanyId = getCompanyId;
        _getUserId = getUserId;
        _getMembership = getMembership;
        _getRequestMethod = getRequestMethod;
        _getRequestPath = getRequestPath;
    }

    public static ExecutionLogScope ForRequest(
        ICorrelationContextAccessor correlationContextAccessor,
        ICompanyContextAccessor companyContextAccessor,
        ICurrentUserAccessor currentUserAccessor) =>
        new(
            () => correlationContextAccessor.CorrelationId,
            () => null,
            () => companyContextAccessor.CompanyId,
            () => companyContextAccessor.UserId ?? currentUserAccessor.UserId,
            () => companyContextAccessor.Membership,
            () => null,
            () => null);

    public static ExecutionLogScope ForHttpContext(HttpContext context)
    {
        var companyContextAccessor = context.RequestServices.GetService<ICompanyContextAccessor>();
        var currentUserAccessor = context.RequestServices.GetService<ICurrentUserAccessor>();

        return new(
            () => CorrelationIdMiddleware.GetCorrelationId(context) ?? context.TraceIdentifier,
            () => context.TraceIdentifier,
            () => companyContextAccessor?.CompanyId,
            () => companyContextAccessor?.UserId ?? currentUserAccessor?.UserId,
            () => companyContextAccessor?.Membership,
            () => context.Request.Method,
            () => context.Request.Path.HasValue ? context.Request.Path.Value : null);
    }

    public static IReadOnlyDictionary<string, object?> ForBackground(string correlationId, Guid? companyId = null)
    {
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["CorrelationId"] = correlationId
        };

        if (companyId.HasValue)
        {
            properties["CompanyId"] = companyId.Value;
        }

        return properties;
    }

    public static IReadOnlyDictionary<string, object?> ForBackgroundJob(string jobName, int attempt, int maxAttempts, string correlationId, Guid? companyId = null)
    {
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["JobName"] = jobName,
            ["Attempt"] = attempt,
            ["MaxAttempts"] = maxAttempts,
            ["CorrelationId"] = correlationId
        };

        if (companyId.HasValue)
        {
            properties["CompanyId"] = companyId.Value;
        }

        return properties;
    }

    public static IReadOnlyDictionary<string, object?> ForOutboxMessage(Guid outboxMessageId, Guid companyId, string? correlationId)
    {
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["OutboxMessageId"] = outboxMessageId,
            ["CompanyId"] = companyId
        };

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            properties["CorrelationId"] = correlationId;
        }

        return properties;
    }

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
    {
        var correlationId = _getCorrelationId();
        if (!string.IsNullOrWhiteSpace(correlationId)) yield return new KeyValuePair<string, object?>("CorrelationId", correlationId);
        
        var traceId = _getTraceId();
        if (!string.IsNullOrWhiteSpace(traceId)) yield return new KeyValuePair<string, object?>("TraceId", traceId);

        var companyId = _getCompanyId();
        if (companyId.HasValue) yield return new KeyValuePair<string, object?>("CompanyId", companyId.Value);

        var userId = _getUserId();
        if (userId.HasValue) yield return new KeyValuePair<string, object?>("UserId", userId.Value);

        var requestMethod = _getRequestMethod();
        if (!string.IsNullOrWhiteSpace(requestMethod)) yield return new KeyValuePair<string, object?>("RequestMethod", requestMethod);

        var requestPath = _getRequestPath();
        if (!string.IsNullOrWhiteSpace(requestPath)) yield return new KeyValuePair<string, object?>("RequestPath", requestPath);

        var membership = _getMembership();
        if (membership is not null) yield return new KeyValuePair<string, object?>("CompanyMembershipId", membership.MembershipId);
        if (membership is not null) yield return new KeyValuePair<string, object?>("CompanyMembershipRole", membership.MembershipRole.ToString());
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}