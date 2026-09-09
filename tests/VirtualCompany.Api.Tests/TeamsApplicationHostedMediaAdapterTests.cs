using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class TeamsApplicationHostedMediaAdapterTests
{
    [Fact]
    public async Task Pcm_pipeline_validates_frames_and_applies_bounded_backpressure()
    {
        var services = new ServiceCollection();
        services.AddScoped<ITeamsMeetingMediaBindingAuthorizer, AllowBinding>();
        await using var provider = services.BuildServiceProvider();
        var platform = new FakePlatform();
        var options = Options.Create(ReadyOptions());
        var adapter = new TeamsApplicationHostedMediaAdapter(platform, provider.GetRequiredService<IServiceScopeFactory>(), options);
        var binding = new TeamsMeetingMediaBinding(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), 2, "host-1", "provider-call");
        var session = await adapter.AttachAsync(new TeamsMeetingMediaAttachRequest(binding,
            TeamsAudioFormat.Pcm16KMono20Ms, 5, TimeSpan.FromMinutes(1)), default);

        for (var sequence = 1; sequence <= 5; sequence++)
        {
            var accepted = await adapter.ReceiveProviderAudioAsync(new TeamsAudioFrame(session.Id, sequence,
                DateTime.UtcNow, TeamsAudioFormat.Pcm16KMono20Ms, new byte[640], false), default);
            Assert.True(accepted.Accepted);

            if (sequence == 1)
            {
                var duplicate = await adapter.ReceiveProviderAudioAsync(new TeamsAudioFrame(session.Id, sequence,
                    DateTime.UtcNow, TeamsAudioFormat.Pcm16KMono20Ms, new byte[640], false), default);
                Assert.False(duplicate.Accepted);
                Assert.False(duplicate.Backpressured);
                Assert.Equal(TeamsMeetingMediaProblemCodes.ReorderedFrame, duplicate.ReasonCode);
            }
        }
        var dropped = await adapter.ReceiveProviderAudioAsync(new TeamsAudioFrame(session.Id, 6,
            DateTime.UtcNow, TeamsAudioFormat.Pcm16KMono20Ms, new byte[640], false), default);
        Assert.True(dropped.Backpressured);
        Assert.Equal(TeamsMeetingMediaProblemCodes.Backpressure, dropped.ReasonCode);

        var malformed = await Assert.ThrowsAsync<TeamsMeetingMediaException>(() => adapter.SendAudioAsync(
            new TeamsAudioFrame(session.Id, 1, DateTime.UtcNow, TeamsAudioFormat.Pcm16KMono20Ms, new byte[20], false), default));
        Assert.Equal(TeamsMeetingMediaProblemCodes.InvalidFrame, malformed.Code);
        Assert.Empty(platform.Sent);
    }

    [Fact]
    public async Task Unsupported_or_unapproved_host_fails_closed_with_typed_fallback()
    {
        var services = new ServiceCollection();
        services.AddScoped<ITeamsMeetingMediaBindingAuthorizer, AllowBinding>();
        await using var provider = services.BuildServiceProvider();
        var options = ReadyOptions(); options.MediaRouteApproved = false;
        var adapter = new TeamsApplicationHostedMediaAdapter(new FakePlatform(),
            provider.GetRequiredService<IServiceScopeFactory>(), Options.Create(options));

        var health = await adapter.GetHealthAsync(default);

        Assert.False(health.Available);
        Assert.Equal("media_route_not_approved", health.ReasonCode);
        Assert.Contains("typed", health.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Pcm_bridge_resamples_and_packetizes_exact_twenty_millisecond_frames()
    {
        var teamsFrame = new byte[TeamsAudioFormat.Pcm16KMono20Ms.ExpectedFrameBytes];
        for (var index = 0; index < teamsFrame.Length; index += 2)
            BitConverter.TryWriteBytes(teamsFrame.AsSpan(index, 2), (short)(index - 320));

        var providerFrame = Pcm16SampleRateConverter.From16KhzTo24Khz(teamsFrame);
        var packetizer = new TeamsPcmPacketizer();
        var first = packetizer.Append24Khz(providerFrame.AsSpan(0, providerFrame.Length / 2));
        var second = packetizer.Append24Khz(providerFrame.AsSpan(providerFrame.Length / 2));

        Assert.Equal(960, providerFrame.Length);
        Assert.Empty(first);
        Assert.Single(second);
        Assert.Equal(640, second[0].Length);
    }

    private static TeamsPresenterOptions ReadyOptions() => new()
    {
        Enabled = true, AudioEnabled = true, CallControlEnabled = true,
        MediaRouteApproved = true, MediaRoute = "teams_application_hosted",
        BotApplicationId = Guid.NewGuid().ToString("D"), CallControlHostId = "host-1",
        MediaHostPublicIp = "203.0.113.10", MediaHostServiceFqdn = "media.example.test",
        MediaCertificateThumbprint = "00112233445566778899AABBCCDDEEFF00112233"
    };

    private sealed class AllowBinding : ITeamsMeetingMediaBindingAuthorizer
    { public Task AuthorizeAsync(TeamsMeetingMediaBinding binding, CancellationToken cancellationToken) => Task.CompletedTask; }

    private sealed class FakePlatform : ITeamsAudioSocketPlatform
    {
        public string SdkVersion => "test"; public bool IsInitialized => true;
        public bool HasPreparedCall(Guid localCallId) => true;
        public List<byte[]> Sent { get; } = [];
        public void SetIngress(Func<Guid, long, DateTime, byte[], bool, string?, Task> ingress) { }
        public Task<TeamsCallMediaPreparation> PrepareAsync(Guid localCallId, string mediaHostInstanceId, CancellationToken cancellationToken) =>
            Task.FromResult(new TeamsCallMediaPreparation("#microsoft.graph.appHostedMediaConfig", "{}"));
        public Task BindProviderCallAsync(Guid localCallId, string providerCallId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SendAsync(Guid localCallId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken) { Sent.Add(data.ToArray()); return Task.CompletedTask; }
        public Task CancelAsync(Guid localCallId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ReleaseAsync(Guid localCallId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
