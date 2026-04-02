using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Auth;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class TenantQueryFilterTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public TenantQueryFilterTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CompanyNotes_query_returns_no_rows_without_company_context()
    {
        await SeedCompanyNotesAsync();

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        var notes = await dbContext.CompanyNotes.AsNoTracking().ToListAsync();

        Assert.Empty(notes);
    }

    [Fact]
    public async Task CompanyNotes_query_filters_rows_to_active_company_context()
    {
        var ids = await SeedCompanyNotesAsync();

        using var scope = _factory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(ids.CompanyAId);

        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var notes = await dbContext.CompanyNotes.AsNoTracking()
            .OrderBy(x => x.Title)
            .ToListAsync();

        var note = Assert.Single(notes);
        Assert.Equal(ids.CompanyANoteId, note.Id);
        Assert.Equal(ids.CompanyAId, note.CompanyId);
    }

    [Fact]
    public async Task ContextRetrievalSources_query_filters_rows_to_active_company_context()
    {
        var companyAId = Guid.NewGuid();
        var companyBId = Guid.NewGuid();
        var retrievalAId = Guid.NewGuid();
        var retrievalBId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Companies.AddRange(new Company(companyAId, "Company A"), new Company(companyBId, "Company B"));
            dbContext.ContextRetrievals.AddRange(
                new ContextRetrieval(retrievalAId, companyAId, Guid.NewGuid(), null, null, "finance retrieval", "hash-a", null, "audit"),
                new ContextRetrieval(retrievalBId, companyBId, Guid.NewGuid(), null, null, "sales retrieval", "hash-b", null, "audit"));
            dbContext.ContextRetrievalSources.AddRange(
                new ContextRetrievalSource(Guid.NewGuid(), retrievalAId, companyAId, "memory_item", "memory-a", null, null, null, "Finance memory", "Finance memory snippet", "memory", "Memory", 1, "fact | company_wide", 1, 0.9d, DateTime.UtcNow),
                new ContextRetrievalSource(Guid.NewGuid(), retrievalBId, companyBId, "memory_item", "memory-b", null, null, null, "Sales memory", "Sales memory snippet", "memory", "Memory", 1, "fact | company_wide", 1, 0.9d, DateTime.UtcNow));

            return Task.CompletedTask;
        });

        using var scope = _factory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(companyAId);

        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var sources = await dbContext.ContextRetrievalSources.AsNoTracking().ToListAsync();

        var source = Assert.Single(sources);
        Assert.Equal(companyAId, source.CompanyId);
        Assert.Equal(retrievalAId, source.RetrievalId);
        Assert.Equal("memory", source.SectionId);
    }

    private async Task<SeedIds> SeedCompanyNotesAsync()
    {
        var companyAId = Guid.NewGuid();
        var companyBId = Guid.NewGuid();
        var companyANoteId = Guid.NewGuid();
        var companyBNoteId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Companies.AddRange(new Company(companyAId, "Company A"), new Company(companyBId, "Company B"));
            dbContext.CompanyNotes.AddRange(
                new CompanyOwnedNote(companyANoteId, companyAId, "A note", "inside company A"),
                new CompanyOwnedNote(companyBNoteId, companyBId, "B note", "inside company B"));

            return Task.CompletedTask;
        });

        return new SeedIds(companyAId, companyBId, companyANoteId, companyBNoteId);
    }

    private sealed record SeedIds(Guid CompanyAId, Guid CompanyBId, Guid CompanyANoteId, Guid CompanyBNoteId);
}