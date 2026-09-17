using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Sales;
using VirtualCompany.Persistence.Migrations.Persistence.Migrations;

namespace VirtualCompany.Api.Tests;

public sealed class SalesPresentationPresetTests
{
    [Fact]
    public void Published_version_is_immutable_and_archived_preset_rejects_publication()
    {
        var now = DateTime.UtcNow; var company = Guid.NewGuid(); var preset = new SalesPresentationPreset(Guid.NewGuid(), company, "Demo", null, Guid.NewGuid(), now);
        var version = Version(company, preset.Id, 1, now); version.Publish(Guid.NewGuid(), now.AddMinutes(1));
        Assert.Throws<InvalidOperationException>(() => version.Update(null, null, "Changed", "CFO", 30, null, "manual", "en", true, false, false, null, now.AddMinutes(2)));
        preset.Archive(now.AddMinutes(2));
        Assert.Throws<InvalidOperationException>(() => preset.Publish(version.Id, now.AddMinutes(3)));
    }

    [Fact]
    public void Asset_supports_bounded_retry_and_stale_claim_recovery()
    {
        var now = DateTime.UtcNow; var asset = new SalesPresentationPresetAsset(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "deck.pptx", "application/octet-stream", 100, new string('a', 64), "safe/deck.pptx", null, Guid.NewGuid(), now);
        asset.BeginProcessing(now, TimeSpan.FromMinutes(5));
        Assert.Throws<InvalidOperationException>(() => asset.BeginProcessing(now.AddMinutes(1), TimeSpan.FromMinutes(5)));
        asset.BeginProcessing(now.AddMinutes(6), TimeSpan.FromMinutes(5));
        asset.MarkFailed("renderer_unavailable", "Rendering is temporarily unavailable.", true, false, now.AddMinutes(7));
        asset.QueueRetry(now.AddMinutes(8));
        Assert.Equal(SalesPresentationPresetAssetStatus.PendingScan, asset.Status);
        Assert.Equal(2, asset.ProcessingAttemptCount);
    }

