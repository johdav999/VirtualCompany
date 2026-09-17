using Microsoft.EntityFrameworkCore;
using VirtualCompany.Infrastructure.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Application.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class SalesRoomNarrationSelectionTests
{
    [Fact]
    public async Task Identical_replacement_preserves_playback_and_original_revocation_without_regeneration()
    {
        await using var f = await SalesNarrationTests.Fixture.Create();
        var revision = await f.Prepare();
        await f.Approve(revision); await f.Generate();
        var original = await f.Db.SalesNarrationRevisions.AsNoTracking().SingleAsync();
        await ReplaceDeck(f, "identical");
        Assert.Equal(revision.Id, (await SalesRoomNarrationSelection.Current(f.Db, f.Company, f.Session, f.Clock.Now).SingleAsync()).Id);
        var playback = await f.Service.OpenPlaybackAsync(f.Company, f.Actor, f.Request(revision), default);
        Assert.NotEmpty(playback.Pcm);
        Assert.Equal(2, f.Speech.Calls);
        var retained = await f.Db.SalesNarrationRevisions.SingleAsync();
        Assert.Equal(original.DeckId, retained.DeckId);
        Assert.Equal(original.ManifestHash, retained.ManifestHash);
        retained.RevokedUtc = f.Clock.Now;
        await f.Db.SaveChangesAsync();
        Assert.Empty(await SalesRoomNarrationSelection.Current(f.Db, f.Company, f.Session, f.Clock.Now).ToListAsync());
        await Assert.ThrowsAsync<SalesNarrationException>(() => f.Service.OpenPlaybackAsync(f.Company, f.Actor, f.Request(revision), default));
    }

    [Theory]
    [InlineData("notes")]
    [InlineData("text")]
    [InlineData("hash")]
    [InlineData("missing_slide")]
    [InlineData("file")]
    public async Task Changed_replacement_cannot_inherit_approved_audio(string change)
    {
        await using var f = await SalesNarrationTests.Fixture.Create();
        var revision = await f.Prepare();
        await f.Approve(revision); await f.Generate();
        await ReplaceDeck(f, change);
        Assert.Empty(await SalesRoomNarrationSelection.Current(f.Db, f.Company, f.Session, f.Clock.Now).ToListAsync());
        await Assert.ThrowsAsync<SalesNarrationException>(() => f.Service.OpenPlaybackAsync(f.Company, f.Actor, f.Request(revision), default));
    }

    private static async Task ReplaceDeck(SalesNarrationTests.Fixture f, string change)
    {
        var old = await f.Db.SalesPresentationDecks.SingleAsync();
        var slides = await f.Db.SalesPresentationSlides.OrderBy(x => x.SlideNumber).ToListAsync();
        old.Deactivate(f.Clock.Now);
        await f.Db.SaveChangesAsync();
        var deck = new SalesPresentationDeck(Guid.NewGuid(), f.Company, f.Session, old.AgentId, 2,
            "Identical preset materialization", "same.pptx", old.ContentType, old.FileSizeBytes,
            change == "file" ? new string('f', 64) : old.ContentHash, "safe/copy", null, f.Actor, f.Clock.Now);
        deck.BeginProcessing(f.Clock.Now, TimeSpan.FromMinutes(10));
        deck.MarkProcessed(slides.Count, "test", "1", "static", 1, f.Clock.Now);
        deck.Activate(f.Clock.Now);
        f.Db.SalesPresentationDecks.Add(deck);
        // A second processing snapshot avoids legacy-upload source deduplication.
        f.Db.Entry(deck).Property(x => x.ProcessingVersion).CurrentValue = 2;
        foreach (var slide in slides)
        {
            if (change == "missing_slide" && slide.SlideNumber == 2) continue;
            f.Db.SalesPresentationSlides.Add(new(Guid.NewGuid(), f.Company, deck.Id, 2, slide.SlideNumber,
                slide.Title, change == "text" ? slide.ExtractedText.ToUpperInvariant() : slide.ExtractedText,
                change == "notes" ? "Changed notes" : slide.SpeakerNotes, "safe/copy-slide", null,
                1600, 900, 100, 100, change == "hash" ? new string('f', 64) : slide.ContentHash,
                "Objective", 60, "Transition", f.Clock.Now));
        }
        await f.Db.SaveChangesAsync();
    }
    [Fact]
    public async Task Retired_deck_is_not_offered_even_when_its_narration_is_approved()
    {
        await using var f = await SalesNarrationTests.Fixture.Create();
        var revision = await f.Prepare();
        await f.Approve(revision); await f.Generate();
        Assert.Equal(revision.Id, (await SalesRoomNarrationSelection.Current(f.Db, f.Company, f.Session, f.Clock.Now).SingleAsync()).Id);
        var deck = await f.Db.SalesPresentationDecks.SingleAsync();
        deck.Deactivate(f.Clock.Now);
        await f.Db.SaveChangesAsync();
        Assert.Empty(await SalesRoomNarrationSelection.Current(f.Db, f.Company, f.Session, f.Clock.Now).ToListAsync());
        Assert.NotNull((await f.Db.SalesNarrationRevisions.SingleAsync()).ApprovedUtc);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("session")]
    [InlineData("expired")]
    [InlineData("revoked")]
    [InlineData("deck_version")]
    [InlineData("processing_version")]
    public async Task Selection_requires_current_company_session_and_release(string change)
    {
        await using var f = await SalesNarrationTests.Fixture.Create();
        var prepared = await f.Prepare();
        await f.Approve(prepared);
        var revision = await f.Db.SalesNarrationRevisions.SingleAsync();
        if (change == "revoked") revision.RevokedUtc = f.Clock.Now;
        if (change == "deck_version") revision.DeckVersion++;
        if (change == "processing_version") revision.ProcessingVersion++;
        await f.Db.SaveChangesAsync();
        var result = await SalesRoomNarrationSelection.Current(f.Db,
            change == "company" ? Guid.NewGuid() : f.Company,
            change == "session" ? Guid.NewGuid() : f.Session,
            change == "expired" ? revision.RetainUntilUtc : f.Clock.Now).ToListAsync();
        Assert.Empty(result);
    }
}
