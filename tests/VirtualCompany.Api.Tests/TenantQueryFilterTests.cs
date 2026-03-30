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