    [Fact]
    public async Task Service_creates_company_scoped_draft_audits_and_rejects_stale_update()
    {
        await using var f = await Fixture.CreateAsync();
        var created = await f.Service.CreateAsync(f.CompanyId, f.UserId, Command(f.UserId, f.AgentId), "corr", default);
        Assert.Equal("draft", created.Lifecycle); Assert.Equal(1, created.CurrentDraft!.VersionNumber);
        Assert.Equal(1, await f.Db.AuditEvents.CountAsync(x => x.Action == "sales.presentation_preset.created"));
        await Assert.ThrowsAsync<SalesPresentationPresetConflictException>(() => f.Service.UpdateDraftAsync(f.CompanyId, f.UserId,
            created.Id, new(99, created.CurrentDraft.ConcurrencyVersion, created.Name, created.Description, f.UserId, f.AgentId,
                "Goal", "Audience", 30, null, "manual", "en", ["sales_meeting"], null, null), null, default));
        Assert.Null(await f.Service.GetAsync(f.CompanyId, f.UserId, Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Ready_draft_publishes_and_new_edit_creates_next_version()
    {
        await using var f = await Fixture.CreateAsync(); var created = await f.Service.CreateAsync(f.CompanyId, f.UserId, Command(f.UserId, f.AgentId), null, default);
        var draft = await f.Db.SalesPresentationPresetVersions.SingleAsync(); var asset = new SalesPresentationPresetAsset(Guid.NewGuid(), f.CompanyId, draft.Id, "deck.pptx", "application/octet-stream", 100, new string('a', 64), "safe/deck.pptx", null, f.UserId, DateTime.UtcNow);
        asset.BeginProcessing(DateTime.UtcNow, TimeSpan.FromMinutes(5)); asset.MarkProcessed(1, "renderer", "1", "flattened", DateTime.UtcNow); f.Db.Add(asset); await f.Db.SaveChangesAsync();
        var published = await f.Service.PublishAsync(f.CompanyId, f.UserId, created.Id, draft.Id, created.ConcurrencyVersion, draft.ConcurrencyVersion, null, default);
        Assert.Equal("published", published!.CurrentPublished!.Lifecycle);
        var next = await f.Service.CreateNextDraftAsync(f.CompanyId, f.UserId, created.Id, published.ConcurrencyVersion, null, default);
        Assert.Equal(2, next!.CurrentDraft!.VersionNumber); Assert.Equal(1, next.CurrentPublished!.VersionNumber);
        var updated = await f.Service.UpdateDraftAsync(f.CompanyId, f.UserId, created.Id,
            new(next.ConcurrencyVersion, next.CurrentDraft.ConcurrencyVersion, next.Name, next.Description, f.UserId, f.AgentId,
                "Updated draft goal", "Updated audience", 45, "New demo", "manual", "en", ["sales_meeting"], null, null), null, default);
        var reloaded = await f.Service.GetAsync(f.CompanyId, f.UserId, created.Id, default);
        Assert.Equal("Updated draft goal", reloaded!.CurrentDraft!.Goal);
        Assert.Equal("Updated audience", reloaded.CurrentDraft.Audience);
        Assert.Equal(45, reloaded.CurrentDraft.DurationMinutes);
        Assert.Equal("New demo", reloaded.CurrentDraft.DemoScenario);
        Assert.Equal(published.CurrentPublished.Goal, reloaded.CurrentPublished!.Goal);
        Assert.Equal(published.CurrentPublished.DurationMinutes, reloaded.CurrentPublished.DurationMinutes);
    }

    [Fact]
    public async Task Library_cover_returns_the_first_rendered_slide_without_public_storage_access()
    {
        await using var f=await Fixture.CreateAsync();
        var (preset,slide,asset)=await SeedCover(f);
        var storage=new SalesNarrationTests.MemoryStorage();
        var png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
        storage.Content["private/first.png"]=png;
        var service=new SalesPresentationPresetService(f.Db,storage,Options.Create(new SalesPresentationOptions()),TimeProvider.System);
        var list=await service.ListAsync(f.CompanyId,f.UserId,null,false,default);
        Assert.Equal(slide,Assert.Single(list).CoverSlideId);
        var cover=await service.GetCoverAsync(f.CompanyId,f.UserId,preset,slide,default);
        Assert.Equal("image/png",cover!.ContentType);
        Assert.Equal(png,cover.Content);
        Assert.Null(await service.GetCoverAsync(f.CompanyId,f.UserId,Guid.NewGuid(),slide,default));
        var second=await f.Db.SalesPresentationPresetSlides.SingleAsync(x=>x.SlideNumber==2);
        Assert.Null(await service.GetCoverAsync(f.CompanyId,f.UserId,preset,second.Id,default));
        var otherCompany=Guid.NewGuid();
        f.Db.Add(new Company(otherCompany,"Other"));
        f.Db.Add(new CompanyMembership(Guid.NewGuid(),otherCompany,f.UserId,CompanyMembershipRole.Owner,CompanyMembershipStatus.Active));
        await f.Db.SaveChangesAsync();
        Assert.Null(await service.GetCoverAsync(otherCompany,f.UserId,preset,slide,default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.GetCoverAsync(f.CompanyId,Guid.NewGuid(),preset,slide,default));
    }

    [Fact]
    public async Task Missing_or_non_raster_cover_returns_a_safe_fallback()
    {
        await using var f=await Fixture.CreateAsync();
        var (preset,slide,asset)=await SeedCover(f);
        var storage=new SalesNarrationTests.MemoryStorage();
        var service=new SalesPresentationPresetService(f.Db,storage,Options.Create(new SalesPresentationOptions()),TimeProvider.System);
        Assert.Null(await service.GetCoverAsync(f.CompanyId,f.UserId,preset,slide,default));
        storage.Content["private/first.png"]=System.Text.Encoding.UTF8.GetBytes("<svg onload='alert(1)'/>");
        Assert.Null(await service.GetCoverAsync(f.CompanyId,f.UserId,preset,slide,default));
        storage.Content["private/first.png"]=new byte[10_485_761];
        Assert.Null(await service.GetCoverAsync(f.CompanyId,f.UserId,preset,slide,default));
    }

    [Fact]
    public async Task Slide_images_include_later_slides_and_require_matching_preset_version_and_membership()
    {
        await using var f = await Fixture.CreateAsync();
        var (preset, _, asset) = await SeedCover(f);
        var version = (await f.Db.SalesPresentationPresetAssets.SingleAsync(x => x.Id == asset)).PresetVersionId;
        var second = await f.Db.SalesPresentationPresetSlides.SingleAsync(x => x.AssetId == asset && x.SlideNumber == 2);
        var storage = new SalesNarrationTests.MemoryStorage();
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
        storage.Content["private/second.png"] = png;
        var service = new SalesPresentationPresetService(f.Db, storage, Options.Create(new SalesPresentationOptions()), TimeProvider.System);
        var image = await service.GetSlideImageAsync(f.CompanyId, f.UserId, preset, version, second.Id, default);
        Assert.Equal(png, image!.Content);
        Assert.Null(await service.GetSlideImageAsync(f.CompanyId, f.UserId, preset, Guid.NewGuid(), second.Id, default));
        Assert.Null(await service.GetSlideImageAsync(f.CompanyId, f.UserId, Guid.NewGuid(), version, second.Id, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetSlideImageAsync(f.CompanyId, Guid.NewGuid(), preset, version, second.Id, default));
        Assert.Null(await service.GetCoverAsync(f.CompanyId, f.UserId, preset, second.Id, default));
    }

    private static async Task<(Guid Preset,Guid Slide,Guid Asset)> SeedCover(Fixture f)
    {
        var created=await f.Service.CreateAsync(f.CompanyId,f.UserId,Command(f.UserId,f.AgentId),null,default);
        var version=created.CurrentDraft!;
        var now=DateTime.UtcNow;
        var asset=new SalesPresentationPresetAsset(Guid.NewGuid(),f.CompanyId,version.Id,"demo.pptx","application/octet-stream",100,new string('a',64),"private/deck",null,f.UserId,now);
        asset.BeginProcessing(now,TimeSpan.FromMinutes(5));asset.MarkProcessed(2,"renderer","1","flattened",now);
        f.Db.Add(asset);
        var first=Guid.NewGuid();
        for(var i=1;i<=2;i++)
            f.Db.Add(new SalesPresentationPresetSlide(i==1?first:Guid.NewGuid(),f.CompanyId,asset.Id,1,i,"Title","Source",null,
                i==1?"private/first.png":"private/second.png",null,1600,900,100,100,new string('b',64),"Objective","Points",60,"Next",now));
        await f.Db.SaveChangesAsync();
        return(created.Id,first,asset.Id);
    }

    private static CreateSalesPresentationPresetCommand Command(Guid owner, Guid agent) => new("Standard demo", "Reusable", owner, agent,
        "Explain the product", "Finance leaders", 30, null, "assisted", "en", ["sales_meeting", "ad_hoc"], null, "{\"tone\":\"concise\"}");
    private static SalesPresentationPresetVersion Version(Guid company, Guid preset, int number, DateTime now) => new(Guid.NewGuid(), company, preset, number, null, null, "Goal", "Audience", 30, null, "manual", "en", true, false, false, null, now);

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(VirtualCompanyDbContext db, SalesPresentationPresetService service, Guid company, Guid user, Guid agent) { Db = db; Service = service; CompanyId = company; UserId = user; AgentId = agent; }
        public VirtualCompanyDbContext Db { get; } public SalesPresentationPresetService Service { get; } public Guid CompanyId { get; } public Guid UserId { get; } public Guid AgentId { get; }
        public static async Task<Fixture> CreateAsync()
        {
            var company = Guid.NewGuid(); var user = Guid.NewGuid(); var agent = Guid.NewGuid(); var context = new Context(company, user);
            var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseInMemoryDatabase($"preset-{Guid.NewGuid():N}").Options, context);
            db.Add(new Company(company, "Preset Company")); db.Add(new User(user, "owner@example.com", "Owner", "test", user.ToString("N")));
            db.Add(new CompanyMembership(Guid.NewGuid(), company, user, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            db.Add(new Agent(agent, company, "sales", "Alex", "Presenter", "Sales", null, AgentSeniority.Senior, AgentStatus.Active)); await db.SaveChangesAsync();
            return new(db, new SalesPresentationPresetService(db, new Storage(), Options.Create(new SalesPresentationOptions()), TimeProvider.System), company, user, agent);
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
    private sealed class Storage : ICompanyDocumentStorage
    {
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => throw new NotSupportedException();
        public Task<DocumentStorageWriteResult> WriteAsync(DocumentStorageWriteRequest request, CancellationToken ct) => Task.FromResult(new DocumentStorageWriteResult(request.StorageKey, null));
        public Task DeleteAsync(string key, CancellationToken ct) => Task.CompletedTask;
    }
    private sealed class Context(Guid? company, Guid user) : ICompanyContextAccessor
    {
        public Guid? CompanyId { get; private set; } = company; public Guid? UserId { get; private set; } = user; public bool IsResolved => CompanyId.HasValue; public ResolvedCompanyMembershipContext? Membership { get; private set; }
        public void SetCompanyId(Guid? value) => CompanyId = value; public void SetCompanyContext(ResolvedCompanyMembershipContext? value) { Membership = value; CompanyId = value?.CompanyId; UserId = value?.UserId; }
    }
}

public sealed class SalesPresentationPresetMigrationTests
{
    [Fact]
    public void Migration_is_additive_and_contains_tenant_safe_versioned_schema()
    {
        var operations = Up(new AddSalesPresentationPresetLibrary());
        Assert.DoesNotContain(operations, x => x is AlterColumnOperation or DropColumnOperation);
        var tables = operations.OfType<CreateTableOperation>().ToDictionary(x => x.Name);
        Assert.Contains("sales_presentation_presets", tables.Keys); Assert.Contains("sales_presentation_preset_versions", tables.Keys);
        Assert.Contains("sales_presentation_preset_assets", tables.Keys); Assert.Contains("sales_presentation_preset_slides", tables.Keys);
        var indexes = operations.OfType<CreateIndexOperation>().ToArray();
        Assert.Contains(indexes, x => x.Table == "sales_presentation_preset_versions" && x.IsUnique && x.Columns.SequenceEqual(["company_id", "preset_id", "version_number"]));
        Assert.Contains(indexes, x => x.Table == "sales_presentation_preset_slides" && x.IsUnique && x.Columns.SequenceEqual(["company_id", "asset_id", "processing_version", "slide_number"]));
        Assert.Contains(tables["sales_presentation_preset_versions"].ForeignKeys, x => x.PrincipalTable == "agents" && x.Columns.SequenceEqual(["company_id", "default_presenter_agent_id"]));
    }
    private static IReadOnlyList<MigrationOperation> Up(Migration migration) { var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer"); migration.GetType().GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(migration, [builder]); return builder.Operations; }
}
