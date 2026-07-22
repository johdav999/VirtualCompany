using Microsoft.Extensions.Localization;
using VirtualCompany.Web.Localization.Shared;
using System.Text.Json;

namespace VirtualCompany.Web.Services;

public sealed record ApiProblemPresentation(string Message, string? TraceId, bool IsKnownCode);

public static class LocalizedProblemPresenter
{
    private static readonly IReadOnlyDictionary<string, string> Keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["identity.company_context_required"] = "ProblemCompanyContextRequired",
        ["finance.approval.conflict"] = "ProblemFinanceApprovalConflict",
        ["finance.approval.validation_failed"] = "ProblemValidationFailed"
        , ["agent.validation_failed"] = "ProblemValidationFailed"
        , ["document.processing_failed"] = "ProblemDocumentProcessingFailed"
        , ["finance.request.invalid"] = "ProblemFinanceRequestInvalid"
        , ["sales.request.invalid"] = "ProblemSalesRequestInvalid"
        , ["support.request.invalid"] = "ProblemSupportRequestInvalid"
        , ["integration.configuration_required"] = "ProblemIntegrationConfigurationRequired"
        , ["resource.not_found"] = "ProblemResourceNotFound"
        , ["communication.language_invalid"] = "ProblemCommunicationLanguageInvalid"
    };

    public static ApiProblemPresentation Present(
        string? code,
        string? fallbackDetail,
        string? traceId,
        IStringLocalizer<CommonResources> text,
        IReadOnlyDictionary<string, JsonElement>? arguments = null)
    {
        if (!string.IsNullOrWhiteSpace(code) && Keys.TryGetValue(code, out var key))
        {
            var values = arguments?.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => SafeValue(x.Value)).ToArray() ?? [];
            return new(values.Length == 0 ? text[key] : text[key, values], traceId, true);
        }

        var message = string.IsNullOrWhiteSpace(fallbackDetail) ? text["ProblemUnknown"] : fallbackDetail.Trim();
        return new(message, traceId, false);
    }

    private static object SafeValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => string.Empty
    };
}
