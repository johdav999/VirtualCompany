using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Api.Tests;

[Trait("Category", "SqlServer")]
public sealed class SalesNarrationSqlServerTests
{
    [ApiSqlServerFact]
    public async Task Fresh_migrations_and_concurrent_asset_claims_preserve_one_owner()
    {
        await WithDatabase(async db =>
        {
            await db.Database.MigrateAsync();
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            Assert.False(db.Database.HasPendingModelChanges());
            var company = Guid.NewGuid();
            db.Companies.Add(new Company(company, "Synthetic narration SQL test"));
            var asset = new SalesNarrationAsset { Id = Guid.NewGuid(), CompanyId = company, CacheKey = new string('a',64),
                CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow };
            db.SalesNarrationAssets.Add(asset); await db.SaveChangesAsync();
            await using var other = new VirtualCompanyDbContext(Options(db.Database.GetConnectionString()!));
            var stale = await other.SalesNarrationAssets.IgnoreQueryFilters().SingleAsync();
            asset.Status = SalesNarrationAsset.Generating; asset.Version++; await db.SaveChangesAsync();
            stale.Status = SalesNarrationAsset.Generating; stale.Version++;
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => other.SaveChangesAsync());
            other.ChangeTracker.Clear();
            other.SalesNarrationAssets.Add(new() { Id = Guid.NewGuid(), CompanyId = company, CacheKey = asset.CacheKey,
                CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow });
            await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
        });
    }

    [ApiSqlServerFact]
    public async Task Retrying_execution_strategy_supports_the_complete_narration_flow()
    {
        await WithDatabase(async migrationDb =>
        {
            await migrationDb.Database.MigrateAsync();
            var company = Guid.NewGuid();
            var actor = Guid.NewGuid();
            var customer = Guid.NewGuid();
            var contact = Guid.NewGuid();
            var lead = Guid.NewGuid();
            var account = Guid.NewGuid();
            var clock = new RoomClock();
            var context = new SalesNarrationTests.NarrationContext(company, actor);
            await using var db = new VirtualCompanyDbContext(
                Options(migrationDb.Database.GetConnectionString()!), context);

            db.Companies.Add(new Company(company, "Narration retry test"));
            db.Users.Add(new User(actor, "host@example.test", "Host", "test", actor.ToString("N")));
            db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), company, actor,
                CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            db.CustomerCompanies.Add(new CustomerCompany(customer, company, "Customer"));
            db.Contacts.Add(new Contact(contact, company, "Buyer", "buyer@example.test", customer));
            db.Leads.Add(new Lead(lead, company, "Lead", SalesPipelineStage.QualifiedStageId,
                SalesStatuses.Qualified, contact, customer));
            var external = new ExternalAccountConnection(account, company, actor,
                ExternalAccountProvider.Google, "host@example.test", "host@example.test", "provider", "external");
            external.SetStatus(ExternalConnectionStatus.Active);
            db.ExternalAccountConnections.Add(external);
            var calendar = new CalendarConnection(account, company, actor, account,
                ExternalAccountProvider.Google, "host@example.test", "host@example.test");
            calendar.SetStatus(ExternalConnectionStatus.Active);
            db.CalendarConnections.Add(calendar);
            var invitation = new SalesMeetingInvitation(Guid.NewGuid(), company, lead, null, contact, account,
                ExternalAccountProvider.Google, "host@example.test", "buyer@example.test", "Buyer", "Meeting",
                "Goal", clock.Now, clock.Now.AddMinutes(30), "Europe/Stockholm", null, false, actor);
            db.SalesMeetingInvitations.Add(invitation);
            var session = new SalesMeetingSession(Guid.NewGuid(), company, invitation.Id, lead, null, contact,
                customer, "Goal", "Audience", 30, null, "test-calendar-event", SalesMeetingConsentStatus.Pending,
                SalesMeetingRetentionPolicy.Standard, 365, clock.Now, actor, clock.Now);
            db.SalesMeetingSessions.Add(session);
            var agent = new Agent(Guid.NewGuid(), company, "alex-sales", "Alex", "Sales representative", "Sales",
                null, AgentSeniority.Senior, AgentStatus.Active);
            db.Agents.Add(agent);
            var deck = new SalesPresentationDeck(Guid.NewGuid(), company, session.Id, agent.Id, 1,
                "Synthetic deck", "deck.pptx",
                "application/vnd.openxmlformats-officedocument.presentationml.presentation", 100,
                new string('a', 64), "safe/deck", null, actor, clock.Now);
            deck.BeginProcessing(clock.Now, TimeSpan.FromMinutes(10));
            deck.MarkProcessed(2, "test", "1", "static", 1, clock.Now);
            deck.Activate(clock.Now);
            db.SalesPresentationDecks.Add(deck);
            for (var i = 1; i <= 2; i++)
                db.SalesPresentationSlides.Add(new SalesPresentationSlide(Guid.NewGuid(), company, deck.Id, 1, i,
                    "Slide", i == 1 ? "Welcome to the presentation." : "Customer goals.", "PRIVATE NOTES",
                    $"safe/{i}", null, 1600, 900, 100, 100, new string((char)('a' + i), 64),
                    "Objective", 60, "Transition", clock.Now));
            await db.SaveChangesAsync();

            var speech = new SalesNarrationTests.FakeSpeech();
            var storage = new SalesNarrationTests.MemoryStorage();
            var settings = new SalesNarrationOptions
            {
                Enabled = true,
                InputUsdPerMillion = 10,
                OutputUsdPerMillion = 20,
                RateVersion = "synthetic-test-only"
            };
            var service = new SalesNarrationService(db, speech, storage, clock,
                Microsoft.Extensions.Options.Options.Create(settings));
            var worker = new SalesNarrationWorker(db, context, service, speech, storage,
                Microsoft.Extensions.Options.Options.Create(settings), clock);

            var revision = await service.PrepareAsync(company, actor, session.Id, new("en"), default);
            await service.DecideAsync(company, actor, revision.Id, "approve", new(revision.Version), default);
            foreach (var assetId in await db.SalesNarrationAssets.Select(x => x.Id).ToListAsync())
                await worker.ProcessAsync(company, assetId, default);

            var completed = await service.GetAsync(company, actor, session.Id, default);
            Assert.Equal("ready", completed.Revisions.Single().Status);
            Assert.Equal(2, speech.Calls);
        });
    }
    [ApiSqlServerFact]
    public async Task Additive_upgrade_is_repeatable_and_preserves_existing_Teams_records()
    {
        await WithDatabase(async db =>
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE __EFMigrationsHistory (MigrationId nvarchar(150) NOT NULL PRIMARY KEY, ProductVersion nvarchar(32) NOT NULL);
                CREATE TABLE companies (Id uniqueidentifier NOT NULL PRIMARY KEY);
                CREATE TABLE sales_meeting_sessions (id uniqueidentifier NOT NULL PRIMARY KEY, company_id uniqueidentifier NOT NULL,
                    CONSTRAINT AK_sessions UNIQUE(company_id,id));
                CREATE TABLE sales_presentation_decks (id uniqueidentifier NOT NULL PRIMARY KEY, company_id uniqueidentifier NOT NULL,
                    CONSTRAINT AK_decks UNIQUE(company_id,id));
                CREATE TABLE teams_meeting_calls (id uniqueidentifier NOT NULL PRIMARY KEY, evidence nvarchar(100) NOT NULL);
                INSERT teams_meeting_calls VALUES ('30000000-0000-0000-0000-000000000001',N'unchanged-teams-evidence');
                """);
            var migrations = db.Database.GetMigrations().ToArray();
            var index = Array.FindIndex(migrations, x => x.EndsWith("_AddApprovedSalesNarration"));
            var script = db.GetService<IMigrator>().GenerateScript(migrations[index-1], migrations[index], MigrationsSqlGenerationOptions.Idempotent);
            for (var pass = 0; pass < 2; pass++)
                foreach (var batch in Regex.Split(script, @"^GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase).Where(x => !string.IsNullOrWhiteSpace(x)))
                    await db.Database.ExecuteSqlRawAsync(batch);
            await db.Database.OpenConnectionAsync();
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM teams_meeting_calls WHERE id='30000000-0000-0000-0000-000000000001' AND evidence=N'unchanged-teams-evidence'";
            Assert.Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync()));
        });
    }

    private static DbContextOptions<VirtualCompanyDbContext> Options(string connection) => new DbContextOptionsBuilder<VirtualCompanyDbContext>()
        .UseSqlServer(connection, sql => sql
            .MigrationsAssembly(typeof(VirtualCompany.Persistence.Migrations.Persistence.MigrationAssemblyMarker).Assembly.GetName().Name)
            .EnableRetryOnFailure()).Options;
    private static async Task WithDatabase(Func<VirtualCompanyDbContext, Task> action)
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable(ApiSqlServerFactAttribute.ConnectionVariable)!)
        { InitialCatalog = $"virtualcompany_narration_test_{Guid.NewGuid():N}", Pooling = false };
        var master = new SqlConnectionStringBuilder(connection.ConnectionString) { InitialCatalog = "master" };
        await using var server = new SqlConnection(master.ConnectionString); await server.OpenAsync();
        await using (var create = new SqlCommand($"CREATE DATABASE [{connection.InitialCatalog}]", server)) await create.ExecuteNonQueryAsync();
        try { await using var db = new VirtualCompanyDbContext(Options(connection.ConnectionString)); await action(db); }
        finally
        {
            if (!Regex.IsMatch(connection.InitialCatalog, @"^virtualcompany_narration_test_[a-f0-9]{32}$")) throw new InvalidOperationException("Unsafe test database name.");
            await using var drop = new SqlCommand($"ALTER DATABASE [{connection.InitialCatalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{connection.InitialCatalog}]", server);
            await drop.ExecuteNonQueryAsync();
        }
    }
}

