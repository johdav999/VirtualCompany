using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Sales;
using VirtualCompany.Persistence.Migrations.Persistence.Migrations;

namespace VirtualCompany.Api.Tests;

public sealed class SalesPresentationAdHocTests
{
    [Fact]
    public void Ad_hoc_run_is_isolated_and_can_complete_without_a_meeting_runtime()
    {
        var now = DateTime.UtcNow; var actor = Guid.NewGuid(); var request = Guid.NewGuid();
        var run = SalesPresentationRun.CreateAdHoc(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), request,
            Guid.NewGuid(), "Goal", "Audience", 20, null, "en", "assisted", false, false, false, false, false,
            null, null, null, null, actor, now);
        run.BeginPreparation(actor, now.AddSeconds(1));
        run.CompletePreparationWithoutRuntime(true, "[\"customer_context_missing\"]", actor, now.AddSeconds(2));

        Assert.Equal(SalesPresentationPresetContextType.AdHoc, run.ContextType);
        Assert.Equal($"ad-hoc:{request:N}", run.ContextReference);
        Assert.Equal("preparation_only", run.RuntimeStrategy);
        Assert.Null(run.MeetingSessionId);
        Assert.Null(run.CompatibilityDeckId);
        Assert.Equal(SalesPresentationRunPreparationStatus.NeedsReview, run.PreparationStatus);
    }

    [Fact]
    public async Task No_context_creates_explicit_missing_evidence_and_idempotent_preparation_only_run()
    {
        await using var fixture = await Fixture.CreateAsync(); var requestId = Guid.NewGuid();
        var command = new CreateAdHocSalesPresentationCommand(requestId, fixture.VersionId, null, null, null, null, null,
            null, null, null, null, null, null, "preparation_only");

        var first = await fixture.Service.CreateAsync(fixture.CompanyId, fixture.UserId, command, "test", default);
        var second = await fixture.Service.CreateAsync(fixture.CompanyId, fixture.UserId, command, "test", default);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("needs_review", first.PreparationStatus);
        Assert.False(first.CanOpenPresenter);
        Assert.Null(first.MeetingSessionId);
        Assert.Contains("customer_context_missing", first.ReadinessBlockers);
        Assert.Contains(first.Artifacts, x => x.Type == "missing_evidence" && x.SourceReference is null);
        Assert.Equal(1, await fixture.Db.SalesPresentationRuns.CountAsync());
        Assert.Equal(1, await fixture.Db.AuditEvents.CountAsync(x => x.Action == "sales.presentation_run.ad_hoc_prepared"));
    }

    [Fact]
    public async Task Ad_hoc_service_rejects_live_runtime_and_cross_company_context()
    {
        await using var fixture = await Fixture.CreateAsync();
        var live = new CreateAdHocSalesPresentationCommand(Guid.NewGuid(), fixture.VersionId, null, null, null, null, null,
            null, null, null, null, null, null, "meeting_session");
        var liveError = await Assert.ThrowsAsync<SalesPresentationPresetConflictException>(() => fixture.Service.CreateAsync(fixture.CompanyId, fixture.UserId, live, null, default));
        Assert.Equal(SalesPresentationAdHocProblemCodes.RuntimeUnavailable, liveError.Code);

        var foreignAccount = new CreateAdHocSalesPresentationCommand(Guid.NewGuid(), fixture.VersionId, Guid.NewGuid(), null, null, null, null,
            null, null, null, null, null, null, "preparation_only");
        var contextError = await Assert.ThrowsAsync<SalesPresentationPresetConflictException>(() => fixture.Service.CreateAsync(fixture.CompanyId, fixture.UserId, foreignAccount, null, default));
        Assert.Equal(SalesPresentationAdHocProblemCodes.ContextInvalid, contextError.Code);
    }

    [Fact]
    public async Task Legacy_backfill_is_idempotent_and_never_deduplicates_ambiguous_matching_content()
    {
        await using var fixture = await Fixture.CreateAsync(); var hash = new string('c', 64); var now = DateTime.UtcNow;
        fixture.Db.AddRange(
            new SalesPresentationDeck(Guid.NewGuid(), fixture.CompanyId, Guid.NewGuid(), Guid.NewGuid(), 1, "Customer A", "a.pptx", "application/octet-stream", 10, hash, "legacy/a.pptx", null, fixture.UserId, now),
            new SalesPresentationDeck(Guid.NewGuid(), fixture.CompanyId, Guid.NewGuid(), Guid.NewGuid(), 1, "Customer B", "b.pptx", "application/octet-stream", 10, hash, "legacy/b.pptx", null, fixture.UserId, now));
        await fixture.Db.SaveChangesAsync();
        var service = new SalesPresentationLegacyMigrationService(fixture.Db, TimeProvider.System, NullLogger<SalesPresentationLegacyMigrationService>.Instance);

        var first = await service.ReconcileAsync(fixture.CompanyId, fixture.UserId, 50, "test", default);
        var second = await service.ReconcileAsync(fixture.CompanyId, fixture.UserId, 50, "test", default);

        Assert.Equal(2, first.CompatibilityOnly);
        Assert.All(first.Items, x => Assert.Equal("legacy.contextual_ownership_ambiguous", x.ReasonCode));
        Assert.Equal(2, first.Items.Select(x => x.Id).Distinct().Count());
        Assert.Equal(0, second.Scanned);
        Assert.Equal(2, await fixture.Db.SalesPresentationLegacyCompatibilityRecords.CountAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(VirtualCompanyDbContext db, SalesPresentationAdHocService service, Guid companyId, Guid userId, Guid versionId)
            => (Db, Service, CompanyId, UserId, VersionId) = (db, service, companyId, userId, versionId);
        public VirtualCompanyDbContext Db { get; }
        public SalesPresentationAdHocService Service { get; }
        public Guid CompanyId { get; }
        public Guid UserId { get; }
        public Guid VersionId { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var now = DateTime.UtcNow; var company = Guid.NewGuid(); var user = Guid.NewGuid(); var agent = Guid.NewGuid();
            var preset = new SalesPresentationPreset(Guid.NewGuid(), company, "Executive demo", "Reusable source", user, now);
            var version = new SalesPresentationPresetVersion(Guid.NewGuid(), company, preset.Id, 1, agent, null,
                "Explain the product", "Finance leaders", 30, null, "assisted", "en", true, false, true, null, now);
            var asset = new SalesPresentationPresetAsset(Guid.NewGuid(), company, version.Id, "deck.pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                100, new string('a', 64), "safe/deck.pptx", null, user, now);
            asset.BeginProcessing(now, TimeSpan.FromMinutes(5)); asset.MarkProcessed(1, "renderer", "1", "flattened", now);
            var slide = new SalesPresentationPresetSlide(Guid.NewGuid(), company, asset.Id, 1, 1, "Overview", "Approved product overview", null,
                "safe/slide-1.png", null, 1600, 900, 1600, 900, new string('b', 64), "Introduce the product", "Use approved facts", 60, "Next", now);
            version.Publish(user, now); preset.Publish(version.Id, now);

            var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseInMemoryDatabase($"adhoc-{Guid.NewGuid():N}").Options, new Context(company, user));
            db.AddRange(new Company(company, "Ad hoc Company"), new User(user, "owner@example.com", "Owner", "test", user.ToString("N")),
                new CompanyMembership(Guid.NewGuid(), company, user, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new Agent(agent, company, "sales", "Avery", "Presenter", "Sales", null, AgentSeniority.Senior, AgentStatus.Active),
                preset, version, asset, slide);
            await db.SaveChangesAsync();
            var service = new SalesPresentationAdHocService(db, new SalesPresentationRunContextResolver(db), TimeProvider.System, NullLogger<SalesPresentationAdHocService>.Instance);
            return new Fixture(db, service, company, user, version.Id);
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class Context(Guid? companyId, Guid userId) : ICompanyContextAccessor
    {
        public Guid? CompanyId { get; private set; } = companyId;
        public Guid? UserId { get; private set; } = userId;
        public bool IsResolved => CompanyId.HasValue;
        public ResolvedCompanyMembershipContext? Membership { get; private set; }
        public void SetCompanyId(Guid? value) => CompanyId = value;
        public void SetCompanyContext(ResolvedCompanyMembershipContext? value) { Membership = value; CompanyId = value?.CompanyId; UserId = value?.UserId; }
    }
}

public sealed class SalesPresentationAdHocMigrationTests
{
    [Fact]
    public void Migration_is_additive_and_preserves_legacy_runtime_semantics()
    {
        var operations = Up(new AddAdHocPresentationRunsAndLegacyCompatibility());
        Assert.DoesNotContain(operations, x => x is DropColumnOperation or DropTableOperation);
        var runtime = Assert.Single(operations.OfType<AddColumnOperation>(), x => x.Table == "sales_presentation_runs" && x.Name == "runtime_strategy");
        Assert.Equal("meeting_session", runtime.DefaultValue);
        var table = Assert.Single(operations.OfType<CreateTableOperation>(), x => x.Name == "sales_presentation_legacy_compatibility");
        Assert.Contains(table.ForeignKeys, x => x.PrincipalTable == "sales_presentation_decks" && x.Columns.SequenceEqual(["company_id", "deck_id"]));
        Assert.Contains(operations.OfType<CreateIndexOperation>(), x => x.Table == "sales_presentation_runs" && x.IsUnique && x.Columns.SequenceEqual(["company_id", "client_request_id"]));
    }
    private static IReadOnlyList<MigrationOperation> Up(Migration migration) { var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer"); migration.GetType().GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(migration, [builder]); return builder.Operations; }
}
