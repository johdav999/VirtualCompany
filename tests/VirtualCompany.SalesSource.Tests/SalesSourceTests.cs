using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Sales;
using VirtualCompany.Domain.Entities;
using Xunit;

namespace VirtualCompany.SalesSource.Tests;

public sealed class SalesSourceTests
{
    [Fact]
    public async Task Attribution_tracks_first_last_conversion_cost_and_tenant_scope()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await CreateSourceSchemaAsync(connection);
        var options = new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options;
        await using var db = new VirtualCompanyDbContext(options);
        var service = new SalesSourceService(db); var company = Guid.NewGuid(); var lead = Guid.NewGuid();
        db.Attach(new Lead(lead, company, "Source model test", SalesPipelineStage.NewStageId));
        var email = await service.RecordAsync(company, new("lead", lead, "email", "outlook", "email", "inquiry", "message-1", DateTime.UtcNow, "visitor", "buyer@example.com", Cost: 20, Currency: "SEK", IsConversion: true), default);
        var eventTouch = await service.RecordAsync(company, new("lead", lead, "event", "sme_fair", "event", "discovery", "badge-1", DateTime.UtcNow.AddDays(-1), "human", "owner", Cost: 100, Currency: "SEK"), default);
        var a = await service.GetAsync(company, "lead", lead, default);
        Assert.NotNull(a); Assert.Equal(2, a!.TouchCount); Assert.Equal(120, a.TotalAcquisitionCost); Assert.Equal(email.Id, a.ConversionTouchId); Assert.Equal(eventTouch.Id, a.FirstTouchId); Assert.Equal(email.Id, a.LastTouchId); Assert.Equal(2, a.Timeline.Count); Assert.Null(await service.GetAsync(Guid.NewGuid(), "lead", lead, default));
    }

    private static async Task CreateSourceSchemaAsync(SqliteConnection connection)
    {
        var command=connection.CreateCommand(); command.CommandText="""
CREATE TABLE SalesSourceTouches (Id TEXT NOT NULL PRIMARY KEY, CompanyId TEXT NOT NULL, CampaignId TEXT NULL, SubjectType TEXT NOT NULL, SubjectId TEXT NOT NULL, Category TEXT NOT NULL, Provider TEXT NOT NULL, Channel TEXT NOT NULL, InteractionType TEXT NOT NULL, SourceReference TEXT NOT NULL, Evidence TEXT NULL, LandingPage TEXT NULL, Referrer TEXT NULL, UtmSource TEXT NULL, UtmMedium TEXT NULL, UtmCampaign TEXT NULL, UtmContent TEXT NULL, UtmTerm TEXT NULL, Cost TEXT NULL, Currency TEXT NULL, ActorType TEXT NOT NULL, ActorReference TEXT NULL, MetadataJson TEXT NULL, DedupeKey TEXT NOT NULL, ObservedUtc TEXT NOT NULL, CreatedUtc TEXT NOT NULL);
CREATE UNIQUE INDEX IX_SalesSourceTouches_CompanyId_DedupeKey ON SalesSourceTouches(CompanyId,DedupeKey);
CREATE TABLE SalesSourceAttributions (Id TEXT NOT NULL PRIMARY KEY, CompanyId TEXT NOT NULL, SubjectType TEXT NOT NULL, SubjectId TEXT NOT NULL, OriginalTouchId TEXT NOT NULL, FirstTouchId TEXT NOT NULL, LastTouchId TEXT NOT NULL, ConversionTouchId TEXT NULL, TouchCount INTEGER NOT NULL, TotalAcquisitionCost TEXT NOT NULL, Currency TEXT NULL, CreatedUtc TEXT NOT NULL, UpdatedUtc TEXT NOT NULL);
CREATE UNIQUE INDEX IX_SalesSourceAttributions_Subject ON SalesSourceAttributions(CompanyId,SubjectType,SubjectId);
"""; await command.ExecuteNonQueryAsync();
    }

    [Theory]
    [InlineData("sales.plan_prospecting_run", ToolActionType.Recommend)] [InlineData("sales.start_prospecting_run", ToolActionType.Execute)] [InlineData("sales.list_prospects", ToolActionType.Read)] [InlineData("sales.research_prospect", ToolActionType.Recommend)] [InlineData("sales.recommend_prospect_decision", ToolActionType.Recommend)]
    public void Prospecting_tools_have_action_boundaries(string name, ToolActionType action)
    { var registry=new StaticCompanyToolRegistry(); Assert.True(registry.TryGetToolDefinition(name,out var d)); Assert.Equal(action,d.ActionType); Assert.True(registry.TryGetTool(name,out var registration)); Assert.Contains("sales",registration.Scopes); }
}
