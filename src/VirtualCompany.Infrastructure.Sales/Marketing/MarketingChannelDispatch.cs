using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Marketing;
using VirtualCompany.Application.Security;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class MarketingChannelDeliveryOptions
{
    public const string SectionName = "Marketing:ChannelDelivery";
    public bool Enabled { get; set; }
    public int PollSeconds { get; set; } = 30;
    public int BatchSize { get; set; } = 20;
    public int MaximumAttempts { get; set; } = 5;
    public string LinkedInVersion { get; set; } = "202604";
    public string MetaGraphVersion { get; set; } = "v24.0";
}

public sealed class MarketingChannelDispatchService(
    VirtualCompanyDbContext db,
    IApprovalRequestService approvals,
    IMarketingPolicyService policies,
    IAuditEventWriter audit,
    IEnumerable<IMarketingChannelAdapter> validators,
    IEnumerable<IMarketingChannelPublisher> publishers,
    IOptions<MarketingChannelDeliveryOptions> options,
    ILogger<MarketingChannelDispatchService> logger) : IMarketingChannelDispatchService
{
    public async Task<int> DispatchDueAsync(DateTime nowUtc, int batchSize, CancellationToken ct)
    {
        var settings = options.Value;
        var candidates = await db.MarketingChannelActions.IgnoreQueryFilters()
            .Where(x => (x.Status == "queued" || x.Status == "retry_scheduled") &&
                        (!x.ScheduledUtc.HasValue || x.ScheduledUtc <= nowUtc) && x.AttemptCount < settings.MaximumAttempts)
            .OrderBy(x => x.ScheduledUtc).ThenBy(x => x.CreatedUtc).Take(Math.Clamp(batchSize, 1, 100)).ToListAsync(ct);
        var processed = 0;
        foreach (var action in candidates)
        {
            var connection = await db.MarketingChannelConnections.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                x.CompanyId == action.CompanyId && x.Id == action.MarketingChannelConnectionId, ct);
            if (connection is null || connection.Status != "connected" || connection.HealthStatus == "reauthorization_required")
            {
                action.ClaimForDispatch();
                action.RecordFailure("connection_unavailable", false);
                await db.SaveChangesAsync(ct);
                processed++;
                continue;
            }
            if (!action.ApprovalRequestId.HasValue)
            {
                action.ClaimForDispatch(); action.RecordFailure("approval_missing", false);
                await db.SaveChangesAsync(ct); processed++; continue;
            }
            var approval = await approvals.GetAsync(action.CompanyId, action.ApprovalRequestId.Value, ct);
            if (!approval.Status.Equals("approved", StringComparison.OrdinalIgnoreCase) ||
                approval.TargetEntityType != "marketing_channel_action" || approval.TargetEntityId != action.Id)
            {
                action.ClaimForDispatch(); action.RecordFailure("approval_not_current", false);
                await db.SaveChangesAsync(ct); processed++; continue;
            }
            var hasCurrentEvidence = !action.MarketingContentBriefId.HasValue || await db.MarketingContentBriefs.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.CompanyId == action.CompanyId && x.Id == action.MarketingContentBriefId &&
                    x.Status == MarketingStatuses.Approved && x.Version == action.ContentBriefVersion, ct);
            var policy = policies.Evaluate(new MarketingPolicyRequest(MarketingPolicyActions.ContentPublication,
                "marketing_channel_action", action.Id, action.Version, hasCurrentEvidence, ApprovalCompleted: true));
            if (!policy.Allowed)
            {
                action.ClaimForDispatch(); action.RecordFailure("policy_or_target_version_changed", false);
                await db.SaveChangesAsync(ct); processed++; continue;
            }
            var validator = validators.SingleOrDefault(x => x.Provider.Equals(connection.Provider, StringComparison.OrdinalIgnoreCase));
            var publisher = publishers.SingleOrDefault(x => x.Provider.Equals(connection.Provider, StringComparison.OrdinalIgnoreCase));
            if (validator is null || publisher is null)
            {
                action.ClaimForDispatch(); action.RecordFailure("provider_unavailable", false);
                await db.SaveChangesAsync(ct); processed++; continue;
            }
            var validation = validator.Validate(action.ActionType, action.PayloadJson, connection.CapabilitiesJson);
            if (!validation.Allowed)
            {
                action.ClaimForDispatch(); action.RecordFailure(validation.ReasonCode, false);
                await db.SaveChangesAsync(ct); processed++; continue;
            }

            action.ClaimForDispatch();
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { db.Entry(action).State = EntityState.Detached; continue; }

            var destination = await db.MarketingChannelDestinations.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
                x.CompanyId == action.CompanyId && x.MarketingChannelConnectionId == connection.Id &&
                x.ProviderReference == action.DestinationReference && x.Status == "active", ct);
            if (await db.MarketingChannelDestinations.IgnoreQueryFilters().AnyAsync(x =>
                    x.CompanyId == action.CompanyId && x.MarketingChannelConnectionId == connection.Id, ct) && destination is null)
            {
                action.RecordFailure("destination_unavailable", false); await db.SaveChangesAsync(ct); processed++; continue;
            }
            var destinationValidation = validator.Validate(action.ActionType, action.PayloadJson,
                destination?.CapabilitiesJson ?? connection.CapabilitiesJson);
            if (!destinationValidation.Allowed)
            {
                action.RecordFailure(destinationValidation.ReasonCode, false); await db.SaveChangesAsync(ct); processed++; continue;
            }
            var outcome = await publisher.PublishAsync(action.DestinationReference, action.ActionType,
                action.PayloadJson, destination?.SecretReference ?? connection.SecretReference, ct);
            if (outcome.Outcome == "succeeded")
            {
                action.RecordDispatch(outcome.ProviderReference ?? "provider-reference-unavailable");
                action.Reconcile(true);
            }
            else if (outcome.Outcome == "ambiguous") action.RecordAmbiguous(outcome.ReasonCode);
            else action.RecordFailure(outcome.ReasonCode, outcome.Outcome == "retryable");
            if (outcome.RequiresReauthorization) connection.RecordHealth(false, outcome.SafeExplanation);
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync(new AuditEventWriteRequest(action.CompanyId, AuditActorTypes.System, null,
                "marketing.channel_action.dispatch_completed", "marketing_channel_action", action.Id.ToString("N"),
                outcome.Outcome == "succeeded" ? AuditEventOutcomes.Succeeded : outcome.Outcome == "ambiguous" ? AuditEventOutcomes.Pending : AuditEventOutcomes.Failed,
                outcome.SafeExplanation, DataSources: [connection.Provider], Metadata: new Dictionary<string,string?>
                { ["provider"] = connection.Provider, ["destination"] = action.DestinationReference,
                  ["reasonCode"] = outcome.ReasonCode, ["providerReference"] = outcome.ProviderReference }), ct);
            processed++;
            logger.LogInformation("Marketing channel action {ActionId} for company {CompanyId} completed provider dispatch with outcome {Outcome} and reason {ReasonCode}.",
                action.Id, action.CompanyId, outcome.Outcome, outcome.ReasonCode);
        }
        return processed;
    }

    public async Task<MarketingChannelActionDto?> ReconcileAsync(Guid companyId, Guid actionId, CancellationToken ct)
    {
        var action = await db.MarketingChannelActions.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == actionId, ct);
        if (action is null) return null;
        if (action.Status is not ("ambiguous" or "dispatched")) throw new InvalidOperationException("Only dispatched or ambiguous actions can be reconciled.");
        if (string.IsNullOrWhiteSpace(action.ProviderReference)) throw new InvalidOperationException("The provider did not return a reference; operator verification is required before retrying.");
        var connection = await db.MarketingChannelConnections.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == companyId && x.Id == action.MarketingChannelConnectionId, ct);
        var destination = await db.MarketingChannelDestinations.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId && x.MarketingChannelConnectionId == connection.Id && x.ProviderReference == action.DestinationReference, ct);
        var publisher = publishers.SingleOrDefault(x => x.Provider.Equals(connection.Provider, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Provider reconciliation is unavailable.");
        var outcome = await publisher.ReconcileAsync(action.DestinationReference, action.ProviderReference,
            destination?.SecretReference ?? connection.SecretReference, ct);
        if (outcome.Outcome == "succeeded") action.Reconcile(true);
        else if (outcome.Outcome == "failed") action.Reconcile(false);
        else return Map(action);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.System, null,
            "marketing.channel_action.reconciled", "marketing_channel_action", action.Id.ToString("N"),
            outcome.Outcome == "succeeded" ? AuditEventOutcomes.Succeeded : AuditEventOutcomes.Failed,
            outcome.SafeExplanation, DataSources: [connection.Provider], Metadata: new Dictionary<string,string?>
            { ["provider"] = connection.Provider, ["reasonCode"] = outcome.ReasonCode,
              ["providerReference"] = action.ProviderReference }), ct);
        return Map(action);
    }

    private static MarketingChannelActionDto Map(VirtualCompany.Domain.Entities.MarketingChannelAction x) =>
        new(x.Id, x.MarketingChannelConnectionId, x.DestinationReference, x.ActionType, x.PayloadJson, x.ScheduledUtc,
            x.Status, x.ApprovalRequestId, x.Version, x.AttemptCount, x.ProviderReference, x.FailureCode, x.ContentBriefVersion);
}

