using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Marketing;
using VirtualCompany.Application.Security;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class MarketingCreativeImageOptions
{
    public const string SectionName = "Marketing:CreativeImage";
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";
    public string ApiKeySecretReference { get; set; } = "platform/openai/api-key";
    public string Model { get; set; } = "gpt-image-2";
    public int TimeoutSeconds { get; set; } = 120;
    public int MaximumBytes { get; set; } = 25 * 1024 * 1024;
}

public sealed class OpenAiMarketingCreativeImageGenerator(
    IHttpClientFactory clients,
    IPlatformSecretStore secrets,
    IOptions<MarketingCreativeImageOptions> options) : IMarketingCreativeImageGenerator
{
    public const string ClientName = "marketing-creative-image";

    public async Task<MarketingCreativeImageResult> GenerateAsync(MarketingCreativeImageRequest request, CancellationToken ct)
    {
        var settings = options.Value;
        if (!settings.Enabled) throw new InvalidOperationException("Marketing creative image generation is disabled.");
        var secret = await secrets.GetAsync(settings.ApiKeySecretReference, null, ct);
        if (secret is null || string.IsNullOrWhiteSpace(secret.Value))
            throw new InvalidOperationException("The protected creative image provider credential is unavailable.");
        var size = request.Dimensions switch
        {
            "1024x1024" or "1024x1536" or "1536x1024" => request.Dimensions,
            _ => throw new ArgumentException("Supported image dimensions are 1024x1024, 1024x1536, or 1536x1024.")
        };
        var quality = request.Quality.Trim().ToLowerInvariant() switch
        { "low" => "low", "medium" => "medium", "high" => "high", _ => throw new ArgumentException("Image quality must be low, medium, or high.") };
        var format = request.OutputFormat.Trim().ToLowerInvariant() switch
        { "png" => "png", "jpeg" => "jpeg", "webp" => "webp", _ => throw new ArgumentException("Image output format must be png, jpeg, or webp.") };
        var client = clients.CreateClient(ClientName);
        client.BaseAddress = new Uri(settings.BaseUrl, UriKind.Absolute);
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 30, 300));
        using var message = new HttpRequestMessage(HttpMethod.Post, "images/generations");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret.Value);
        message.Content = JsonContent.Create(new { model = settings.Model, prompt = request.Prompt, size, quality,
            output_format = format, moderation = "auto", n = 1 });
        using var response = await client.SendAsync(message, ct);
        if (!response.IsSuccessStatusCode)
        {
            var code = (int)response.StatusCode == 429 ? "rate limited" : response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden ? "not authorized" : "unavailable";
            throw new InvalidOperationException($"The creative image provider is {code}. No asset was created.");
        }
        using var envelope = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var data = envelope.RootElement.GetProperty("data");
        if (data.GetArrayLength() != 1 || !data[0].TryGetProperty("b64_json", out var encoded) || string.IsNullOrWhiteSpace(encoded.GetString()))
            throw new InvalidOperationException("The creative image provider returned no usable image.");
        byte[] content;
        try { content = Convert.FromBase64String(encoded.GetString()!); }
        catch (FormatException exception) { throw new InvalidOperationException("The creative image provider returned invalid image data.", exception); }
        if (content.Length == 0 || content.Length > Math.Clamp(settings.MaximumBytes, 1_000_000, 25 * 1024 * 1024))
            throw new InvalidOperationException("The generated image exceeds the configured storage limit.");
        var contentType = format switch { "jpeg" => "image/jpeg", "webp" => "image/webp", _ => "image/png" };
        var requestId = response.Headers.TryGetValues("x-request-id", out var values) ? values.FirstOrDefault() : null;
        requestId ??= Guid.NewGuid().ToString("N");
        return new MarketingCreativeImageResult(content, contentType, settings.Model, requestId,
            $"Generated from approved Marketing brief context using {settings.Model}; {size}; {quality}; {format}.",
            "Provider moderation completed; human brand and evidence review required before approval or publication.");
    }
}
