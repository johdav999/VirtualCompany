using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesPresentationDeckService(
    VirtualCompanyDbContext db,
    ICompanyDocumentStorage storage,
    IOptions<SalesPresentationOptions> options,
    TimeProvider timeProvider) : ISalesPresentationDeckService
{
    public async Task<SalesPresentationDeckDto> ImportAsync(
        Guid companyId, Guid actorUserId, Guid sessionId,
        ImportSalesPresentationDeckCommand command, string? correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateIds(companyId, actorUserId, sessionId);
        if (command.AgentId == Guid.Empty)
            throw Validation("AgentId", "Choose an active Sales agent for this meeting deck.");
        if (command.Content is null)
            throw Validation("File", "A PowerPoint presentation is required.");
        await EnsureMemberAsync(companyId, actorUserId, cancellationToken);
        await EnsureSessionAndAgentAsync(companyId, sessionId, command.AgentId, cancellationToken);
        var normalizedFileName = Path.GetFileName(command.OriginalFileName?.Trim());
        if (!string.Equals(Path.GetExtension(normalizedFileName), ".pptx", StringComparison.OrdinalIgnoreCase))
            throw Validation("File", "Upload a PowerPoint .pptx file.");
        if (command.Length is < 1 || command.Length > options.Value.MaximumUploadBytes)
            throw Validation("File", $"The PowerPoint file must be between 1 byte and {options.Value.MaximumUploadBytes} bytes.");
        if (!IsSupportedContentType(command.ContentType))
            throw Validation("File", "The uploaded content type is not a supported PowerPoint presentation.");

        await using var buffered = await ReadBoundedAsync(command.Content, options.Value.MaximumUploadBytes, cancellationToken);
        ValidatePptxPackage(buffered);
        var hash = Convert.ToHexString(SHA256.HashData(buffered.ToArray())).ToLowerInvariant();
        var existing = await db.SalesPresentationDecks.AsNoTracking().SingleOrDefaultAsync(
            x => x.CompanyId == companyId && x.SessionId == sessionId &&
                 x.ContentHash == hash && x.ProcessingVersion == 1,
            cancellationToken);
        if (existing is not null) return await MapDeckAsync(existing, cancellationToken);

        var version = (await db.SalesPresentationDecks
            .Where(x => x.CompanyId == companyId && x.SessionId == sessionId)
            .MaxAsync(x => (int?)x.Version, cancellationToken) ?? 0) + 1;
        var deckId = Guid.NewGuid();
        var storageKey = $"companies/{companyId:N}/sales/meeting-sessions/{sessionId:N}/decks/{deckId:N}/original.pptx";
        buffered.Position = 0;
        var stored = await storage.WriteAsync(new DocumentStorageWriteRequest(
            companyId, deckId, storageKey, normalizedFileName, NormalizeContentType(command.ContentType), buffered), cancellationToken);
        var now = UtcNow();
        var deck = new SalesPresentationDeck(
            deckId, companyId, sessionId, command.AgentId, version,
            string.IsNullOrWhiteSpace(command.Title) ? Path.GetFileNameWithoutExtension(normalizedFileName) : command.Title,
            normalizedFileName, NormalizeContentType(command.ContentType), buffered.Length,
            hash, stored.StorageKey, stored.StorageUrl, actorUserId, now);
        db.SalesPresentationDecks.Add(deck);
        AddAudit(deck, actorUserId, AuditEventActions.SalesPresentationDeckImported,
            "A PowerPoint deck was stored and queued for safe background processing.", correlationId);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await storage.DeleteAsync(stored.StorageKey, CancellationToken.None);
            foreach (var entry in db.ChangeTracker.Entries().Where(x =>
                         x.State == EntityState.Added &&
                         (ReferenceEquals(x.Entity, deck) ||
                          x.Entity is AuditEvent audit && audit.TargetId == deck.Id.ToString("D"))))
                entry.State = EntityState.Detached;
            var winner = await db.SalesPresentationDecks.AsNoTracking().SingleOrDefaultAsync(
                x => x.CompanyId == companyId && x.SessionId == sessionId &&
                     x.ContentHash == hash && x.ProcessingVersion == 1,
                cancellationToken);
            if (winner is not null) return await MapDeckAsync(winner, cancellationToken);
            throw;
        }
        catch
        {
            await storage.DeleteAsync(stored.StorageKey, CancellationToken.None);
            throw;
        }
        return await MapDeckAsync(deck, cancellationToken);
    }

    public async Task<IReadOnlyList<SalesPresentationDeckDto>> ListAsync(
        Guid companyId, Guid sessionId, CancellationToken cancellationToken)
    {
        ValidateIds(companyId, sessionId);
        var decks = await db.SalesPresentationDecks.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.SessionId == sessionId)
            .OrderByDescending(x => x.Version).ToListAsync(cancellationToken);
        var results = new List<SalesPresentationDeckDto>(decks.Count);
        foreach (var deck in decks) results.Add(await MapDeckAsync(deck, cancellationToken));
        return results;
    }

    public async Task<SalesPresentationDeckDto?> GetAsync(
        Guid companyId, Guid sessionId, Guid deckId, CancellationToken cancellationToken)
    {
        ValidateIds(companyId, sessionId, deckId);
        var deck = await db.SalesPresentationDecks.AsNoTracking().SingleOrDefaultAsync(
            x => x.CompanyId == companyId && x.SessionId == sessionId && x.Id == deckId, cancellationToken);
        return deck is null ? null : await MapDeckAsync(deck, cancellationToken);
    }

    public async Task<SalesPresentationSlideDto?> GetSlideAsync(
        Guid companyId, Guid sessionId, Guid deckId, int slideNumber, CancellationToken cancellationToken)
    {
        if (slideNumber < 1) throw Validation("SlideNumber", "Slide number must be at least one.");
        var deck = await db.SalesPresentationDecks.AsNoTracking().SingleOrDefaultAsync(
            x => x.CompanyId == companyId && x.SessionId == sessionId && x.Id == deckId, cancellationToken);
        if (deck is null) return null;
        var slide = await db.SalesPresentationSlides.AsNoTracking().SingleOrDefaultAsync(
            x => x.CompanyId == companyId && x.DeckId == deckId &&
                 x.ProcessingVersion == deck.ProcessingVersion && x.SlideNumber == slideNumber,
            cancellationToken);
        return slide is null ? null : await MapSlideAsync(slide, cancellationToken);
    }

    public async Task<SalesPresentationDeckDto?> ActivateAsync(
        Guid companyId, Guid actorUserId, Guid sessionId, Guid deckId,
        string? correlationId, CancellationToken cancellationToken)
    {
        await EnsureMemberAsync(companyId, actorUserId, cancellationToken);
        var deck = await MutableDeckAsync(companyId, sessionId, deckId, cancellationToken);
        if (deck is null) return null;
        var now = UtcNow();
        var active = await db.SalesPresentationDecks
            .Where(x => x.CompanyId == companyId && x.SessionId == sessionId && x.IsActive && x.Id != deckId)
            .ToListAsync(cancellationToken);
        foreach (var item in active) item.Deactivate(now);
        try { deck.Activate(now); }
        catch (InvalidOperationException exception) { throw Conflict(exception.Message); }
        AddAudit(deck, actorUserId, AuditEventActions.SalesPresentationDeckActivated,
            "A processed presentation deck was activated for the meeting session.", correlationId);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new SalesPresentationConflictException(
                SalesPresentationProblemCodes.InvalidState,
                "Another deck was activated at the same time. Refresh and try again.");
        }
        return await MapDeckAsync(deck, cancellationToken);
    }

    public async Task<SalesPresentationDeckDto?> RetryAsync(
        Guid companyId, Guid actorUserId, Guid sessionId, Guid deckId,
        string? correlationId, CancellationToken cancellationToken)
    {
        await EnsureMemberAsync(companyId, actorUserId, cancellationToken);
        var deck = await MutableDeckAsync(companyId, sessionId, deckId, cancellationToken);
        if (deck is null) return null;
        try { deck.QueueRetry(UtcNow()); }
        catch (InvalidOperationException exception) { throw Conflict(exception.Message); }
        AddAudit(deck, actorUserId, AuditEventActions.SalesPresentationDeckRetryQueued,
            "A retryable presentation processing failure was queued for another attempt.", correlationId);
        await db.SaveChangesAsync(cancellationToken);
        return await MapDeckAsync(deck, cancellationToken);
    }

    public async Task<SalesMeetingBriefDto?> GetBriefAsync(
        Guid companyId, Guid sessionId, CancellationToken cancellationToken)
    {
        ValidateIds(companyId, sessionId);
        var deck = await db.SalesPresentationDecks.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.SessionId == sessionId && x.IsActive &&
                        x.Status == SalesPresentationDeckStatus.Processed)
            .SingleOrDefaultAsync(cancellationToken);
        if (deck is null || deck.BriefVersion < 1) return null;
        var artifacts = await db.SalesMeetingArtifacts.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.SessionId == sessionId && x.DeckId == deck.Id &&
                        x.SlideId == null && x.ArtifactVersion == deck.BriefVersion)
            .OrderBy(x => x.Section).ThenBy(x => x.Order).ToListAsync(cancellationToken);
        var items = artifacts.Select(MapArtifact).ToArray();
        return new SalesMeetingBriefDto(sessionId, deck.Id, deck.BriefVersion,
            artifacts.Any(x => x.Classification != SalesMeetingArtifactClassification.ConfirmedFact), items);
    }

    public async Task<SalesPresentationDeckDto?> RequestBriefRegenerationAsync(
        Guid companyId, Guid actorUserId, Guid sessionId,
        string? correlationId, CancellationToken cancellationToken)
    {
        await EnsureMemberAsync(companyId, actorUserId, cancellationToken);
        var deck = await db.SalesPresentationDecks.SingleOrDefaultAsync(
            x => x.CompanyId == companyId && x.SessionId == sessionId && x.IsActive, cancellationToken);
        if (deck is null) return null;
        try { deck.RequestBriefRegeneration(UtcNow()); }
        catch (InvalidOperationException exception) { throw Conflict(exception.Message); }
        AddAudit(deck, actorUserId, AuditEventActions.SalesMeetingBriefRegenerationQueued,
            "A new version of Alex's pre-meeting brief was queued.", correlationId);
        await db.SaveChangesAsync(cancellationToken);
        return await MapDeckAsync(deck, cancellationToken);
    }

    private async Task<SalesPresentationDeckDto> MapDeckAsync(
        SalesPresentationDeck deck, CancellationToken cancellationToken)
    {
        var slides = await db.SalesPresentationSlides.AsNoTracking()
            .Where(x => x.CompanyId == deck.CompanyId && x.DeckId == deck.Id &&
                        x.ProcessingVersion == deck.ProcessingVersion)
            .OrderBy(x => x.SlideNumber).ToListAsync(cancellationToken);
        var mapped = new List<SalesPresentationSlideDto>(slides.Count);
        foreach (var slide in slides) mapped.Add(await MapSlideAsync(slide, cancellationToken));
        return new SalesPresentationDeckDto(
            deck.Id, deck.CompanyId, deck.SessionId, deck.AgentId, deck.Version, deck.Title,
            deck.OriginalFileName, deck.ContentType, deck.FileSizeBytes, deck.ContentHash,
            deck.StorageKey, deck.StorageUrl, deck.Status.ToStorageValue(), deck.ProcessingVersion,
            deck.ProcessingAttemptCount, deck.SlideCount, deck.RendererName, deck.RendererVersion,
            deck.AnimationHandling, deck.FailureCode, deck.FailureSummary, deck.CanRetry,
            deck.IsActive, deck.BriefVersion, deck.BriefRegenerationRequestedUtc.HasValue,
            deck.UploadedByUserId, deck.CreatedUtc, deck.UpdatedUtc, deck.ProcessingStartedUtc,
            deck.ProcessedUtc, deck.FailedUtc, deck.ActivatedUtc, deck.ConcurrencyVersion, mapped);
    }

    private async Task<SalesPresentationSlideDto> MapSlideAsync(
        SalesPresentationSlide slide, CancellationToken cancellationToken)
    {
        var artifacts = await db.SalesMeetingArtifacts.AsNoTracking()
            .Where(x => x.CompanyId == slide.CompanyId && x.SlideId == slide.Id &&
                        x.ArtifactVersion == slide.ProcessingVersion)
            .OrderBy(x => x.Order).ToListAsync(cancellationToken);
        return new SalesPresentationSlideDto(
            slide.Id, slide.ProcessingVersion, slide.SlideNumber, slide.Title, slide.ExtractedText,
            slide.SpeakerNotes, slide.ImageStorageKey, slide.ImageStorageUrl,
            slide.ImageWidthPixels, slide.ImageHeightPixels, slide.SourceWidthEmus,
            slide.SourceHeightEmus, slide.ContentHash, slide.Objective,
            slide.ExpectedDurationSeconds, slide.TransitionText, slide.Status.ToStorageValue(),
            artifacts.Select(MapArtifact).ToArray());
    }

    internal static SalesPresentationArtifactDto MapArtifact(SalesMeetingArtifact value) => new(
        value.Id, value.SlideId, value.ArtifactVersion, value.ArtifactType.ToStorageValue(),
        value.Section, value.Order, value.Content, value.Classification.ToStorageValue(),
        value.SourceId, value.AiRunId, value.CreatedUtc);

    private async Task EnsureSessionAndAgentAsync(Guid companyId, Guid sessionId, Guid agentId, CancellationToken ct)
    {
        if (!await db.SalesMeetingSessions.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id == sessionId, ct))
            throw new KeyNotFoundException("Sales meeting session not found.");
        var agent = await db.Agents.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == agentId, ct);
        if (agent is null || agent.Status != AgentStatus.Active ||
            !string.Equals(agent.Department, "Sales", StringComparison.OrdinalIgnoreCase))
            throw Validation("AgentId", "Choose an active Sales agent for this meeting deck.");
    }

    private async Task EnsureMemberAsync(Guid companyId, Guid userId, CancellationToken ct)
    {
        if (!await db.CompanyMemberships.AsNoTracking().AnyAsync(x =>
                x.CompanyId == companyId && x.UserId == userId && x.Status == CompanyMembershipStatus.Active, ct))
            throw new UnauthorizedAccessException("An active company membership is required.");
    }

    private Task<SalesPresentationDeck?> MutableDeckAsync(Guid companyId, Guid sessionId, Guid deckId, CancellationToken ct) =>
        db.SalesPresentationDecks.SingleOrDefaultAsync(
            x => x.CompanyId == companyId && x.SessionId == sessionId && x.Id == deckId, ct);

    private void AddAudit(SalesPresentationDeck deck, Guid actorUserId, string action, string rationale, string? correlationId)
    {
        db.AuditEvents.Add(new AuditEvent(
            Guid.NewGuid(), deck.CompanyId, AuditActorTypes.User, actorUserId,
            action, "sales_presentation_deck", deck.Id.ToString("D"), AuditEventOutcomes.Succeeded,
            rationale, ["sales meeting session", "presentation deck"],
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["sessionId"] = deck.SessionId.ToString("D"),
                ["contentHash"] = deck.ContentHash,
                ["deckVersion"] = deck.Version.ToString(),
                ["processingVersion"] = deck.ProcessingVersion.ToString(),
                ["status"] = deck.Status.ToStorageValue()
            }, correlationId, UtcNow()));
    }

    private static async Task<MemoryStream> ReadBoundedAsync(Stream source, long maximumBytes, CancellationToken ct)
    {
        var result = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(buffer, ct);
            if (read == 0) break;
            if (result.Length + read > maximumBytes)
            {
                await result.DisposeAsync();
                throw Validation("File", $"The PowerPoint file exceeds the {maximumBytes}-byte upload limit.");
            }
            await result.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        if (result.Length == 0)
        {
            await result.DisposeAsync();
            throw Validation("File", "The PowerPoint file is empty.");
        }
        result.Position = 0;
        return result;
    }

    private static void ValidatePptxPackage(Stream content)
    {
        try
        {
            content.Position = 0;
            Span<byte> header = stackalloc byte[4];
            if (content.Read(header) != 4 || header[0] != (byte)'P' || header[1] != (byte)'K')
                throw Validation("File", "The file content is not an unencrypted PowerPoint .pptx package.");
            content.Position = 0;
            using var archive = new System.IO.Compression.ZipArchive(content, System.IO.Compression.ZipArchiveMode.Read, true);
            var types = archive.GetEntry("[Content_Types].xml");
            var presentation = archive.GetEntry("ppt/presentation.xml");
            if (types is null || presentation is null)
                throw Validation("File", "The file content is not a valid PowerPoint presentation.");
        }
        catch (InvalidDataException)
        {
            throw Validation("File", "The PowerPoint package is malformed or encrypted.");
        }
        finally
        {
            if (content.CanSeek) content.Position = 0;
        }
    }

    private static bool IsSupportedContentType(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Split(';')[0].Trim().ToLowerInvariant() is
            "application/vnd.openxmlformats-officedocument.presentationml.presentation" or
            "application/octet-stream" or "application/zip";

    private static string? NormalizeContentType(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Split(';')[0].Trim().ToLowerInvariant();

    private static void ValidateIds(params Guid[] ids)
    {
        if (ids.Any(x => x == Guid.Empty)) throw new ArgumentException("Company, user, session, deck, and agent identifiers must not be empty.");
    }

    private static SalesPresentationValidationException Validation(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { [field] = [message] });

    private static SalesPresentationConflictException Conflict(string message) =>
        new(SalesPresentationProblemCodes.InvalidState, message);

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}
