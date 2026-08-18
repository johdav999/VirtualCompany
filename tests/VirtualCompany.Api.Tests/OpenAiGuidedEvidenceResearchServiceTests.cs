using System.Text.Json;
using VirtualCompany.Infrastructure.Companies;

namespace VirtualCompany.Api.Tests;

public sealed class OpenAiGuidedEvidenceResearchServiceTests
{
    [Fact]
    public void Responses_result_preserves_bounded_summary_and_public_citations()
    {
        using var response = JsonDocument.Parse("""
        {
          "output": [
            {
              "type": "web_search_call",
              "action": {
                "sources": [
                  { "type": "url", "title": "SME pricing study", "url": "https://example.com/study" }
                ]
              }
            },
            {
              "type": "message",
              "content": [
                {
                  "type": "output_text",
                  "text": "SME price sensitivity varies by switching cost and demonstrated ROI.",
                  "annotations": [
                    { "type": "url_citation", "title": "SME pricing study", "url": "https://example.com/study" },
                    { "type": "url_citation", "title": "Unsafe", "url": "file:///secret" }
                  ]
                }
              ]
            }
          ]
        }
        """);

        var result = OpenAiGuidedEvidenceResearchService.Parse(response.RootElement);

        Assert.True(result.Available);
        Assert.Contains("price sensitivity", result.Summary, StringComparison.OrdinalIgnoreCase);
        var source = Assert.Single(result.Sources);
        Assert.Equal("SME pricing study", source.Title);
        Assert.Equal("https://example.com/study", source.Url);
    }

    [Fact]
    public void Empty_provider_result_fails_safely_without_fabricating_evidence()
    {
        using var response = JsonDocument.Parse("{\"output\":[]}");

        var result = OpenAiGuidedEvidenceResearchService.Parse(response.RootElement);

        Assert.False(result.Available);
        Assert.Empty(result.Sources);
        Assert.Equal("research_empty_response", result.FailureCode);
    }

    [Fact]
    public void Provider_error_logging_extracts_only_bounded_identifiers()
    {
        var error = OpenAiGuidedEvidenceResearchService.ParseProviderError(
            "{\"error\":{\"type\":\"invalid_request_error<script>\",\"code\":\"unsupported_tool\"}}");

        Assert.Equal("invalid_request_errorscript", error.Type);
        Assert.Equal("unsupported_tool", error.Code);
    }
}