public abstract class MarketingHttpChannelPublisher(
    string provider,
    IHttpClientFactory clients,
    IPlatformSecretStore secrets) : IMarketingChannelPublisher
{
    public string Provider { get; } = provider;
    public abstract Task<MarketingChannelDispatchResult> PublishAsync(string destinationReference, string actionType,
        string payloadJson, string secretReference, CancellationToken ct);
    public abstract Task<MarketingChannelDispatchResult> ReconcileAsync(string destinationReference,
        string providerReference, string secretReference, CancellationToken ct);
    protected IHttpClientFactory Clients { get; } = clients;
    protected async Task<string?> TokenAsync(string reference, CancellationToken ct) =>
        (await secrets.GetAsync(reference, null, ct))?.Value;
    protected static MarketingChannelDispatchResult Classify(HttpStatusCode status, string? reference = null) =>
        status is HttpStatusCode.OK or HttpStatusCode.Created or HttpStatusCode.Accepted or HttpStatusCode.NoContent
            ? new("succeeded", reference, "published", "Provider accepted the action.")
            : status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? new("failed", null, "reauthorization_required", "Provider authorization must be renewed.", true)
                : (int)status == 429
                    ? new("retryable", null, "rate_limited", "Provider rate limit deferred the action.")
                    : (int)status >= 500
                        ? new("retryable", null, "provider_unavailable", "Provider is temporarily unavailable.")
                        : new("failed", null, "provider_validation_failed", "Provider rejected the action.");
    protected static MarketingChannelDispatchResult MissingSecret() =>
        new("failed", null, "reauthorization_required", "The protected provider credential is unavailable.", true);
    protected static MarketingChannelDispatchResult Ambiguous() =>
        new("ambiguous", null, "provider_outcome_unknown", "Provider outcome is unknown and requires reconciliation.");
}

