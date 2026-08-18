namespace VirtualCompany.Web.Services;

public static class GuidedRealtimeProxyEndpoints
{
    private const int MaxOfferLength = 100_000;

    public static IEndpointRouteBuilder MapGuidedRealtimeProxyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
            "/api/companies/{companyId:guid}/guided-work-sessions/{sessionId:guid}/voice/calls",
            ForwardStartAsync);
        endpoints.MapDelete(
            "/api/companies/{companyId:guid}/guided-work-sessions/{sessionId:guid}/voice/calls/{bindingId:guid}",
            ForwardEndAsync);
        return endpoints;
    }

    private static async Task ForwardStartAsync(
        Guid companyId,
        Guid sessionId,
        HttpContext context,
        GuidedWorkApiClient client,
        CancellationToken ct)
    {
        if (context.Request.ContentLength is > MaxOfferLength)
        {
            await WritePlainErrorAsync(context, StatusCodes.Status413PayloadTooLarge, "The voice offer is too large.", ct);
            return;
        }

        using var reader = new StreamReader(context.Request.Body);
        var offer = await reader.ReadToEndAsync(ct);
        if (offer.Length > MaxOfferLength)
        {
            await WritePlainErrorAsync(context, StatusCodes.Status413PayloadTooLarge, "The voice offer is too large.", ct);
            return;
        }

        try
        {
            var upstream = await client.StartVoiceCallAsync(companyId, sessionId, offer, ct);
            await WriteUpstreamAsync(context, upstream, ct);
        }
        catch (OnboardingApiException ex)
        {
            await WritePlainErrorAsync(context, StatusCodes.Status503ServiceUnavailable, ex.Message, ct);
        }
        catch (HttpRequestException)
        {
            await WritePlainErrorAsync(context, StatusCodes.Status502BadGateway, "The workshop API could not be reached. You can continue by typing.", ct);
        }
    }

    private static async Task ForwardEndAsync(
        Guid companyId,
        Guid sessionId,
        Guid bindingId,
        HttpContext context,
        GuidedWorkApiClient client,
        CancellationToken ct)
    {
        try
        {
            var upstream = await client.EndVoiceCallAsync(companyId, sessionId, bindingId, ct);
            await WriteUpstreamAsync(context, upstream, ct);
        }
        catch (OnboardingApiException ex)
        {
            await WritePlainErrorAsync(context, StatusCodes.Status503ServiceUnavailable, ex.Message, ct);
        }
        catch (HttpRequestException)
        {
            await WritePlainErrorAsync(context, StatusCodes.Status502BadGateway, "The workshop API could not be reached.", ct);
        }
    }

    private static async Task WriteUpstreamAsync(HttpContext context, GuidedVoiceTransportResponse upstream, CancellationToken ct)
    {
        context.Response.StatusCode = upstream.StatusCode;
        context.Response.ContentType = string.IsNullOrWhiteSpace(upstream.ContentType) ? "text/plain; charset=utf-8" : upstream.ContentType;
        CopyHeader(context, "X-Guided-Voice-Binding", upstream.BindingId);
        CopyHeader(context, "X-Guided-Voice-Expires", upstream.ExpiresAt);
        CopyHeader(context, "Retry-After", upstream.RetryAfter);
        if (upstream.StatusCode != StatusCodes.Status204NoContent && !string.IsNullOrEmpty(upstream.Body))
            await context.Response.WriteAsync(upstream.Body, ct);
    }

    private static Task WritePlainErrorAsync(HttpContext context, int statusCode, string message, CancellationToken ct)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "text/plain; charset=utf-8";
        return context.Response.WriteAsync(message, ct);
    }

    private static void CopyHeader(HttpContext context, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) context.Response.Headers[name] = value;
    }
}
