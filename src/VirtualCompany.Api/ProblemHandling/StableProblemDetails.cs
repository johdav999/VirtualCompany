using Microsoft.AspNetCore.Mvc;

namespace VirtualCompany.Api.ProblemHandling;

public static class StableProblemDetails
{
    public static ProblemDetails Create(
        HttpContext httpContext,
        int status,
        string code,
        string title,
        string detail,
        IReadOnlyDictionary<string, object?>? arguments = null)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path
        };
        problem.Extensions["code"] = code;
        problem.Extensions["arguments"] = arguments ?? new Dictionary<string, object?>();
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;
        return problem;
    }

    public static ValidationProblemDetails CreateValidation(
        HttpContext httpContext,
        IReadOnlyDictionary<string, string[]> errors,
        string code,
        string title = "Validation failed")
    {
        var problem = new ValidationProblemDetails(new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = title,
            Detail = "One or more fields are invalid.",
            Instance = httpContext.Request.Path
        };
        problem.Extensions["code"] = code;
        problem.Extensions["arguments"] = new Dictionary<string, object?>();
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;
        return problem;
    }
}

public static class ApiProblemCodes
{
    public const string CompanyContextRequired = "identity.company_context_required";
    public const string FinanceApprovalConflict = "finance.approval.conflict";
    public const string FinanceApprovalValidation = "finance.approval.validation_failed";
    public const string AgentValidation = "agent.validation_failed";
    public const string DocumentProcessingFailed = "document.processing_failed";
    public const string FinanceRequestInvalid = "finance.request.invalid";
    public const string SalesRequestInvalid = "sales.request.invalid";
    public const string SupportRequestInvalid = "support.request.invalid";
    public const string IntegrationConfigurationRequired = "integration.configuration_required";
    public const string ResourceNotFound = "resource.not_found";
    public const string CommunicationLanguageInvalid = "communication.language_invalid";
}