public sealed class LinkedInMarketingChannelPublisher(
    IHttpClientFactory clients, IPlatformSecretStore secrets, IOptions<MarketingChannelDeliveryOptions> options)
    : MarketingHttpChannelPublisher("linkedin", clients, secrets)
{
    public override async Task<MarketingChannelDispatchResult> PublishAsync(string destinationReference, string actionType, string payloadJson, string secretReference, CancellationToken ct)
    {
        var token = await TokenAsync(secretReference, ct); if (string.IsNullOrWhiteSpace(token)) return MissingSecret();
        using var payload = JsonDocument.Parse(payloadJson);
        var text = payload.RootElement.GetProperty("text").GetString();
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.linkedin.com/rest/posts");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Restli-Protocol-Version", "2.0.0");
        request.Headers.Add("Linkedin-Version", options.Value.LinkedInVersion);
        request.Content = JsonContent.Create(new { author = destinationReference, commentary = text, visibility = "PUBLIC",
            distribution = new { feedDistribution = "MAIN_FEED", targetEntities = Array.Empty<object>(), thirdPartyDistributionChannels = Array.Empty<object>() },
            lifecycleState = "PUBLISHED", isReshareDisabledByAuthor = false });
        try
        {
            using var response = await Clients.CreateClient(nameof(LinkedInMarketingChannelPublisher)).SendAsync(request, ct);
            var reference = response.Headers.TryGetValues("x-restli-id", out var values) ? values.FirstOrDefault() : null;
            return Classify(response.StatusCode, reference);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Ambiguous(); }
        catch (HttpRequestException) { return Ambiguous(); }
    }
    public override async Task<MarketingChannelDispatchResult> ReconcileAsync(string destinationReference, string providerReference, string secretReference, CancellationToken ct)
    {
        var token=await TokenAsync(secretReference,ct);if(string.IsNullOrWhiteSpace(token))return MissingSecret();
        using var request=new HttpRequestMessage(HttpMethod.Get,$"https://api.linkedin.com/rest/posts/{Uri.EscapeDataString(providerReference)}");
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);request.Headers.Add("X-Restli-Protocol-Version","2.0.0");request.Headers.Add("Linkedin-Version",options.Value.LinkedInVersion);
        try{using var response=await Clients.CreateClient(nameof(LinkedInMarketingChannelPublisher)).SendAsync(request,ct);return response.StatusCode==HttpStatusCode.NotFound?new("failed",providerReference,"provider_not_found","LinkedIn did not find the publication."):Classify(response.StatusCode,providerReference);}catch(OperationCanceledException)when(!ct.IsCancellationRequested){return Ambiguous();}catch(HttpRequestException){return Ambiguous();}
    }
}

