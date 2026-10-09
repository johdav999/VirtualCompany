using System.Net;
using System.Text.Json;

namespace VirtualCompany.Api.Tests;

public sealed class HttpJsonTestExtensionsTests
{
    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    public async Task Composed_test_clients_serialize_json_nodes_with_system_text_json(string method)
    {
        using var handler = new CaptureHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        var value = new { metadata = new JsonObject { ["category"] = "retained evidence" } };
        using var response = method == "POST"
            ? await client.PostAsJsonAsync("command", value)
            : await client.PutAsJsonAsync("command", value);

        Assert.Equal(method, handler.Method);
        Assert.Equal("application/json", handler.MediaType);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("retained evidence", body.RootElement.GetProperty("metadata").GetProperty("category").GetString());
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? Method { get; private set; }
        public string? MediaType { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Method = request.Method.Method;
            MediaType = request.Content!.Headers.ContentType!.MediaType;
            Body = await request.Content.ReadAsStringAsync(token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
