using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using VirtualCompany.Api.Hubs;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Tests;

public sealed class SalesPresentationSignalRIntegrationTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();

    [Fact]
    public async Task Stage_and_private_clients_receive_separate_payloads_and_reconnect_gets_current_snapshot()
    {
        var seed = await SeedAsync();
        await using var stage = Connection(seed.CompanyId);
        await using var presenter = Connection(seed.CompanyId);
        var stageEvent = new TaskCompletionSource<SalesPresentationStageSnapshotDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var privateEvent = new TaskCompletionSource<SalesPresentationPrivateSnapshotDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stagePrivateLeak = new TaskCompletionSource<SalesPresentationPrivateSnapshotDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        stage.On<SalesPresentationStageSnapshotDto>("StageStateChanged", value => stageEvent.TrySetResult(value));
        stage.On<SalesPresentationPrivateSnapshotDto>("PrivateStateChanged", value => stagePrivateLeak.TrySetResult(value));
        presenter.On<SalesPresentationPrivateSnapshotDto>("PrivateStateChanged", value => privateEvent.TrySetResult(value));

        await stage.StartAsync();
        await presenter.StartAsync();
        var initialStage = await stage.InvokeAsync<SalesPresentationStageSnapshotDto>("JoinStage", seed.SessionId);
        var initialPrivate = await presenter.InvokeAsync<SalesPresentationPrivateSnapshotDto>("JoinPrivate", seed.SessionId);
        var result = await presenter.InvokeAsync<SalesPresentationCommandResultDto>(
            "ExecutePresentationCommand", seed.SessionId, SalesPresentationToolNames.Next,
            new SalesPresentationCommandRequest(Guid.NewGuid(), 1, initialPrivate.Stage.Version));

        var deliveredStage = await WaitAsync(stageEvent.Task);
        var deliveredPrivate = await WaitAsync(privateEvent.Task);
        Assert.Equal("accepted", result.Disposition);
        Assert.Equal(1, deliveredStage.Sequence);
        Assert.Equal(1, deliveredPrivate.Stage.Sequence);
        Assert.Equal("private presenter note", deliveredPrivate.SpeakerNotes);
        Assert.DoesNotContain("speakerNotes", JsonSerializer.Serialize(deliveredStage), StringComparison.OrdinalIgnoreCase);
        var possibleLeak = await Task.WhenAny(stagePrivateLeak.Task, Task.Delay(TimeSpan.FromMilliseconds(200)));
        Assert.NotSame(stagePrivateLeak.Task, possibleLeak);

        await presenter.StopAsync();
        await using var reconnected = Connection(seed.CompanyId);
        await reconnected.StartAsync();
        var recovered = await reconnected.InvokeAsync<SalesPresentationPrivateSnapshotDto>("JoinPrivate", seed.SessionId);
        Assert.Equal(1, recovered.Stage.Sequence);
        Assert.Equal(deliveredPrivate.Stage.Version, recovered.Stage.Version);
    }

    [Fact]
    public async Task Http_and_hub_reject_unverified_company_contexts()
    {
        var seed = await SeedAsync();
        using var anonymous = _factory.CreateClient();
        anonymous.DefaultRequestHeaders.Add(CompanyContextResolutionMiddleware.CompanyHeaderName, seed.CompanyId.ToString("D"));
        using var response = await anonymous.GetAsync(
            $"/api/sales/meeting-sessions/{seed.SessionId:D}/presentation/current-slide");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        await using var connection = Connection(seed.OtherCompanyId);
        try
        {
            await connection.StartAsync();
        }
        catch
        {
            return;
        }
        await Task.Delay(100);
        Assert.Equal(HubConnectionState.Disconnected, connection.State);
    }

    private async Task<Seed> SeedAsync()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var leadId = Guid.NewGuid();
        var invitationId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var deckId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await _factory.SeedAsync(db =>
        {
            db.Users.Add(new User(userId, "presentation-owner@example.com", "Presentation Owner", "dev-header", "presentation-owner"));
            db.Companies.AddRange(new Company(companyId, "Presentation Tenant"), new Company(otherCompanyId, "Other Tenant"));
            db.CompanyMemberships.Add(new CompanyMembership(
                Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            db.CustomerCompanies.Add(new CustomerCompany(customerId, companyId, "Customer"));
            db.Contacts.Add(new Contact(contactId, companyId, "Buyer", "buyer@example.com", customerId));
            db.Leads.Add(new Lead(leadId, companyId, "Lead", SalesPipelineStage.QualifiedStageId,
                SalesStatuses.Qualified, contactId, customerId));
            var external = new ExternalAccountConnection(
                connectionId, companyId, userId, ExternalAccountProvider.Microsoft365,
                "presentation-owner@example.com", "Presentation Owner", "presentation-account", "external:presentation");
            external.SetStatus(ExternalConnectionStatus.Active);
            var calendar = new CalendarConnection(
                connectionId, companyId, userId, connectionId, ExternalAccountProvider.Microsoft365,
                "presentation-owner@example.com", "Presentation Owner");
            calendar.SetStatus(ExternalConnectionStatus.Active);
            db.ExternalAccountConnections.Add(external);
            db.CalendarConnections.Add(calendar);
            db.SalesMeetingInvitations.Add(new SalesMeetingInvitation(
                invitationId, companyId, leadId, null, contactId, connectionId,
                ExternalAccountProvider.Microsoft365, "owner@example.com", "buyer@example.com", "Buyer",
                "Product meeting", "Confirm fit", now.AddHours(1), now.AddHours(2),
                "Europe/Stockholm", null, false, userId));
            db.SalesMeetingSessions.Add(new SalesMeetingSession(
                sessionId, companyId, invitationId, leadId, null, contactId, customerId,
                "Confirm fit", "Finance team", 45, null, "provider-meeting",
                SalesMeetingConsentStatus.Pending, SalesMeetingRetentionPolicy.Standard, 365,
                now.AddHours(2), userId, now));
            db.Agents.Add(new Agent(agentId, companyId, "alex-sales", "Alex", "Sales", "Sales", null,
                AgentSeniority.Senior, AgentStatus.Active));
            var deck = new SalesPresentationDeck(
                deckId, companyId, sessionId, agentId, 1, "Board deck", "board.pptx",
                "application/vnd.openxmlformats-officedocument.presentationml.presentation", 100,
                new string('a', 64), "safe/deck.pptx", null, userId, now);
            deck.BeginProcessing(now, TimeSpan.FromMinutes(10));
            deck.MarkProcessed(1, "test", "1", "static", 1, now);
            deck.Activate(now);
            db.SalesPresentationDecks.Add(deck);
            db.SalesPresentationSlides.Add(new SalesPresentationSlide(
                Guid.NewGuid(), companyId, deckId, 1, 1, "Opening", "Welcome",
                "private presenter note", "safe/1.svg", null, 1600, 900, 12192000, 6858000,
                new string('b', 64), "Open", 60, "Continue", now));
            return Task.CompletedTask;
        });
        return new Seed(companyId, otherCompanyId, sessionId);
    }

    private HubConnection Connection(Guid companyId) => new HubConnectionBuilder()
        .WithUrl(new Uri(_factory.Server.BaseAddress, $"{SalesMeetingHub.Route}?companyId={companyId:D}"), options =>
        {
            options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            options.Headers.Add(DevHeaderAuthenticationDefaults.SubjectHeader, "presentation-owner");
            options.Headers.Add(DevHeaderAuthenticationDefaults.EmailHeader, "presentation-owner@example.com");
            options.Headers.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, "Presentation Owner");
        })
        .Build();

    private static async Task<T> WaitAsync<T>(Task<T> task)
    {
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(3)));
        if (completed != task) throw new TimeoutException("Timed out waiting for a presentation event.");
        return await task;
    }

    public void Dispose() => _factory.Dispose();

    private sealed record Seed(Guid CompanyId, Guid OtherCompanyId, Guid SessionId);
}