public sealed class XMarketingChannelPublisher(IHttpClientFactory clients, IPlatformSecretStore secrets)
    : MarketingHttpChannelPublisher("x", clients, secrets)
{
    public override async Task<MarketingChannelDispatchResult> PublishAsync(string destinationReference, string actionType, string payloadJson, string secretReference, CancellationToken ct)
    {
        var token = await TokenAsync(secretReference, ct); if (string.IsNullOrWhiteSpace(token)) return MissingSecret();
        using var payload = JsonDocument.Parse(payloadJson);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.x.com/2/tweets");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { text = payload.RootElement.GetProperty("text").GetString() });
        try
        {
            using var response = await Clients.CreateClient(nameof(XMarketingChannelPublisher)).SendAsync(request, ct);
            string? reference = null;
            if (response.IsSuccessStatusCode)
            {
                using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                reference = result.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("id", out var id) ? id.GetString() : null;
            }
            return Classify(response.StatusCode, reference);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Ambiguous(); }
        catch (HttpRequestException) { return Ambiguous(); }
    }
    public override async Task<MarketingChannelDispatchResult> ReconcileAsync(string destinationReference,string providerReference,string secretReference,CancellationToken ct)
    {var token=await TokenAsync(secretReference,ct);if(string.IsNullOrWhiteSpace(token))return MissingSecret();using var request=new HttpRequestMessage(HttpMethod.Get,$"https://api.x.com/2/tweets/{Uri.EscapeDataString(providerReference)}");request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);try{using var response=await Clients.CreateClient(nameof(XMarketingChannelPublisher)).SendAsync(request,ct);return response.StatusCode==HttpStatusCode.NotFound?new("failed",providerReference,"provider_not_found","X did not find the post."):Classify(response.StatusCode,providerReference);}catch(OperationCanceledException)when(!ct.IsCancellationRequested){return Ambiguous();}catch(HttpRequestException){return Ambiguous();}}
}

