using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

internal static class SalesRoomNarrationSelection
{
    // Re-materializing the same file for the same meeting changes deck IDs, not
    // the approved content. Preserve the original immutable release and its revocation.
    internal static IQueryable<SalesPresentationDeck> CompatibleDecks(
        VirtualCompanyDbContext db, Guid companyId, Guid sessionId)
    {
        var decks = db.SalesPresentationDecks.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.SessionId == sessionId &&
                x.Status == SalesPresentationDeckStatus.Processed);
        var slides = db.SalesPresentationSlides.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId);
        return decks.Where(source => source.IsActive || decks.Any(active => active.IsActive &&
            active.ContentHash == source.ContentHash && active.SlideCount == source.SlideCount &&
            slides.Count(x => x.DeckId == source.Id && x.ProcessingVersion == source.ProcessingVersion) ==
                slides.Count(x => x.DeckId == active.Id && x.ProcessingVersion == active.ProcessingVersion) &&
            !slides.Any(old => old.DeckId == source.Id && old.ProcessingVersion == source.ProcessingVersion &&
                !slides.Any(current => current.DeckId == active.Id && current.ProcessingVersion == active.ProcessingVersion &&
                    current.SlideNumber == old.SlideNumber && current.ContentHash == old.ContentHash &&
                    current.ExtractedText == old.ExtractedText && current.SpeakerNotes == old.SpeakerNotes))));
    }

    internal static IQueryable<SalesNarrationRevision> Current(
        VirtualCompanyDbContext db, Guid companyId, Guid sessionId, DateTime now)
    {
        var decks = CompatibleDecks(db, companyId, sessionId);
        return
        db.SalesNarrationRevisions.IgnoreQueryFilters().AsNoTracking().Where(revision =>
            revision.CompanyId == companyId && revision.SessionId == sessionId &&
            revision.ApprovedUtc != null && revision.RevokedUtc == null && revision.RetainUntilUtc > now &&
            decks.Any(deck => deck.Id == revision.DeckId &&
                deck.Version == revision.DeckVersion && deck.ProcessingVersion == revision.ProcessingVersion))
        .OrderByDescending(revision => revision.ApprovedUtc).ThenByDescending(revision => revision.CreatedUtc);
    }

    // SQL Server collations may compare text without case sensitivity. The final
    // release gate uses ordinal equality before returning any audio.
    internal static async Task<bool> HasIdenticalActiveSnapshotAsync(
        VirtualCompanyDbContext db, SalesPresentationDeck source, CancellationToken ct)
    {
        if (source.IsActive) return true;
        var active = await db.SalesPresentationDecks.AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == source.CompanyId && x.SessionId == source.SessionId && x.IsActive &&
            x.Status == SalesPresentationDeckStatus.Processed, ct);
        if (active is null || active.ContentHash != source.ContentHash || active.SlideCount != source.SlideCount)
            return false;
        var oldSlides = await db.SalesPresentationSlides.AsNoTracking().Where(x => x.CompanyId == source.CompanyId &&
            x.DeckId == source.Id && x.ProcessingVersion == source.ProcessingVersion).OrderBy(x => x.SlideNumber).ToListAsync(ct);
        var newSlides = await db.SalesPresentationSlides.AsNoTracking().Where(x => x.CompanyId == source.CompanyId &&
            x.DeckId == active.Id && x.ProcessingVersion == active.ProcessingVersion).OrderBy(x => x.SlideNumber).ToListAsync(ct);
        return oldSlides.Count > 0 && oldSlides.Count == newSlides.Count && oldSlides.Zip(newSlides).All(pair =>
            pair.First.SlideNumber == pair.Second.SlideNumber && pair.First.ContentHash == pair.Second.ContentHash &&
            pair.First.ExtractedText == pair.Second.ExtractedText && pair.First.SpeakerNotes == pair.Second.SpeakerNotes);
    }
}
