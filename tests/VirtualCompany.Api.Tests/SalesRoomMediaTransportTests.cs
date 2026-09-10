using Google.Protobuf;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Sales;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class SalesRoomMediaTransportTests
{
    private static SalesRoomMediaOptions Configured() => new()
    { Enabled = true, Url = "wss://isolated.example.test", ApiKey = "test-key", ApiSecret = new string('x', 32) };
    private static LiveKitSalesRoomMediaTransport Adapter(SalesRoomMediaOptions options, HttpMessageHandler? handler = null) =>
        new(Options.Create(options), new Factory(handler ?? new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent("{}") })));
    private static SalesRoomMediaScope Scope() => new(Guid.NewGuid(), Guid.NewGuid());

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void Browser_settings_are_independent_of_legacy_voice(bool browserEnabled, bool legacyEnabled)
    {
        var values = new Dictionary<string, string?>
        {
            ["SalesBrowserRoom:Enabled"] = browserEnabled.ToString(),
            ["SalesBrowserRoom:Url"] = "wss://isolated.example.test",
            ["SalesBrowserRoom:ApiKey"] = "test-key",
            ["SalesBrowserRoom:ApiSecret"] = new string('x', 32),
            ["SalesMeetingVoice:Enabled"] = legacyEnabled.ToString(),
            ["SalesMeetingVoice:PilotApproved"] = "true",
            ["TeamsPresenter:Enabled"] = legacyEnabled.ToString()
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection(); services.AddSalesInfrastructure(configuration);
        services.AddOptions<SalesMeetingVoiceOptions>().Bind(configuration.GetSection("SalesMeetingVoice"));
        using var provider = services.BuildServiceProvider();
        var browser = provider.GetRequiredService<ISalesRoomMediaTransport>().GetReadiness();
        Assert.Equal(browserEnabled, browser.Enabled);
        Assert.Equal(browserEnabled ? "configured_unverified" : "unavailable", browser.State);
        Assert.Equal(browserEnabled ? "native_not_probed" : "disabled", browser.ReasonCode);
        Assert.Equal(legacyEnabled, provider.GetRequiredService<IOptions<TeamsPresenterOptions>>().Value.Enabled);
        Assert.Equal(legacyEnabled, provider.GetRequiredService<IOptions<SalesMeetingVoiceOptions>>().Value.Enabled);
        Assert.Equal("browser_webrtc", provider.GetRequiredService<IOptions<SalesMeetingVoiceOptions>>().Value.MediaRoute);
    }
    [Fact]
    public void Ready_configuration_is_not_claimed_as_live_verification()
    {
        var health = Adapter(Configured()).GetReadiness();
        Assert.True(health.Configured); Assert.False(health.NativeAvailable);
        Assert.Equal("configured_unverified", health.State);
    }
    [Theory]
    [InlineData("http://example.test")] [InlineData("wss://user:password@example.test")]
    [InlineData("wss://example.test?secret=x")] [InlineData("wss://example.test/other")]
    public void Invalid_endpoints_fail_closed(string url)
    {
        var options = Configured(); options.Url = url;
        Assert.Equal("invalid_endpoint", Adapter(options).GetReadiness().ReasonCode);
        Assert.Throws<SalesRoomMediaException>(() => Adapter(options).IssueToken(Scope(), new(Guid.NewGuid(), false)));
    }
    [Fact]
    public void Room_scope_and_token_are_tenant_bound_and_narrow()
    {
        var scope = Scope(); var other = scope with { CompanyId = Guid.NewGuid() };
        Assert.NotEqual(LiveKitSalesRoomMediaTransport.RoomName(scope), LiveKitSalesRoomMediaTransport.RoomName(other));
        var result = Adapter(Configured()).IssueToken(scope, new(Guid.NewGuid(), true, 5));
        Assert.DoesNotContain(result.Token, result.ToString());
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        using var video = JsonDocument.Parse(jwt.Claims.Single(c => c.Type == "video").Value);
        Assert.Equal(LiveKitSalesRoomMediaTransport.RoomName(scope), video.RootElement.GetProperty("room").GetString());
        Assert.False(video.RootElement.GetProperty("canPublishData").GetBoolean());
        Assert.Equal("microphone", Assert.Single(video.RootElement.GetProperty("canPublishSources").EnumerateArray()).GetString());
        Assert.False(video.RootElement.TryGetProperty("roomAdmin", out var admin) && admin.GetBoolean());
        Assert.InRange(jwt.ValidTo - DateTime.UtcNow, TimeSpan.FromSeconds(110), TimeSpan.FromSeconds(121));
    }
    [Fact]
    public void Token_cannot_outlive_the_room_or_participant_deadline()
    {
        var expiry=DateTimeOffset.UtcNow.AddSeconds(20);
        var result=Adapter(Configured()).IssueToken(Scope(),new(Guid.NewGuid(),false,1,expiry));
        var jwt=new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        Assert.True(jwt.ValidTo<=expiry.UtcDateTime);Assert.True(result.ExpiresAt<=expiry);
        Assert.Throws<SalesRoomMediaException>(()=>Adapter(Configured()).IssueToken(Scope(),new(Guid.NewGuid(),false,1,DateTimeOffset.UtcNow.AddSeconds(-1))));
    }
    [Fact]
    public void Empty_scope_never_issues_credentials() => Assert.Throws<ArgumentException>(() =>
        Adapter(Configured()).IssueToken(new(Guid.Empty, Guid.NewGuid()), new(Guid.NewGuid(), false)));

    [Fact]
    public async Task Retry_reuses_matching_provider_room_without_create()
    {
        var scope = Scope(); var operation = Guid.NewGuid(); var requests = new List<string>();
        using var handler = new Handler(request =>
        {
            requests.Add(request.RequestUri!.AbsolutePath);
            return Json(new { rooms = new[] { new { name = LiveKitSalesRoomMediaTransport.RoomName(scope), metadata = operation.ToString("N"), maxParticipants = 7 } } });
        });
        var result = await Adapter(Configured(), handler).EnsureRoomAsync(scope, operation, 7, default);
        Assert.Equal(operation.ToString("N"), result.OperationId);
        Assert.All(requests, path => Assert.EndsWith("ListRooms", path));
    }
    [Fact]
    public async Task Existing_room_with_different_operation_requires_reconciliation()
    {
        var scope = Scope(); using var handler = new Handler(_ => Json(new { rooms = new[] {
            new { name = LiveKitSalesRoomMediaTransport.RoomName(scope), metadata = "other", maxParticipants = 7 } } }));
        var error = await Assert.ThrowsAsync<SalesRoomMediaException>(() => Adapter(Configured(), handler).EnsureRoomAsync(scope, Guid.NewGuid(), 7, default));
        Assert.Equal("room_binding_conflict", error.Code); Assert.True(error.RequiresReconciliation);
    }
    [Fact]
    public async Task Provider_write_failure_is_ambiguous_and_does_not_expose_payload()
    {
        using var handler = new Handler(_ => new(HttpStatusCode.InternalServerError) { Content = new StringContent("private-provider-payload") });
        var error = await Assert.ThrowsAsync<SalesRoomMediaException>(() => Adapter(Configured(), handler).RemoveParticipantAsync(Scope(), new(Guid.NewGuid(), false), default));
        Assert.True(error.RequiresReconciliation); Assert.DoesNotContain("private-provider-payload", error.ToString());
    }
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
        { Content = new ByteArrayContent(Livekit.Server.Sdk.Dotnet.ListRoomsResponse.Parser.ParseJson(JsonSerializer.Serialize(value)).ToByteArray()) };
    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(handler, false); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request)); }
}

