using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Hubs;

public interface ISalesMeetingHubClient
{
    Task StageStateChanged(SalesPresentationStageSnapshotDto snapshot);
    Task PrivateStateChanged(SalesPresentationPrivateSnapshotDto snapshot);
    Task StageRenderAcknowledged(SalesPresentationRenderAcknowledgement acknowledgement);
    Task BrowserAudienceChanged(SalesBrowserPresentationReadinessDto readiness);
    Task AgentPlaybackStopRequested(SalesRoomPlaybackStopRequest request);
    Task AgentPlaybackStarted(SalesRoomPlaybackStartRequest request);
}

public sealed class SalesMeetingHub(
    ICompanyMembershipContextResolver memberships,
    ICompanyContextAccessor companyContext,
    ISalesPresentationRuntimeService runtime,
    ISalesPresentationStageAccessService stageAccess,
    ISalesPresentationStagePresenceService stagePresence,
    ISalesBrowserPresentationService browserPresentations,
    ISalesRoomAgentService roomAgents) : Hub<ISalesMeetingHubClient>
{
    public const string Route = "/hubs/sales-meeting";
    private const string StageAccessItem = "sales.presentation.stage-access";
    private const string StageBindingItem = "sales.presentation.stage-binding";
    private const string BrowserAccessItem = "sales.presentation.browser-access";

    public override async Task OnConnectedAsync()
    {
        if (TryReadBrowserRoom(out var browserRoomId, out var browserCredential) &&
            !string.IsNullOrWhiteSpace(browserCredential))
        {
            try
            {
                Context.Items[BrowserAccessItem] = await browserPresentations.ValidateGuestAsync(
                    browserCredential, browserRoomId, Context.ConnectionAborted);
                await base.OnConnectedAsync();
                return;
            }
            catch (SalesRoomAccessException)
            {
                Context.Abort();
                return;
            }
        }

        if (TryReadStageAccess(out var stageSessionId, out var stageToken))
        {
            try
            {
                var access = await stageAccess.ValidateAsync(stageSessionId, stageToken, Context.ConnectionAborted);
                Context.Items[StageAccessItem] = new StageConnectionAccess(access, stageToken);
                await base.OnConnectedAsync();
                return;
            }
            catch (SalesPresentationStageAccessException)
            {
                Context.Abort();
                return;
            }
        }

        if (!TryReadCompanyId(out var requestedCompanyId) ||
            await memberships.ResolveAsync(requestedCompanyId, Context.ConnectionAborted) is not { } membership)
        {
            Context.Abort();
            return;
        }

        // The requested id only selects a candidate membership. The verified persisted
        // membership becomes the scoped tenant context used by every filtered query.
        companyContext.SetCompanyContext(membership);
        Context.Items[nameof(membership.CompanyId)] = membership.CompanyId;
        Context.Items[nameof(membership.UserId)] = membership.UserId;
        await base.OnConnectedAsync();
    }

    public async Task<SalesBrowserPresentationPublicDto> JoinBrowserStage(Guid roomId)
    {
        SalesBrowserPresentationAccessContext access;
        SalesBrowserPresentationPublicDto snapshot;
        if (Context.Items.TryGetValue(BrowserAccessItem, out var item) &&
            item is SalesBrowserPresentationAccessContext guestAccess)
        {
            if (guestAccess.RoomId != roomId) throw new HubException("The browser-room grant does not match this room.");
            access = guestAccess;
            var stage = await browserPresentations.ConnectAsync(access, Context.ConnectionAborted);
            var current = await browserPresentations.GetGuestAsync(
                ReadBrowserCredential(), roomId, Context.ConnectionAborted);
            snapshot = current;
            await Clients.Group(PrivateGroup(access.CompanyId, access.SessionId)).BrowserAudienceChanged(stage);
        }
        else
        {
            var identity = await ContextIdentityAsync();
            access = await browserPresentations.ValidateHostAsync(
                identity.CompanyId, identity.UserId, roomId, Context.ConnectionAborted);
            var current = await browserPresentations.GetHostAsync(
                identity.CompanyId, identity.UserId, roomId, Context.ConnectionAborted);
            snapshot = new(roomId, current.ParticipantId, current.ActorGeneration, current.Presentation.Stage);
            var readiness = await browserPresentations.ConnectAsync(access, Context.ConnectionAborted);
            await Clients.Group(PrivateGroup(access.CompanyId, access.SessionId)).BrowserAudienceChanged(readiness);
        }

        await Groups.AddToGroupAsync(Context.ConnectionId,
            StageGroup(access.CompanyId, access.SessionId), Context.ConnectionAborted);
        Context.Items[BrowserAccessItem] = access;
        Context.Items[StageBindingItem] = new StageConnectionBinding(
            access.CompanyId, access.SessionId, snapshot.Stage.DeckId, snapshot.Stage.DeckVersion, access);
        await stagePresence.RegisterAsync(new SalesPresentationStagePresence(
            access.CompanyId, access.SessionId, snapshot.Stage.DeckId, snapshot.Stage.DeckVersion,
            Context.ConnectionId, true, DateTime.UtcNow), Context.ConnectionAborted);
        return snapshot;
    }

    public async Task<SalesPresentationStageSnapshotDto> JoinStage(Guid sessionId)
    {
        Guid companyId;
        SalesPresentationStageSnapshotDto snapshot;
        if (Context.Items.TryGetValue(StageAccessItem, out var item) && item is StageConnectionAccess stage)
        {
            if (stage.Context.SessionId != sessionId) throw new HubException("The stage grant does not match this meeting.");
            companyId = stage.Context.CompanyId;
            snapshot = await stageAccess.GetSnapshotAsync(sessionId, stage.Token, Context.ConnectionAborted);
        }
        else
        {
            var identity = await ContextIdentityAsync();
            companyId = identity.CompanyId;
            snapshot = (await runtime.GetCurrentAsync(companyId, identity.UserId, sessionId, Context.ConnectionAborted))?.Stage
                ?? throw new HubException("Sales presentation was not found.");
            await runtime.RecordReconnectAsync(companyId, identity.UserId, sessionId, "stage", Context.ConnectionAborted);
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, StageGroup(companyId, sessionId), Context.ConnectionAborted);
        var binding = new StageConnectionBinding(companyId, sessionId, snapshot.DeckId, snapshot.DeckVersion, null);
        Context.Items[StageBindingItem] = binding;
        await stagePresence.RegisterAsync(new SalesPresentationStagePresence(companyId, sessionId,
            snapshot.DeckId, snapshot.DeckVersion, Context.ConnectionId, true, DateTime.UtcNow), Context.ConnectionAborted);
        return snapshot;
    }

    public async Task<SalesPresentationPrivateSnapshotDto> JoinPrivate(Guid sessionId)
    {
        var (companyId, userId) = await ContextIdentityAsync();
        var snapshot = await runtime.GetCurrentAsync(companyId, userId, sessionId, Context.ConnectionAborted)
            ?? throw new HubException("Sales presentation was not found.");
        await Groups.AddToGroupAsync(Context.ConnectionId, PrivateGroup(companyId, sessionId), Context.ConnectionAborted);
        await runtime.RecordReconnectAsync(companyId, userId, sessionId, "private", Context.ConnectionAborted);
        return snapshot.Private;
    }

    public async Task<SalesPresentationCommandResultDto> ExecutePresentationCommand(
        Guid sessionId, string toolName, SalesPresentationCommandRequest request)
    {
        var (companyId, userId) = await ContextIdentityAsync();
        try
        {
            return await runtime.ExecuteAsync(
                companyId, userId, sessionId, toolName, request,
                Context.ConnectionId, Context.ConnectionAborted)
                ?? throw new HubException("Sales presentation was not found.");
        }
        catch (SalesPresentationRuntimeConflictException exception)
        {
            throw new HubException($"{exception.Code}: {exception.Message}");
        }
    }

    public async Task<SalesPresentationStageSnapshotDto> GetCurrentSlide(Guid sessionId)
    {
        var (companyId, userId) = await ContextIdentityAsync();
        var snapshot = await runtime.GetCurrentAsync(companyId, userId, sessionId, Context.ConnectionAborted)
            ?? throw new HubException("Sales presentation was not found.");
        return snapshot.Stage;
    }

    public async Task<IReadOnlyList<SalesPresentationSearchResultDto>> SearchSlides(Guid sessionId, string query)
    {
        var (companyId, userId) = await ContextIdentityAsync();
        return await runtime.SearchAsync(companyId, userId, sessionId, query, Context.ConnectionAborted);
    }

    public async Task AcknowledgeStageRender(Guid sessionId, Guid deckId, int deckVersion,
        int slideNumber, long presentationSequence, long presentationVersion, DateTime renderedUtc)
    {
        if (!Context.Items.TryGetValue(StageBindingItem, out var item) || item is not StageConnectionBinding binding ||
            binding.SessionId != sessionId ||
            (binding.BrowserAccess is null && (binding.DeckId != deckId || binding.DeckVersion != deckVersion)))
            throw new HubException("An active authorized stage connection is required.");

        var acknowledgement = new SalesPresentationRenderAcknowledgement(binding.CompanyId,
            sessionId, deckId, deckVersion, slideNumber, presentationSequence, presentationVersion,
            Context.ConnectionId, renderedUtc.Kind == DateTimeKind.Utc ? renderedUtc : renderedUtc.ToUniversalTime());

        if (binding.BrowserAccess is { } browserAccess)
        {
            browserAccess = browserAccess.IsOrganizer
                ? await browserPresentations.ValidateHostAsync(
                    browserAccess.CompanyId, (await ContextIdentityAsync()).UserId,
                    browserAccess.RoomId, Context.ConnectionAborted)
                : await browserPresentations.ValidateGuestAsync(
                    ReadBrowserCredential(), browserAccess.RoomId, Context.ConnectionAborted);
            var readiness = await browserPresentations.AcknowledgeAsync(
                browserAccess, acknowledgement, Context.ConnectionAborted);
            // Browser-room deck selection can change while the connection stays open.
            // The durable browser service has already validated this exact snapshot,
            // so refresh the route-neutral in-memory presence before acknowledging it.
            binding = binding with { DeckId = deckId, DeckVersion = deckVersion };
            Context.Items[StageBindingItem] = binding;
            await stagePresence.RegisterAsync(new SalesPresentationStagePresence(
                binding.CompanyId, binding.SessionId, deckId, deckVersion,
                Context.ConnectionId, true, DateTime.UtcNow), Context.ConnectionAborted);
            await stagePresence.AcknowledgeAsync(acknowledgement, Context.ConnectionAborted);
            await Clients.Group(PrivateGroup(binding.CompanyId, sessionId))
                .BrowserAudienceChanged(readiness);
            return;
        }

        SalesPresentationStageSnapshotDto current;
        if (Context.Items.TryGetValue(StageAccessItem, out var accessItem) && accessItem is StageConnectionAccess stage)
            current = await stageAccess.GetSnapshotAsync(sessionId, stage.Token, Context.ConnectionAborted);
        else
        {
            var identity = await ContextIdentityAsync();
            current = (await runtime.GetCurrentAsync(identity.CompanyId, identity.UserId, sessionId,
                Context.ConnectionAborted))?.Stage ?? throw new HubException("Sales presentation was not found.");
        }

        if (current.DeckId != deckId || current.DeckVersion != deckVersion || current.SlideNumber != slideNumber ||
            current.Sequence != presentationSequence || current.Version != presentationVersion)
            throw new HubException("The render acknowledgement is stale; reload the authoritative stage state.");

        await stagePresence.AcknowledgeAsync(acknowledgement, Context.ConnectionAborted);
        await Clients.Group(PrivateGroup(binding.CompanyId, sessionId)).StageRenderAcknowledged(acknowledgement);
    }

    public async Task AcknowledgeAgentPlaybackStopped(Guid stopId, long responseGeneration)
    {
        if (!Context.Items.TryGetValue(BrowserAccessItem, out var item) ||
            item is not SalesBrowserPresentationAccessContext access)
            throw new HubException("An active authorized browser-room connection is required.");
        access = access.IsOrganizer
            ? await browserPresentations.ValidateHostAsync(access.CompanyId, (await ContextIdentityAsync()).UserId,
                access.RoomId, Context.ConnectionAborted)
            : await browserPresentations.ValidateGuestAsync(ReadBrowserCredential(), access.RoomId, Context.ConnectionAborted);
        Context.Items[BrowserAccessItem] = access;
        try
        {
            await roomAgents.AcknowledgePlaybackStopAsync(access,
                new(stopId, responseGeneration, Context.ConnectionId), Context.ConnectionAborted);
        }
        catch (SalesRoomAgentException exception) { throw new HubException(exception.Code + ": " + exception.Message); }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue(StageBindingItem, out var item) && item is StageConnectionBinding binding)
        {
            await stagePresence.DisconnectAsync(binding.CompanyId, binding.SessionId, Context.ConnectionId,
                CancellationToken.None);
            if (binding.BrowserAccess is { } browserAccess)
            {
                try
                {
                    var readiness = await browserPresentations.DisconnectAsync(browserAccess, CancellationToken.None);
                    await Clients.Group(PrivateGroup(binding.CompanyId, binding.SessionId))
                        .BrowserAudienceChanged(readiness);
                }
                catch (SalesRoomAccessException) { }
            }
        }
        await base.OnDisconnectedAsync(exception);
    }

    public static string StageGroup(Guid companyId, Guid sessionId) =>
        $"sales-meeting:{companyId:N}:{sessionId:N}:stage";

    public static string PrivateGroup(Guid companyId, Guid sessionId) =>
        $"sales-meeting:{companyId:N}:{sessionId:N}:private";

    private async Task<(Guid CompanyId, Guid UserId)> ContextIdentityAsync()
    {
        if (Context.Items.TryGetValue(nameof(ResolvedCompanyMembershipContext.CompanyId), out var company) && company is Guid companyId &&
            await memberships.ResolveAsync(companyId, Context.ConnectionAborted) is { } verified)
        {
            companyContext.SetCompanyContext(verified);
            return (verified.CompanyId, verified.UserId);
        }
        throw new HubException("A verified company membership is required.");
    }

    private bool TryReadCompanyId(out Guid companyId)
    {
        var value = Context.GetHttpContext()?.Request.Query["companyId"].FirstOrDefault();
        return Guid.TryParse(value, out companyId) && companyId != Guid.Empty;
    }

    private bool TryReadStageAccess(out Guid sessionId, out string token)
    {
        var request = Context.GetHttpContext()?.Request;
        token = request?.Query["stageToken"].FirstOrDefault()?.Trim() ?? string.Empty;
        return Guid.TryParse(request?.Query["sessionId"].FirstOrDefault(), out sessionId) &&
               sessionId != Guid.Empty && token.Length is > 0 and <= 8192;
    }

    private bool TryReadBrowserRoom(out Guid roomId, out string credential)
    {
        var request = Context.GetHttpContext()?.Request;
        credential = request?.Headers["X-Sales-Room-Session"].Count == 1
            ? request.Headers["X-Sales-Room-Session"].ToString()
            : string.Empty;
        return Guid.TryParse(request?.Query["roomId"].FirstOrDefault(), out roomId) &&
               roomId != Guid.Empty && credential.Length <= 100;
    }

    private string ReadBrowserCredential()
    {
        var credential = Context.GetHttpContext()?.Request.Headers["X-Sales-Room-Session"].ToString();
        return string.IsNullOrWhiteSpace(credential)
            ? throw new HubException("The browser-room session is missing.")
            : credential;
    }

    private sealed record StageConnectionAccess(SalesPresentationStageAccessContext Context, string Token);
    private sealed record StageConnectionBinding(
        Guid CompanyId, Guid SessionId, Guid DeckId, int DeckVersion,
        SalesBrowserPresentationAccessContext? BrowserAccess);
}

