using System.Text.Json;
using VirtualCompany.Application.Sales;
namespace VirtualCompany.Api.Tests;
public sealed class SalesRoomPublicAudienceTests
{
    [Fact]
    public async Task Only_admitted_guests_see_public_participants_and_removal_revokes_projection()
    {
        await using var fixture = await RoomFixture.Create(); await fixture.Ready();
        var guest = await fixture.Guest(); var waiting = await fixture.Guest();
        Assert.Empty((await fixture.Service.GuestStatusAsync(guest.Credential, fixture.Room, default)).Participants);
        await fixture.Service.DecideAsync(fixture.Company, fixture.Actor, fixture.Room, guest.Participant.ParticipantId, "admit", new(Guid.NewGuid(), (await fixture.View()).Version), default);
        var view = await fixture.Service.GuestStatusAsync(guest.Credential, fixture.Room, default);
        Assert.Equal(2, view.Participants.Count);
        Assert.DoesNotContain(view.Participants, x => x.Id == waiting.Participant.ParticipantId);
        Assert.All(view.Participants, x => Assert.StartsWith("human-", x.MediaIdentity));
        var serialized = JsonSerializer.Serialize(view);
        foreach (var privateValue in new[] { fixture.Company.ToString(), fixture.Actor.ToString(), guest.Credential, "Operations", "MemberUserId", "SessionHash", "ProviderReference" }) Assert.DoesNotContain(privateValue, serialized);
        await Assert.ThrowsAsync<SalesRoomAccessException>(() => fixture.Service.GuestStatusAsync(guest.Credential, Guid.NewGuid(), default));
        await Assert.ThrowsAsync<SalesRoomAccessException>(() => fixture.Service.GetAsync(Guid.NewGuid(), fixture.Actor, fixture.Room, default));
        await fixture.Service.DecideAsync(fixture.Company, fixture.Actor, fixture.Room, guest.Participant.ParticipantId, "remove", new(Guid.NewGuid(), (await fixture.View()).Version), default);
        await Assert.ThrowsAsync<SalesRoomAccessException>(() => fixture.Service.GuestStatusAsync(guest.Credential, fixture.Room, default));
    }
    [Fact]
    public async Task Status_polling_has_an_independent_budget_and_commands_remain_limited()
    {
        using var factory = new TestWebApplicationFactory(); using var client = factory.CreateClient();
        var room = Guid.NewGuid();
        for (var i = 0; i < 125; i++)
        {
            using var response = await client.GetAsync($"/api/sales/browser-room-guests/{room}");
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        }
        for (var i = 0; i < 120; i++)
        {
            using var response = await client.PostAsync($"/api/sales/browser-room-guests/{room}/media-token", null);
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        }
        using var limited = await client.PostAsync($"/api/sales/browser-room-guests/{room}/media-token", null);
        Assert.Equal(System.Net.HttpStatusCode.TooManyRequests, limited.StatusCode);
    }
    [Fact]
    public async Task Organizer_identity_projection_matches_issued_media_identity()
    {
        await using var fixture = await RoomFixture.Create(); await fixture.Ready();
        var view = await fixture.View(); var organizer = Assert.Single(view.Participants);
        Assert.True(organizer.IsOrganizer);
        var token = await fixture.Service.HostTokenAsync(fixture.Company, fixture.Actor, fixture.Room, default);
        Assert.Equal(token.Identity, organizer.MediaIdentity);
    }
}