public sealed class SalesRoomAudioOutputTests
{
    [Fact]
    public async Task Takeover_cancels_inflight_write_flushes_queue_and_rejects_late_frames()
    {
        var sink = new Sink { Block = true };
        await using var output = new SalesRoomAudioOutput(sink);
        var write = output.SendAsync(1, 24000, new short[480], default);
        await sink.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(await output.SendAsync(1, 24000, new short[480], default));
        var generation = await output.CancelAsync();
        Assert.False(await write); Assert.Equal(2, generation); Assert.True(sink.Clears > 0);
        Assert.False(await output.SendAsync(1, 24000, new short[480], default));
        sink.Block = false; Assert.True(await output.SendAsync(generation, 24000, new short[480], default));
    }
    [Fact]
    public async Task Invalid_frames_and_disposed_output_never_reach_provider()
    {
        var sink = new Sink(); var output = new SalesRoomAudioOutput(sink);
        await Assert.ThrowsAsync<SalesRoomMediaException>(() => output.SendAsync(1, 44100, new short[882], default));
        await Assert.ThrowsAsync<SalesRoomMediaException>(() => output.SendAsync(1, 24000, new short[96000], default));
        await output.DisposeAsync(); Assert.False(await output.SendAsync(1, 24000, new short[480], default));
        Assert.Equal(0, sink.Writes); Assert.True(sink.Disposed);
    }
    [Fact]
    public async Task Completion_drains_current_audio_but_never_an_abandoned_turn()
    {
        var sink = new Sink(); await using var output = new SalesRoomAudioOutput(sink);
        Assert.True(await output.CompleteAsync(1, default));
        await output.CancelAsync();
        Assert.False(await output.CompleteAsync(1, default));
        Assert.Equal(1, sink.Completions);
    }
    private sealed class Sink : ISalesRoomAudioSink
    {
        public bool Block, Disposed; public int Clears, Writes, Completions;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task WriteAsync(int rate, ReadOnlyMemory<short> samples, CancellationToken ct)
        { Writes++; Entered.TrySetResult(); if (Block) await Task.Delay(Timeout.Infinite, ct); }
        public Task CompleteAsync(CancellationToken ct) { Completions++; return Task.CompletedTask; }
        public void Clear() => Clears++;
        public void Dispose() => Disposed = true;
    }
}
