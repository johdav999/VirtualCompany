using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class SalesNarrationTests
{
    [Fact]
    public async Task Preparation_and_generation_are_idempotent_and_preview_reuses_audio()
    {
        await using var f = await Fixture.Create();
        var first = await f.Prepare();
        var again = await f.Prepare();
        Assert.Equal(first.Id, again.Id);
        Assert.DoesNotContain("PRIVATE", System.Text.Json.JsonSerializer.Serialize(first));
        await f.Approve(first);
        await f.Generate();
        var ready = await f.Current();
        Assert.Equal("ready", ready.Status);
        Assert.Equal(2, f.Speech.Calls);
        await f.Generate();
        Assert.Equal(2, f.Speech.Calls);
        var preview = await f.Service.PreviewAsync(f.Company, f.Actor, f.Request(ready), default);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(preview.Audio, 0, 4));
        Assert.Equal(2, f.Speech.Calls);
        Assert.True((await f.Current()).ReusedMinutes > 0);
    }

    [Fact]
    public async Task One_changed_script_only_invalidates_its_segment_and_language_isolated()
    {
        await using var f = await Fixture.Create();
        var original = await f.Prepare(); await f.Approve(original); await f.Generate();
        var changed = await f.Service.PrepareAsync(f.Company, f.Actor, f.Session,
            new("en", [new(1, 1, "Updated approved introduction."), new(2, 1, "Customer goals.")]), default);
        Assert.Equal(1, changed.Segments.Count(x => x.Reused));
        await f.Approve(changed); await f.Generate();
        Assert.Equal(3, f.Speech.Calls);
        var swedish = await f.Service.PrepareAsync(f.Company, f.Actor, f.Session, new("sv"), default);
        Assert.All(swedish.Segments, x => Assert.False(x.Reused));
    }

    [Fact]
    public async Task Wrong_company_actor_audience_unapproved_and_revoked_are_denied()
    {
        await using var f = await Fixture.Create();
        var draft = await f.Prepare();
        await Assert.ThrowsAsync<SalesNarrationException>(() => f.Service.PreviewAsync(f.Company, f.Actor, f.Request(draft), default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.GetAsync(Guid.NewGuid(), f.Actor, f.Session, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.GetAsync(f.Company, f.OtherActor, f.Session, default));
        await f.Approve(draft); await f.Generate();
        var ready = await f.Current();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.PreviewAsync(f.Company, f.Actor, f.Request(ready) with { AudienceId = Guid.NewGuid() }, default));
        await f.Service.DecideAsync(f.Company, f.Actor, ready.Id, "revoke", new(ready.Version), default);
        await Assert.ThrowsAsync<SalesNarrationException>(() => f.Service.PreviewAsync(f.Company, f.Actor, f.Request(ready), default));
    }

    [Fact]
    public async Task Rejected_audio_is_not_ready_and_retry_keeps_usage()
    {
        await using var f = await Fixture.Create();
        var draft = await f.Prepare(); await f.Approve(draft);
        f.Speech.Match = false; await f.Generate();
        var rejected = await f.Current();
        Assert.Equal("not_ready", rejected.Status);
        Assert.All(rejected.Segments, x => Assert.Equal("rejected", x.Status));
        Assert.Empty(f.Storage.Content);
        Assert.Equal(20, rejected.InputTokens);
        await Assert.ThrowsAsync<SalesNarrationException>(() => f.Service.DecideAsync(f.Company, f.Actor, rejected.Id, "retry", new(rejected.Version), default));
        await f.Service.DecideAsync(f.Company, f.Actor, rejected.Id, "retry", new(rejected.Version, true), default);
        f.Speech.Match = true; await f.Generate();
        Assert.Equal(40, (await f.Current()).InputTokens);
        Assert.Equal(4, await f.Db.SalesNarrationAttempts.CountAsync());
    }

    [Fact]
    public async Task Unknown_provider_outcomes_do_not_automatically_retry_or_lose_reservations()
    {
        await using var f = await Fixture.Create();
        var draft = await f.Prepare(); await f.Approve(draft);
        f.Speech.Fail = true; await f.Generate(); await f.Generate();
        Assert.Equal(2, f.Speech.Calls);
        Assert.Equal(2, (await f.Current()).UnresolvedAttempts);
        Assert.All(await f.Db.SalesNarrationAttempts.ToListAsync(), x => Assert.True(x.EstimatedCostUsd > 0));
    }

    [Fact]
    public async Task Daily_budget_blocks_before_provider_and_missing_objects_invalidate_preview()
    {
        await using var f = await Fixture.Create();
        var draft = await f.Prepare(); await f.Approve(draft);
        f.Options.MaximumCompanyDailyUsd = 0.000001m;
        await f.Generate(); Assert.Equal(0, f.Speech.Calls);
        f.Options.MaximumCompanyDailyUsd = 5;
        await f.Generate();
        f.Storage.Content.Clear();
        Assert.Equal("not_ready", (await f.Current()).Status);
        await Assert.ThrowsAsync<SalesNarrationException>(() => f.Service.PreviewAsync(f.Company, f.Actor, f.Request(draft), default));
        Assert.Equal("not_ready", (await f.Current()).Status);
    }

    [Fact]
    public async Task Revocation_during_generation_never_publishes_and_stale_command_is_rejected()
    {
        await using var f = await Fixture.Create();
        var draft = await f.Prepare(); await f.Approve(draft);
        f.Speech.BeforeReturn = async () => {
            var revision = await f.Db.SalesNarrationRevisions.SingleAsync();
            revision.RevokedUtc = f.Clock.Now; revision.Version++; await f.Db.SaveChangesAsync();
        };
        await f.Generate();
        Assert.Empty(f.Storage.Content);
        await Assert.ThrowsAsync<SalesNarrationException>(() => f.Service.DecideAsync(f.Company, f.Actor, draft.Id, "approve", new(1), default));
    }

    [Fact]
    public async Task Expired_unreferenced_audio_is_deleted_but_active_references_are_retained()
    {
        await using var f = await Fixture.Create();
        var draft = await f.Prepare(); await f.Approve(draft); await f.Generate();
        var assets = await f.Db.SalesNarrationAssets.ToListAsync();
        foreach (var asset in assets) asset.UpdatedUtc = f.Clock.Now.AddDays(-100);
        await f.Db.SaveChangesAsync();
        await f.Worker.RunAsync(default); Assert.Equal(2, f.Storage.Content.Count);
        f.Clock.Now = f.Clock.Now.AddDays(100);
        await f.Worker.RunAsync(default); Assert.Empty(f.Storage.Content);
        Assert.All(await f.Db.SalesNarrationAssets.ToListAsync(), x => Assert.Equal("deleted", x.Status));
    }

    [Fact]
    public void Cache_identity_includes_company_source_script_language_voice_configuration_and_audience()
    {
        var company = Guid.NewGuid();
        string Key(Guid c, string source = "s", string script = "t", string language = "en", string voice = "marin", string config = "v1", string audience = "a") =>
            SalesNarrationService.AssetKey(c, source, script, language, voice, config, audience);
        var key = Key(company);
        Assert.All(new[] { Key(Guid.NewGuid()), Key(company, source:"new"), Key(company, script:"new"),
            Key(company, language:"sv"), Key(company, voice:"cedar"), Key(company, config:"v2"), Key(company, audience:"other") }, x => Assert.NotEqual(key, x));
    }


    [Fact]
    public async Task Concurrent_worker_observes_the_committed_claim_without_another_provider_request()
    {
        await using var f = await Fixture.Create();
        var draft = await f.Prepare(); await f.Approve(draft);
        var id = await f.Db.SalesNarrationAssets.Select(x => x.Id).FirstAsync();
        f.Speech.BeforeReturn = async () =>
        {
            var context = new NarrationContext(f.Company, f.Actor);
            await using var other = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                .UseSqlite(f.Db.Database.GetDbConnection()).Options, context);
            var service = new SalesNarrationService(other, f.Speech, f.Storage, f.Clock, Options.Create(f.Options));
            var worker = new SalesNarrationWorker(other, context, service, f.Speech, f.Storage, Options.Create(f.Options), f.Clock);
            await worker.ProcessAsync(f.Company, id, default);
        };
        await f.Worker.ProcessAsync(f.Company, id, default);
        Assert.Equal(1, f.Speech.Calls);
        Assert.Single(await f.Db.SalesNarrationAttempts.ToListAsync());
    }

    [Fact]
    public async Task Source_revision_invalidates_only_the_affected_slide()
    {
        await using var f = await Fixture.Create();
        var draft = await f.Prepare(); await f.Approve(draft); await f.Generate();
        var slide = await f.Db.SalesPresentationSlides.SingleAsync(x => x.SlideNumber == 1);
        f.Db.Entry(slide).Property(x => x.ExtractedText).CurrentValue = "Updated source slide.";
        await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<SalesNarrationException>(() => f.Service.PreviewAsync(f.Company, f.Actor, f.Request(draft), default));
        var next = await f.Prepare();
        Assert.Equal(1, next.Segments.Count(x => x.Reused));
    }

    [Fact]
    public async Task Missing_generation_configuration_is_explicit_before_approval()
    {
        await using var f = await Fixture.Create();
        var draft = await f.Prepare();
        f.Options.Enabled = false;
        Assert.False((await f.Service.GetAsync(f.Company,f.Actor,f.Session,default)).Speech.Available);
        await Assert.ThrowsAsync<SalesNarrationException>(() => f.Approve(draft));
        Assert.Equal(0, f.Speech.Calls);
    }

    internal sealed class Fixture : IAsyncDisposable
    {
        private RoomFixture room = null!;
        public VirtualCompanyDbContext Db = null!;
        public SalesNarrationService Service = null!;
        public SalesNarrationWorker Worker = null!;
        public FakeSpeech Speech = new();
        public MemoryStorage Storage = new();
        public SalesNarrationOptions Options = new() { Enabled = true, InputUsdPerMillion = 10, OutputUsdPerMillion = 20, RateVersion = "synthetic-test-only" };
        public Guid Company => room.Company;
        public Guid Actor => room.Actor;
        public Guid OtherActor => room.OtherActor;
        public Guid Session => room.Meeting;
        public RoomClock Clock => room.Clock;
        public async Task<SalesNarrationRevisionDto> Prepare() => await Service.PrepareAsync(Company, Actor, Session, new("en"), default);
        public async Task Approve(SalesNarrationRevisionDto r) => await Service.DecideAsync(Company, Actor, r.Id, "approve", new(r.Version), default);
        public async Task<SalesNarrationRevisionDto> Current() => (await Service.GetAsync(Company, Actor, Session, default)).Revisions[0];
        public SalesNarrationPlaybackRequest Request(SalesNarrationRevisionDto r) => new(r.Id, r.Segments[0].Id, Session, r.AudienceId, 0, 1);
        public async Task Generate()
        {
            var ids = await Db.SalesNarrationAssets.Select(x => x.Id).ToListAsync();
            foreach (var id in ids) await Worker.ProcessAsync(Company, id, default);
        }
        public static async Task<Fixture> Create()
        {
            var f = new Fixture { room = await RoomFixture.Create() };
            var context = new NarrationContext(f.Company, f.Actor);
            f.Db = new(new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(f.room.Db.Database.GetDbConnection()).Options, context);
            var agent = new Agent(Guid.NewGuid(), f.Company, "alex-sales", "Alex", "Sales representative", "Sales", null, AgentSeniority.Senior, AgentStatus.Active);
            f.Db.Agents.Add(agent);
            var deck = new SalesPresentationDeck(Guid.NewGuid(), f.Company, f.Session, agent.Id, 1, "Synthetic deck", "deck.pptx",
                "application/vnd.openxmlformats-officedocument.presentationml.presentation", 100, new string('a',64), "safe/deck", null, f.Actor, f.Clock.Now);
            deck.BeginProcessing(f.Clock.Now, TimeSpan.FromMinutes(10)); deck.MarkProcessed(2,"test","1","static",1,f.Clock.Now); deck.Activate(f.Clock.Now);
            f.Db.SalesPresentationDecks.Add(deck);
            for (var i = 1; i <= 2; i++) f.Db.SalesPresentationSlides.Add(new(Guid.NewGuid(), f.Company, deck.Id, 1, i,
                "Slide", i == 1 ? "Welcome to the presentation." : "Customer goals.", "PRIVATE NOTES", $"safe/{i}", null,
                1600,900,100,100,new string((char)('a'+i),64),"Objective",60,"Transition",f.Clock.Now));
            await f.Db.SaveChangesAsync();
            f.Service = new(f.Db, f.Speech, f.Storage, f.Clock, Microsoft.Extensions.Options.Options.Create(f.Options));
            f.Worker = new(f.Db, context, f.Service, f.Speech, f.Storage, Microsoft.Extensions.Options.Options.Create(f.Options), f.Clock);
            return f;
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await room.DisposeAsync(); }
    }

    internal sealed class FakeSpeech : IApprovedSpeechGateway
    {
        public int Calls; public bool Match = true, Fail; public Func<Task>? BeforeReturn;
        public Task<ApprovedSpeechProfile> GetProfileAsync(CancellationToken ct) => Task.FromResult(new ApprovedSpeechProfile(true,"model","marin","config1"));
        public async Task<ApprovedSpeechResult> GenerateAsync(ApprovedSpeechRequest r, CancellationToken ct)
        {
            Calls++; if (BeforeReturn is not null) await BeforeReturn();
            if (Fail) throw new IOException("Synthetic ambiguous outcome");
            return new(new byte[4800], r.Text, "model", "response-"+Calls, 10,20,"{\"input_tokens\":10,\"output_tokens\":20}",Match);
        }
    }
    internal sealed class MemoryStorage : ICompanyDocumentStorage
    {
        public Dictionary<string,byte[]> Content = new();
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream(Content[key], false));
        public async Task<DocumentStorageWriteResult> WriteAsync(DocumentStorageWriteRequest r, CancellationToken ct)
        { using var m = new MemoryStream(); await r.Content.CopyToAsync(m,ct); Content[r.StorageKey] = m.ToArray(); return new(r.StorageKey,null); }
        public Task DeleteAsync(string key, CancellationToken ct) { Content.Remove(key); return Task.CompletedTask; }
    }
    internal sealed class NarrationContext(Guid company, Guid actor) : ICompanyContextAccessor
    {
        public Guid? CompanyId { get; private set; } = company;
        public Guid? UserId { get; private set; } = actor;
        public bool IsResolved => CompanyId.HasValue;
        public ResolvedCompanyMembershipContext? Membership { get; private set; }
        public void SetCompanyId(Guid? value) => CompanyId = value;
        public void SetCompanyContext(ResolvedCompanyMembershipContext? value) { Membership=value; CompanyId=value?.CompanyId; UserId=value?.UserId; }
    }
}




