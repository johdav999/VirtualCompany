using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesNarrationService(VirtualCompanyDbContext db, IApprovedSpeechGateway speech,
    ICompanyDocumentStorage storage, TimeProvider clock, IOptions<SalesNarrationOptions> options) : ISalesNarrationService
{
    public static string Hash(string value) => Hash(Encoding.UTF8.GetBytes(value));
    public static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    public static string Audience(SalesMeetingSession session) => Hash(JsonSerializer.Serialize(new {
        session.CustomerCompanyId, session.LeadId, session.ContactId, session.IntendedAudience
    }));
    public static string AssetKey(Guid company, string sourceHash, string scriptHash, string language,
        string voice, string configuration, string audience) =>
        Hash(JsonSerializer.Serialize(new { company, sourceHash, scriptHash, language, voice, configuration, audience }));

    public async Task<SalesNarrationWorkspace> GetAsync(Guid companyId, Guid userId, Guid sessionId, CancellationToken ct)
    {
        await AuthorizeAsync(companyId, userId, sessionId, ct);
        var revisions = await db.SalesNarrationRevisions.AsNoTracking().Where(x => x.CompanyId == companyId && x.SessionId == sessionId)
            .OrderByDescending(x => x.CreatedUtc).Take(50).ToListAsync(ct);
        var result = new List<SalesNarrationRevisionDto>();
        foreach (var revision in revisions) result.Add(await MapAsync(revision, ct));
        var profile = await speech.GetProfileAsync(ct);
        return new(profile with { Available = profile.Available && options.Value.CanGenerate }, result);
    }

    public async Task<SalesNarrationRevisionDto> PrepareAsync(Guid companyId, Guid userId, Guid sessionId,
        PrepareSalesNarration command, CancellationToken ct)
    {
        var session = await AuthorizeAsync(companyId, userId, sessionId, ct);
        if (command.Language is not ("en" or "sv")) throw new SalesNarrationException("Choose English or Swedish.", 400);
        var deck = await db.SalesPresentationDecks.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId &&
            x.SessionId == sessionId && x.IsActive && x.Status == SalesPresentationDeckStatus.Processed, ct)
            ?? throw new SalesNarrationException("Activate a processed presentation before preparing narration.");
        var slides = await db.SalesPresentationSlides.AsNoTracking().Where(x => x.CompanyId == companyId &&
            x.DeckId == deck.Id && x.ProcessingVersion == deck.ProcessingVersion).OrderBy(x => x.SlideNumber).ToListAsync(ct);
        var scripts = command.Scripts?.ToArray() ?? slides.SelectMany(x =>
            x.ExtractedText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select((text, i) => new SalesNarrationScript(x.SlideNumber, i + 1, text))).ToArray();
        if (scripts.Length is < 1 or > 100 || scripts.Any(x => string.IsNullOrWhiteSpace(x.Text) || x.Text.Length > 3000 ||
            x.TalkingPoint < 1 || !slides.Any(s => s.SlideNumber == x.SlideNumber)) ||
            scripts.Select(x => (x.SlideNumber, x.TalkingPoint)).Distinct().Count() != scripts.Length ||
            slides.Any(x => !scripts.Any(s => s.SlideNumber == x.SlideNumber)))
            throw new SalesNarrationException("Supply 1–100 talking points, at most 3,000 characters each, covering every slide. Split long source text into shorter talking points.", 400);
        scripts = scripts.OrderBy(x => x.SlideNumber).ThenBy(x => x.TalkingPoint).ToArray();
        var profile = await speech.GetProfileAsync(ct);
        var audience = Audience(session);
        var manifest = Hash(JsonSerializer.Serialize(new { deck.Id, deck.Version, deck.ProcessingVersion,
            audience, command.Language, profile.Voice, profile.ConfigurationVersion,
            scripts, sources = slides.Select(x => new { x.Id, x.ContentHash }) }));
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var existing = await db.SalesNarrationRevisions.SingleOrDefaultAsync(x => x.CompanyId == companyId &&
            x.SessionId == sessionId && x.ManifestHash == manifest, ct);
        if (existing is not null) return await MapAsync(existing, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var revision = new SalesNarrationRevision {
            Id = Guid.NewGuid(), CompanyId = companyId, SessionId = sessionId, DeckId = deck.Id,
            DeckVersion = deck.Version, ProcessingVersion = deck.ProcessingVersion, AudienceId = session.CustomerCompanyId,
            AudienceHash = audience, ManifestHash = manifest, Language = command.Language, Voice = profile.Voice,
            Model = profile.Model, ConfigurationVersion = profile.ConfigurationVersion,
            CreatedByUserId = userId, CreatedUtc = now, RetainUntilUtc = now.AddDays(90)
        };
        db.SalesNarrationRevisions.Add(revision);
        foreach (var script in scripts)
        {
            var slide = slides.Single(x => x.SlideNumber == script.SlideNumber);
            var sourceHash = Hash(slide.ContentHash + "|" + slide.ExtractedText);
            var scriptHash = Hash(script.Text);
            var key = AssetKey(companyId, sourceHash, scriptHash, command.Language, profile.Voice, profile.ConfigurationVersion, audience);
            var asset = db.SalesNarrationAssets.Local.FirstOrDefault(x => x.CompanyId == companyId && x.CacheKey == key)
                ?? await db.SalesNarrationAssets.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.CacheKey == key, ct);
            SalesRoomBenchmarkTelemetry.RecordCache(asset?.Status == SalesNarrationAsset.Ready);
            if (asset?.FailureCode == "retention_deletion") throw new SalesNarrationException("Audio cleanup is in progress. Retry preparation shortly.");
            if (asset is null)
            {
                asset = new() { Id = Guid.NewGuid(), CompanyId = companyId, CacheKey = key, CreatedUtc = now, UpdatedUtc = now };
                db.SalesNarrationAssets.Add(asset);
            }
            db.SalesNarrationSegments.Add(new() { Id = Guid.NewGuid(), CompanyId = companyId, RevisionId = revision.Id,
                SourceSlideId = slide.Id, SlideNumber = slide.SlideNumber, TalkingPoint = script.TalkingPoint,
                SourceHash = sourceHash, SourceText = slide.ExtractedText, Script = script.Text, ScriptHash = scriptHash,
                AssetId = asset.Id, Reused = asset.Status == SalesNarrationAsset.Ready });
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await MapAsync(revision, ct);
    }

    public async Task DecideAsync(Guid companyId, Guid userId, Guid revisionId, string action, SalesNarrationDecision command, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var revision = await db.SalesNarrationRevisions.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == revisionId, ct)
            ?? throw new KeyNotFoundException();
        await AuthorizeAsync(companyId, userId, revision.SessionId, ct);
        if (revision.Version != command.ExpectedVersion) throw new SalesNarrationException("This revision changed. Reload before deciding.");
        var now = clock.GetUtcNow().UtcDateTime;
        if (action == "revoke")
        {
            revision.RevokedByUserId = userId;
            revision.RevokedUtc ??= now;
        }
        else if (action is "approve" or "retry")
        {
            if (revision.RevokedUtc.HasValue || revision.RetainUntilUtc <= now)
                throw new SalesNarrationException("This release is revoked or expired. Prepare a new content revision.");
            await EnsureCurrentAsync(revision, ct);
            var profile = await speech.GetProfileAsync(ct);
            if (!options.Value.CanGenerate) throw new SalesNarrationException("Narration generation is disabled or its dated rate limits are missing. Ask an administrator to configure generation before approval.");
            if (!profile.Available || profile.ConfigurationVersion != revision.ConfigurationVersion)
                throw new SalesNarrationException("Speech is unavailable or its configuration changed. Configure speech and prepare a new revision.");
            if (action == "approve")
            {
                revision.ApprovedByUserId ??= userId;
                revision.ApprovedUtc ??= now;
            }
            else
            {
                if (!revision.ApprovedUtc.HasValue || !command.AcknowledgeAdditionalCost)
                    throw new SalesNarrationException("Approve the script and acknowledge that retrying may incur another generation charge.");
                var assets = await (from s in db.SalesNarrationSegments
                    join a in db.SalesNarrationAssets on new { s.CompanyId, Id = s.AssetId } equals new { a.CompanyId, a.Id }
                    where s.CompanyId == companyId && s.RevisionId == revisionId select a).Distinct().ToListAsync(ct);
                foreach (var asset in assets)
                {
                    if (asset.Status is SalesNarrationAsset.Pending or SalesNarrationAsset.Generating) continue;
                    if (asset.Status == SalesNarrationAsset.Ready && await StoredAsync(asset, ct)) continue;
                    if (asset.FailureCode == "retention_deletion") throw new SalesNarrationException("Audio cleanup is in progress. Retry shortly.");
                    if (asset.AttemptCount >= 3) throw new SalesNarrationException("Three attempts have been used. Review the script or speech configuration before creating a new revision.");
                    asset.Status = SalesNarrationAsset.Pending; asset.FailureCode = null; asset.Version++; asset.UpdatedUtc = now;
                }
            }
        }
        else throw new SalesNarrationException("Unknown narration action.", 400);
        revision.Version++;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<SalesNarrationPreview> PreviewAsync(Guid companyId, Guid userId, SalesNarrationPlaybackRequest request, CancellationToken ct)
    {
        var playback = await OpenPlaybackAsync(companyId, userId, request, ct);
        return new(Wave(playback.Pcm), "audio/wav");
    }

    public async Task<SalesNarrationPlayback> OpenPlaybackAsync(Guid companyId, Guid userId, SalesNarrationPlaybackRequest request, CancellationToken ct)
    {
        if (request.OffsetMilliseconds < 0 || request.TurnGeneration < 1) throw new SalesNarrationException("Invalid playback position or generation.", 400);
        var session = await AuthorizeAsync(companyId, userId, request.SessionId, ct);
        var revision = await db.SalesNarrationRevisions.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId &&
            x.Id == request.RevisionId && x.SessionId == request.SessionId, ct) ?? throw new KeyNotFoundException();
        if (request.AudienceId != session.CustomerCompanyId || revision.AudienceHash != Audience(session))
            throw new UnauthorizedAccessException("Narration is not approved for this audience.");
        await EnsureReleasedAsync(revision, ct);
        var segment = await db.SalesNarrationSegments.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId &&
            x.RevisionId == revision.Id && x.Id == request.SegmentId, ct) ?? throw new KeyNotFoundException();
        var asset = await db.SalesNarrationAssets.SingleAsync(x => x.CompanyId == companyId && x.Id == segment.AssetId, ct);
        if (asset.Status != SalesNarrationAsset.Ready || asset.StorageKey is null || request.OffsetMilliseconds >= asset.DurationMilliseconds)
            throw new SalesNarrationException("This segment has no playable approved audio.");
        byte[] wav;
        try
        {
            await using var stream = await storage.OpenReadAsync(asset.StorageKey, ct);
            using var buffer = new MemoryStream();
            var chunk = new byte[16384];
            int count;
            while ((count = await stream.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + count > 5_760_044) throw new IOException("Invalid stored audio size.");
                buffer.Write(chunk, 0, count);
            }
            wav = buffer.ToArray();
            if (wav.Length != asset.Bytes || Hash(wav) != asset.AudioHash || wav.Length < 46)
                throw new IOException("Audio integrity check failed.");
        }
        catch (Exception e) when (e is IOException or KeyNotFoundException)
        {
            asset.Status = SalesNarrationAsset.NeedsReview; asset.FailureCode = "asset_missing_or_corrupt"; asset.Version++;
            await db.SaveChangesAsync(ct);
            throw new SalesNarrationException("The stored audio is missing or invalid. Review and retry generation.");
        }
        // Recheck release after object I/O; a revoked release never returns a new preview.
        db.ChangeTracker.Clear();
        revision = await db.SalesNarrationRevisions.AsNoTracking().SingleAsync(x => x.CompanyId == companyId && x.Id == revision.Id, ct);
        await EnsureReleasedAsync(revision, ct);
        await AuthorizeAsync(companyId, userId, request.SessionId, ct);
        var offset = checked(request.OffsetMilliseconds * 48);
        var data = wav.AsSpan(44 + offset).ToArray();
        await db.SalesNarrationAssets.Where(x => x.CompanyId == companyId && x.Id == asset.Id &&
            x.Status == SalesNarrationAsset.Ready).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.PreviewCount, x => x.PreviewCount + 1)
                .SetProperty(x => x.ReusedMilliseconds, x => x.ReusedMilliseconds + asset.DurationMilliseconds - request.OffsetMilliseconds), ct);
        return new(asset.Id, segment.SlideNumber, segment.TalkingPoint, request.OffsetMilliseconds,
            request.TurnGeneration, data, 24000);
    }

    internal async Task EnsureReleasedAsync(SalesNarrationRevision revision, CancellationToken ct)
    {
        if (!revision.ApprovedUtc.HasValue || revision.RevokedUtc.HasValue || revision.RetainUntilUtc <= clock.GetUtcNow().UtcDateTime)
            throw new SalesNarrationException("This narration is not approved for release.");
        await EnsureCurrentAsync(revision, ct);
    }

    internal async Task EnsureCurrentAsync(SalesNarrationRevision revision, CancellationToken ct)
    {
        var session = await db.SalesMeetingSessions.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == revision.CompanyId && x.Id == revision.SessionId, ct);
        if (session is null || session.CustomerCompanyId != revision.AudienceId || Audience(session) != revision.AudienceHash)
            throw new SalesNarrationException("The intended audience changed. Prepare a new revision.");
        if (!await db.SalesPresentationDecks.AsNoTracking().AnyAsync(x => x.CompanyId == revision.CompanyId &&
            x.Id == revision.DeckId && x.IsActive && x.Version == revision.DeckVersion &&
            x.ProcessingVersion == revision.ProcessingVersion && x.Status == SalesPresentationDeckStatus.Processed, ct))
            throw new SalesNarrationException("The presentation changed. Prepare and approve its current revision.");
        var segments = await db.SalesNarrationSegments.AsNoTracking().Where(x => x.CompanyId == revision.CompanyId && x.RevisionId == revision.Id).ToListAsync(ct);
        var slides = await db.SalesPresentationSlides.AsNoTracking().Where(x => x.CompanyId == revision.CompanyId &&
            x.DeckId == revision.DeckId && x.ProcessingVersion == revision.ProcessingVersion).ToListAsync(ct);
        if (segments.Count == 0 || segments.Any(s => !slides.Any(x => x.Id == s.SourceSlideId &&
            Hash(x.ContentHash + "|" + x.ExtractedText) == s.SourceHash)))
            throw new SalesNarrationException("Source evidence changed. Prepare a new revision.");
    }

    private async Task<SalesMeetingSession> AuthorizeAsync(Guid companyId, Guid userId, Guid sessionId, CancellationToken ct)
    {
        if (!await db.CompanyMemberships.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.UserId == userId &&
            x.Status == CompanyMembershipStatus.Active, ct)) throw new UnauthorizedAccessException("Active company membership is required.");
        var session = await db.SalesMeetingSessions.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == sessionId, ct)
            ?? throw new KeyNotFoundException();
        if (session.CreatedByUserId != userId) throw new UnauthorizedAccessException("Only the meeting organizer can prepare or preview narration.");
        return session;
    }

    private async Task<SalesNarrationRevisionDto> MapAsync(SalesNarrationRevision revision, CancellationToken ct)
    {
        var parts = await (from s in db.SalesNarrationSegments.AsNoTracking()
            join a in db.SalesNarrationAssets.AsNoTracking() on new { s.CompanyId, Id = s.AssetId } equals new { a.CompanyId, a.Id }
            where s.CompanyId == revision.CompanyId && s.RevisionId == revision.Id orderby s.SlideNumber, s.TalkingPoint
            select new { s, a }).ToListAsync(ct);
        var missing = new HashSet<Guid>();
        foreach (var asset in parts.Select(x => x.a).DistinctBy(x => x.Id).Where(x => x.Status == SalesNarrationAsset.Ready))
            if (!await StoredAsync(asset, ct)) missing.Add(asset.Id);
        var ids = parts.Select(x => x.a.Id).Distinct().ToArray();
        var attempts = await db.SalesNarrationAttempts.AsNoTracking().Where(x => x.CompanyId == revision.CompanyId && ids.Contains(x.AssetId)).ToListAsync(ct);
        var status = revision.RevokedUtc.HasValue ? "revoked" : revision.RetainUntilUtc <= clock.GetUtcNow().UtcDateTime ? "expired" :
            !revision.ApprovedUtc.HasValue ? "draft" : parts.Count > 0 && parts.All(x => x.a.Status == SalesNarrationAsset.Ready) && missing.Count == 0 ? "ready" : "not_ready";
        try { await EnsureCurrentAsync(revision, ct); } catch (SalesNarrationException) { if (status != "revoked") status = "outdated"; }
        return new(revision.Id, revision.DeckId, revision.DeckVersion, revision.Language, revision.Voice, revision.Model,
            revision.AudienceId, status, revision.Version, revision.CreatedUtc, revision.ApprovedUtc,
            parts.Select(x => new SalesNarrationSegmentDto(x.s.Id, x.s.SlideNumber, x.s.TalkingPoint, x.s.SourceText,
                x.s.Script, missing.Contains(x.a.Id) ? SalesNarrationAsset.NeedsReview : x.a.Status, x.s.Reused, x.a.DurationMilliseconds, x.a.Bytes, x.a.AttemptCount, missing.Contains(x.a.Id) ? "asset_missing_or_corrupt" : x.a.FailureCode)).ToArray(),
            attempts.Sum(x => x.InputTokens ?? 0), attempts.Sum(x => x.OutputTokens ?? 0), attempts.Count(x => !x.InputTokens.HasValue),
            attempts.Sum(x => (double)x.GeneratedMilliseconds) / 60000,
            parts.DistinctBy(x => x.a.Id).Sum(x => (double)x.a.ReusedMilliseconds) / 60000,
            attempts.Count > 0 && attempts.All(x => x.EstimatedCostUsd.HasValue) ? attempts.Sum(x => x.EstimatedCostUsd) : null);
    }

    private async Task<bool> StoredAsync(SalesNarrationAsset asset, CancellationToken ct)
    {
        if (asset.StorageKey is null) return false;
        try
        {
            await using var stream = await storage.OpenReadAsync(asset.StorageKey, ct);
            return stream.CanSeek ? stream.Length == asset.Bytes : await stream.ReadAsync(new byte[1], ct) == 1;
        }
        catch (Exception e) when (e is IOException or KeyNotFoundException) { return false; }
    }

    public static byte[] Wave(byte[] pcm)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer, Encoding.ASCII, true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + pcm.Length); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(24000); writer.Write(48000);
        writer.Write((short)2); writer.Write((short)16); writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(pcm.Length); writer.Write(pcm);
        return buffer.ToArray();
    }
}




