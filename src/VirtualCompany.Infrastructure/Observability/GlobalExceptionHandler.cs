using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using VirtualCompany.Application.Companies;

namespace VirtualCompany.Infrastructure.Observability;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IOptions<ObservabilityOptions> _options;
    private const string ProblemDetailsContentType = "application/problem+json";
    private const string UnexpectedErrorDetail = "The server encountered an unexpected error. Use the provided identifiers when contacting support.";

    public GlobalExceptionHandler(
        ILogger<GlobalExceptionHandler> logger,
        IOptions<ObservabilityOptions> options)
    {
        _logger = logger;
        _options = options;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (httpContext.Response.HasStarted)
        {
            return false;
        }

        var correlationId = CorrelationIdMiddleware.GetCorrelationId(httpContext) ?? httpContext.TraceIdentifier;
        var traceId = httpContext.TraceIdentifier;
        var mappedException = MapException(exception);

        using var scope = _logger.BeginScope(ExecutionLogScope.ForHttpContext(httpContext));

        if (mappedException.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(
                exception,
                "Unhandled exception {ExceptionType} for HTTP {Method} {Path}. TraceId: {TraceId}.",
                exception.GetType().FullName ?? exception.GetType().Name,
                httpContext.Request.Method,
                httpContext.Request.Path,
                traceId);
        }
        else
        {
            _logger.LogWarning(
                exception,
                "Mapped exception {ExceptionType} for HTTP {Method} {Path} to status code {StatusCode}. TraceId: {TraceId}.",
                exception.GetType().FullName ?? exception.GetType().Name,
                httpContext.Request.Method,
                httpContext.Request.Path,
                mappedException.StatusCode,
                traceId);
        }

        var problemDetails = CreateProblemDetails(httpContext, mappedException, correlationId, traceId);
        problemDetails.Extensions["correlationId"] = correlationId;
        problemDetails.Extensions["traceId"] = traceId;

        httpContext.Response.StatusCode = mappedException.StatusCode;
        httpContext.Response.Headers[_options.Value.CorrelationId.HeaderName] = correlationId;
        await httpContext.Response.WriteAsJsonAsync(
            problemDetails,
            options: null,
            contentType: ProblemDetailsContentType,
            cancellationToken: cancellationToken);

        return true;
    }

    private static ProblemDetails CreateProblemDetails(
        HttpContext httpContext,
        ExceptionHandlingResult mappedException,
        string correlationId,
        string traceId)
    {
        ProblemDetails problemDetails = mappedException.Errors is null
            ? new ProblemDetails()
            : new ValidationProblemDetails(mappedException.Errors);

        problemDetails.Status = mappedException.StatusCode;
        problemDetails.Title = mappedException.Title;
        problemDetails.Detail = mappedException.Detail;
        problemDetails.Type = $"https://httpstatuses.com/{mappedException.StatusCode}";
        problemDetails.Instance = httpContext.Request.Path.HasValue ? httpContext.Request.Path.Value : null;
        problemDetails.Extensions["correlationId"] = correlationId;
        problemDetails.Extensions["traceId"] = traceId;

        return problemDetails;
    }

    private static ExceptionHandlingResult MapException(Exception exception) =>
        exception switch
        {
            CompanyOnboardingValidationException validationException => ValidationFailure(validationException.Errors),
            CompanyMembershipAdministrationValidationException validationException => ValidationFailure(validationException.Errors),
            UnauthorizedAccessException => new ExceptionHandlingResult(
                StatusCodes.Status403Forbidden,
                "Forbidden",
                "The request could not be authorized for the current company context."),
            KeyNotFoundException => new ExceptionHandlingResult(
                StatusCodes.Status404NotFound,
                "Not Found",
                "The requested resource was not found."),
            ArgumentException => new ExceptionHandlingResult(
                StatusCodes.Status400BadRequest,
                "Invalid request",
                "The request payload or parameters were invalid."),
            JsonException => new ExceptionHandlingResult(
                StatusCodes.Status400BadRequest,
                "Invalid request",
                "The request payload or parameters were invalid."),
            _ => new ExceptionHandlingResult(
                StatusCodes.Status500InternalServerError,
                "An unexpected error occurred.",
                UnexpectedErrorDetail)
        };

    private static ExceptionHandlingResult ValidationFailure(IReadOnlyDictionary<string, string[]> errors) =>
        new(
            StatusCodes.Status400BadRequest,
            "Validation failed",
            "One or more validation errors occurred.",
            new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase));

    private sealed record ExceptionHandlingResult(int StatusCode, string Title, string Detail, IDictionary<string, string[]>? Errors = null);
}