public sealed class SignalRSalesPresentationEventPublisher(
    IHubContext<SalesMeetingHub, ISalesMeetingHubClient> hubContext) : ISalesPresentationEventPublisher
{
    public async Task PublishAsync(
        Guid companyId, Guid sessionId, SalesPresentationAuthoritativeSnapshotDto snapshot,
        CancellationToken cancellationToken)
    {
        await hubContext.Clients.Group(SalesMeetingHub.StageGroup(companyId, sessionId))
            .StageStateChanged(snapshot.Stage);
        await hubContext.Clients.Group(SalesMeetingHub.PrivateGroup(companyId, sessionId))
            .PrivateStateChanged(snapshot.Private);
    }
}

public sealed class SignalRSalesRoomFloorEventPublisher(
    IHubContext<SalesMeetingHub, ISalesMeetingHubClient> hubContext) : ISalesRoomFloorEventPublisher
{
    public Task RequestPlaybackStopAsync(Guid companyId, Guid sessionId,
        SalesRoomPlaybackStopRequest request, CancellationToken ct) =>
        hubContext.Clients.Group(SalesMeetingHub.StageGroup(companyId, sessionId)).AgentPlaybackStopRequested(request);

    public Task AllowPlaybackAsync(Guid companyId, Guid sessionId,
        SalesRoomPlaybackStartRequest request, CancellationToken ct) =>
        hubContext.Clients.Group(SalesMeetingHub.StageGroup(companyId, sessionId)).AgentPlaybackStarted(request);
}
