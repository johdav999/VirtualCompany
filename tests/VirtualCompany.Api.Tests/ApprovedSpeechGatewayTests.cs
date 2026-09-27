using Microsoft.Extensions.Options;
using VirtualCompany.Application.Agents;
using VirtualCompany.Infrastructure.Companies;
namespace VirtualCompany.Api.Tests;

public sealed class ApprovedSpeechGatewayTests
{
    [Theory]
    [InlineData("incomplete", 2, "Approved answer", "speech_incomplete")]
    [InlineData("cancelled", 2, "Approved answer", "speech_incomplete")]
    [InlineData("completed", 0, "Approved answer", "speech_empty")]
    [InlineData("completed", 2, "Different answer", "speech_content_mismatch")]
    [InlineData("completed", 2, "Approved answer", null)]
    public void Completion_is_required_even_when_the_transcript_matches(string status, long bytes, string transcript, string? expected) =>
        Assert.Equal(expected, ApprovedSpeechGateway.FailureCode(status, bytes, transcript, "Approved answer"));

    [Theory]
    [InlineData("Welcome, everyone!", "welcome everyone", true)]
    [InlineData("Välkommen till vår presentation.", "Välkommen till vår presentation", true)]
    [InlineData("now here", "nowhere", false)]
    [InlineData("1.5 percent", "15 percent", false)]
    [InlineData("5%", "5", false)]
    [InlineData("AI-assisted high-risk third-party", "AI assisted high risk third party", true)]
    [InlineData("-5", "5", false)]
    [InlineData("5-10", "5 10", false)]
    [InlineData("not approved", "approved", false)]
    public void Script_comparison_retains_word_and_numeric_boundaries(string script, string transcript, bool equal) =>
        Assert.Equal(equal, ApprovedSpeechGateway.Normalize(script) == ApprovedSpeechGateway.Normalize(transcript));
}

