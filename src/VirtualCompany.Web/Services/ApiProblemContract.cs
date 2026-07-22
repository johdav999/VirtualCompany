using System.Text.Json;
using Microsoft.Extensions.Localization;
using VirtualCompany.Web.Localization.Shared;

namespace VirtualCompany.Web.Services;

public sealed class ApiProblemResponse
{
    public string? Title { get; set; }
    public string? Detail { get; set; }
    public string? Message { get; set; }
    public string? Code { get; set; }
    public string? TraceId { get; set; }
    public string? CorrelationId { get; set; }
    public Dictionary<string, JsonElement> Arguments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string[]>? Errors { get; set; }
}

public interface IApiProblemMessageResolver
{
    string Resolve(ApiProblemResponse? problem, string fallbackMessage);
}

public sealed class ApiProblemMessageResolver(IStringLocalizer<CommonResources> text) : IApiProblemMessageResolver
{
    public string Resolve(ApiProblemResponse? problem, string fallbackMessage)
    {
        if (problem is null) return fallbackMessage;
        var presentation = LocalizedProblemPresenter.Present(problem.Code, problem.Detail ?? problem.Message ?? problem.Title, problem.TraceId ?? problem.CorrelationId, text, problem.Arguments);
        return string.IsNullOrWhiteSpace(presentation.TraceId)
            ? presentation.Message
            : text["ProblemWithTrace", presentation.Message, presentation.TraceId];
    }
}
