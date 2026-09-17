using System.Net;
using System.Net.Http.Json;

namespace VirtualCompany.Api.Tests;

public sealed class SalesNarrationRoutingTests
{
    [Theory]
    [InlineData("approve")]
    [InlineData("retry")]
    [InlineData("revoke")]
    public async Task Decision_routes_resolve_and_enforce_authentication(string decision)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Company-Id", Guid.NewGuid().ToString());
        using var response = await client.PostAsJsonAsync(
            $"/api/sales/narration/revisions/{Guid.NewGuid()}/{decision}",
            new { expectedVersion = 1, acknowledgeAdditionalCost = false });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}