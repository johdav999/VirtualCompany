using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class SalesMeetingPresentationConductorTests
{
    [Fact]
    public void Route_neutral_options_inherit_explicit_teams_values_when_the_new_section_is_absent()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{TeamsPresenterOptions.SectionName}:StageRenderTimeoutMilliseconds"] = "1700",
            [$"{TeamsPresenterOptions.SectionName}:MaximumConsecutiveSlideTransitions"] = "6",
            [$"{TeamsPresenterOptions.SectionName}:MinimumSlideDwellSeconds"] = "4"
        }).Build();
        using var provider = new ServiceCollection().AddSalesInfrastructure(configuration).BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<SalesPresentationConductorOptions>>().Value;

        Assert.Equal(1700, options.RenderTimeoutMilliseconds);
        Assert.Equal(6, options.MaximumConsecutiveSlideTransitions);
        Assert.Equal(4, options.MinimumSlideDwellSeconds);
    }

    [Fact]
    public async Task Autonomous_transition_waits_for_exact_authoritative_render_before_narration()
    {
        var ids = new Ids();
        var runtime = new Runtime(ids, SalesPresentationControlModes.Autonomous);
        var stage = new SalesPresentationStagePresenceService();
        var preemption = new SalesPresentationNarrationPreemption();
        var conductor = new SalesMeetingPresentationConductor(runtime, stage, preemption,
            Options.Create(new SalesPresentationConductorOptions { RenderTimeoutMilliseconds = 500,
                MaximumConsecutiveSlideTransitions = 8, MinimumSlideDwellSeconds = 0 }));
        await stage.RegisterAsync(new SalesPresentationStagePresence(ids.Company, ids.Session, ids.Deck, 3,
            "connection", true, DateTime.UtcNow), default);
        await stage.AcknowledgeAsync(new SalesPresentationRenderAcknowledgement(ids.Company, ids.Session, ids.Deck,
            3, 2, 1, 2, "connection", DateTime.UtcNow), default);

        var plan = await conductor.PrepareAsync(new SalesPresentationNarrationRequest(ids.Company, ids.User,
            ids.Session, SalesPresentationToolNames.Next, null, null, null, null, null, ids.Agent), default);

        Assert.True(plan!.MayNarrate);
        Assert.Equal(2, plan.Snapshot.Stage.SlideNumber);
        Assert.Equal(2, plan.Render.Acknowledgement!.PresentationVersion);
        Assert.Equal(SalesPresentationCommandActorTypes.Agent, runtime.LastRequest!.ActorType);
        Assert.Equal(ids.Deck, runtime.LastRequest.DeckId);
        Assert.Equal(ids.Agent, runtime.LastRequest.ActorId);
    }

    [Fact]
    public async Task Manual_and_assisted_modes_never_mutate_and_disconnected_stage_fails_closed()
    {
        var ids = new Ids();
        var stage = new SalesPresentationStagePresenceService();
        var preemption = new SalesPresentationNarrationPreemption();
        foreach (var (mode, disposition) in new[] { (SalesPresentationControlModes.Manual, "blocked"), (SalesPresentationControlModes.Assisted, "recommended") })
        {
            var runtime = new Runtime(ids, mode);
            var conductor = new SalesMeetingPresentationConductor(runtime, stage, preemption,
                Options.Create(new SalesPresentationConductorOptions { RenderTimeoutMilliseconds = 250,
                    MaximumConsecutiveSlideTransitions = 8, MinimumSlideDwellSeconds = 0 }));
            var plan = await conductor.PrepareAsync(new SalesPresentationNarrationRequest(ids.Company, ids.User,
                ids.Session, SalesPresentationToolNames.Next, null, null, null, null, null), default);
            Assert.Equal(disposition, plan!.Disposition);
            Assert.Null(runtime.LastRequest);
            Assert.False(plan.MayNarrate);
        }
    }

    [Fact]
    public void Human_preemption_cancels_narration_bound_to_an_older_version_once()
    {
        using var preemption = new SalesPresentationNarrationPreemption();
        var company = Guid.NewGuid(); var session = Guid.NewGuid();
        var token = preemption.Bind(company, session, 7);
        preemption.Preempt(company, session, 8);
        preemption.Preempt(company, session, 9);
        Assert.True(token.IsCancellationRequested);
    }

    [Fact]
    public async Task Render_ack_requires_the_exact_active_connection_and_is_idempotent()
    {
        var ids = new Ids();
        var stage = new SalesPresentationStagePresenceService();
        var acknowledgement = new SalesPresentationRenderAcknowledgement(ids.Company, ids.Session, ids.Deck,
            3, 1, 0, 1, "active-stage", DateTime.UtcNow);

        await Assert.ThrowsAsync<InvalidOperationException>(() => stage.AcknowledgeAsync(acknowledgement, default));
        await stage.RegisterAsync(new SalesPresentationStagePresence(ids.Company, ids.Session, ids.Deck, 3,
            "active-stage", true, DateTime.UtcNow), default);
        await stage.AcknowledgeAsync(acknowledgement, default);
        await stage.AcknowledgeAsync(acknowledgement with { RenderedUtc = acknowledgement.RenderedUtc.AddSeconds(1) }, default);

        var expected = new SalesPresentationStageSnapshotDto(ids.Session, "presenting", 0, 1,
            ids.Deck, 3, 1, 3, "Slide 1", "Visible", null, 1600, 900);
        var rendered = await stage.WaitForRenderAsync(expected, TimeSpan.FromMilliseconds(50), default);
        Assert.True(rendered.Acknowledged);
        Assert.Equal(acknowledgement.RenderedUtc, rendered.Acknowledgement!.RenderedUtc);
    }

    private sealed class Runtime(Ids ids, string mode) : ISalesPresentationRuntimeService
    {
        public SalesPresentationCommandRequest? LastRequest { get; private set; }
        public Task<SalesPresentationAuthoritativeSnapshotDto?> GetCurrentAsync(Guid companyId, Guid userId, Guid sessionId, CancellationToken cancellationToken) =>
            Task.FromResult<SalesPresentationAuthoritativeSnapshotDto?>(Snapshot(1, 0, 1));
        public Task<IReadOnlyList<SalesPresentationSearchResultDto>> SearchAsync(Guid companyId, Guid userId, Guid sessionId, string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SalesPresentationSearchResultDto>>([]);
        public Task<SalesPresentationCommandResultDto?> ExecuteAsync(Guid companyId, Guid userId, Guid sessionId, string toolName, SalesPresentationCommandRequest request, string? correlationId, CancellationToken cancellationToken)
        { LastRequest = request; return Task.FromResult<SalesPresentationCommandResultDto?>(new("accepted", null, Snapshot(2, 1, 2))); }
        public Task RecordReconnectAsync(Guid companyId, Guid userId, Guid sessionId, string surface, CancellationToken cancellationToken) => Task.CompletedTask;
        private SalesPresentationAuthoritativeSnapshotDto Snapshot(int slide, long sequence, long version)
        {
            var stage = new SalesPresentationStageSnapshotDto(ids.Session, "presenting", sequence, version,
                ids.Deck, 3, slide, 3, $"Slide {slide}", "Visible text", null, 1600, 900);
            return new(stage, new SalesPresentationPrivateSnapshotDto(stage, mode, 0, null, null,
                "Objective", 60, "Transition", []));
        }
    }

    private sealed class Ids
    { public Guid Company { get; } = Guid.NewGuid(); public Guid User { get; } = Guid.NewGuid(); public Guid Agent { get; } = Guid.NewGuid(); public Guid Session { get; } = Guid.NewGuid(); public Guid Deck { get; } = Guid.NewGuid(); }
}
