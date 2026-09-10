using System.Net.Http.Json;

namespace VirtualCompany.Web.Services;

public sealed record NarrationSpeechProfile(bool Available, string Model, string Voice, string ConfigurationVersion);
public sealed record NarrationScript(int SlideNumber, int TalkingPoint, string Text);
public sealed record NarrationSegment(Guid Id, int SlideNumber, int TalkingPoint, string SourceText,
    string Script, string Status, bool Reused, int DurationMilliseconds, long Bytes, int Attempts, string? FailureCode);
public sealed record NarrationRevision(Guid Id, Guid DeckId, int DeckVersion, string Language, string Voice,
    string Model, Guid AudienceId, string Status, long Version, DateTime CreatedUtc, DateTime? ApprovedUtc,
    IReadOnlyList<NarrationSegment> Segments, int InputTokens, int OutputTokens, int UnresolvedAttempts,
    double GeneratedMinutes, double ReusedMinutes, decimal? EstimatedCostUsd);
public sealed record NarrationWorkspace(NarrationSpeechProfile Speech, IReadOnlyList<NarrationRevision> Revisions);

public sealed class SalesNarrationApiClient(ICompanyApiTransport transport)
{
    public async Task<NarrationWorkspace> GetAsync(Guid company, Guid session, CancellationToken ct = default)
    {
        using var response = await transport.SendAsync(company, HttpMethod.Get, $"api/sales/narration/sessions/{session}", null, ct);
        await Check(response, ct);
        return (await response.Content.ReadFromJsonAsync<NarrationWorkspace>(ct))!;
    }
    public async Task PrepareAsync(Guid company, Guid session, string language, NarrationScript[]? scripts = null)
    {
        using var content = JsonContent.Create(new { language, scripts });
        using var response = await transport.SendAsync(company, HttpMethod.Post, $"api/sales/narration/sessions/{session}/prepare", content, default);
        await Check(response, default);
    }
    public async Task DecideAsync(Guid company, NarrationRevision revision, string action, bool acknowledge)
    {
        using var content = JsonContent.Create(new { expectedVersion = revision.Version, acknowledgeAdditionalCost = acknowledge });
        using var response = await transport.SendAsync(company, HttpMethod.Post, $"api/sales/narration/revisions/{revision.Id}/{action}", content, default);
        await Check(response, default);
    }
    private static async Task Check(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            throw new InvalidOperationException("Only the meeting organizer can prepare and preview narration.");
        var problem = await response.Content.ReadFromJsonAsync<NarrationProblem>(ct);
        throw new InvalidOperationException(problem?.Detail ?? "Narration is unavailable. Reload and try again.");
    }
    private sealed record NarrationProblem(string? Detail);

    public static string PreviewUrl(Guid company, Guid session, NarrationRevision revision, NarrationSegment segment) =>
        $"/sales/narration-preview/{company}/{session}/{revision.Id}/{segment.Id}?audienceId={revision.AudienceId}";
}

public static class SalesNarrationPreviewEndpoint
{
    public static void MapSalesNarrationPreview(this WebApplication app)
    {
        app.MapGet("/sales/narration-preview/{company:guid}/{session:guid}/{revision:guid}/{segment:guid}",
            async (Guid company, Guid session, Guid revision, Guid segment, Guid audienceId,
                ICompanyApiTransport transport, HttpContext context, CancellationToken ct) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                using var response = await transport.SendAsync(company, HttpMethod.Get,
                    $"api/sales/narration/sessions/{session}/revisions/{revision}/segments/{segment}/preview?audienceId={audienceId}", null, ct);
                if (!response.IsSuccessStatusCode) return Results.StatusCode((int)response.StatusCode);
                return Results.Bytes(await response.Content.ReadAsByteArrayAsync(ct), "audio/wav");
            });
    }
}