public sealed class MetaMarketingChannelPublisher(
    IHttpClientFactory clients, IPlatformSecretStore secrets, IOptions<MarketingChannelDeliveryOptions> options)
    : MarketingHttpChannelPublisher("meta", clients, secrets)
{
    public override async Task<MarketingChannelDispatchResult> PublishAsync(string destinationReference, string actionType, string payloadJson, string secretReference, CancellationToken ct)
    {
        var token = await TokenAsync(secretReference, ct); if (string.IsNullOrWhiteSpace(token)) return MissingSecret();
        using var payload = JsonDocument.Parse(payloadJson);
        var text = payload.RootElement.GetProperty("text").GetString() ?? string.Empty;
        try
        {
            return actionType.Equals("publish_instagram_media", StringComparison.OrdinalIgnoreCase)
                ? await PublishInstagramAsync(destinationReference, text, payload.RootElement, token, ct)
                : await PublishFacebookAsync(destinationReference, text, token, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Ambiguous(); }
        catch (HttpRequestException) { return Ambiguous(); }
    }
    private async Task<MarketingChannelDispatchResult> PublishFacebookAsync(string destination, string text, string token, CancellationToken ct)
    {
        using var request = Request(HttpMethod.Post, $"https://graph.facebook.com/{options.Value.MetaGraphVersion}/{Uri.EscapeDataString(destination)}/feed", token);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["message"] = text });
        using var response = await Clients.CreateClient(nameof(MetaMarketingChannelPublisher)).SendAsync(request, ct);
        return await WithJsonReference(response, ct);
    }
    private async Task<MarketingChannelDispatchResult> PublishInstagramAsync(string destination, string text, JsonElement payload, string token, CancellationToken ct)
    {
        if (!payload.TryGetProperty("imageUrl", out var image) || string.IsNullOrWhiteSpace(image.GetString()))
            return new("failed", null, "image_url_required", "Instagram publishing requires an approved public image URL.");
        using var create = Request(HttpMethod.Post, $"https://graph.facebook.com/{options.Value.MetaGraphVersion}/{Uri.EscapeDataString(destination)}/media", token);
        create.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["image_url"] = image.GetString()!, ["caption"] = text });
        using var createResponse = await Clients.CreateClient(nameof(MetaMarketingChannelPublisher)).SendAsync(create, ct);
        if (!createResponse.IsSuccessStatusCode) return Classify(createResponse.StatusCode);
        using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync(ct));
        var container = created.RootElement.GetProperty("id").GetString();
        using var publish = Request(HttpMethod.Post, $"https://graph.facebook.com/{options.Value.MetaGraphVersion}/{Uri.EscapeDataString(destination)}/media_publish", token);
        publish.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["creation_id"] = container! });
        using var publishResponse = await Clients.CreateClient(nameof(MetaMarketingChannelPublisher)).SendAsync(publish, ct);
        return await WithJsonReference(publishResponse, ct);
    }
    private static HttpRequestMessage Request(HttpMethod method, string uri, string token)
    { var request = new HttpRequestMessage(method, uri); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token); return request; }
    private static async Task<MarketingChannelDispatchResult> WithJsonReference(HttpResponseMessage response, CancellationToken ct)
    {
        string? reference = null;
        if (response.IsSuccessStatusCode)
        { using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)); reference = result.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null; }
        return Classify(response.StatusCode, reference);
    }
    public override async Task<MarketingChannelDispatchResult> ReconcileAsync(string destinationReference,string providerReference,string secretReference,CancellationToken ct)
    {var token=await TokenAsync(secretReference,ct);if(string.IsNullOrWhiteSpace(token))return MissingSecret();using var request=Request(HttpMethod.Get,$"https://graph.facebook.com/{options.Value.MetaGraphVersion}/{Uri.EscapeDataString(providerReference)}?fields=id",token);try{using var response=await Clients.CreateClient(nameof(MetaMarketingChannelPublisher)).SendAsync(request,ct);return response.StatusCode==HttpStatusCode.NotFound?new("failed",providerReference,"provider_not_found","Meta did not find the publication."):Classify(response.StatusCode,providerReference);}catch(OperationCanceledException)when(!ct.IsCancellationRequested){return Ambiguous();}catch(HttpRequestException){return Ambiguous();}}
}

public sealed class MarketingChannelDispatchBackgroundService(
    IServiceScopeFactory scopes,
    IOptionsMonitor<MarketingChannelDeliveryOptions> options,
    ILogger<MarketingChannelDispatchBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var current = options.CurrentValue;
            if (current.Enabled)
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<IMarketingChannelDispatchService>()
                        .DispatchDueAsync(DateTime.UtcNow, current.BatchSize, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception exception) { logger.LogError(exception, "Marketing channel delivery worker failed."); }
            }
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(current.PollSeconds, 5, 300)), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}
