using Microsoft.Extensions.Options;
using VirtualCompany.Application.Agents;
using VirtualCompany.Infrastructure.Companies;
namespace VirtualCompany.Api.Tests;

public sealed class ApprovedSpeechGatewayTests
{
    [Theory]
    [InlineData("Welcome, everyone!", "welcome everyone", true)]
    [InlineData("Välkommen till vår presentation.", "Välkommen till vår presentation", true)]
    [InlineData("now here", "nowhere", false)]
    [InlineData("1.5 percent", "15 percent", false)]
    [InlineData("5%", "5", false)]
    public void Script_comparison_retains_word_and_numeric_boundaries(string script, string transcript, bool equal) =>
        Assert.Equal(equal, ApprovedSpeechGateway.Normalize(script) == ApprovedSpeechGateway.Normalize(transcript));
}

