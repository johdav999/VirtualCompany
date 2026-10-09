using System.Net.Http.Json;

namespace VirtualCompany.Api.Tests;

// Keep the composed API fixtures on the same JSON implementation as the API.
// The media SDK also brings legacy Web API HTTP extensions into the reference graph;
// their shorter overloads otherwise win and cannot serialize System.Text.Json.Nodes.
public static class HttpJsonTestExtensions
{
    public static Task<HttpResponseMessage> PostAsJsonAsync<T>(this HttpClient client, string requestUri, T value) =>
        HttpClientJsonExtensions.PostAsJsonAsync(client, requestUri, value);

    public static Task<HttpResponseMessage> PutAsJsonAsync<T>(this HttpClient client, string requestUri, T value) =>
        HttpClientJsonExtensions.PutAsJsonAsync(client, requestUri, value);
}
