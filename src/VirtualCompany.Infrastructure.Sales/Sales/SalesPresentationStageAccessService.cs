using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

internal sealed class SalesPresentationStageAccessService(
    VirtualCompanyDbContext db,
    ICompanyDocumentStorage storage,
    IDataProtectionProvider dataProtection,
    IOptions<TeamsPresenterOptions> configured,
    IAuditEventWriter audit,
    TimeProvider timeProvider) : ISalesPresentationStageAccessService
{
    private const string Purpose = "VirtualCompany.Sales.TeamsStage.v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ITimeLimitedDataProtector _protector = dataProtection
        .CreateProtector(Purpose)
        .ToTimeLimitedDataProtector();

    public async Task<SalesPresentationStageAccessGrantDto> IssueAsync(
        Guid companyId, Guid organizerUserId, Guid sessionId, CancellationToken cancellationToken)
    {
        ValidateIds(companyId, organizerUserId, sessionId);
        var options = configured.Value;
        if (!options.Enabled || !options.SharedStageEnabled)
            throw new SalesPresentationStageAccessException(SalesPresentationStageAccessProblemCodes.Disabled,
                "Teams shared-stage presentation is disabled. The browser presentation remains available.");

        var isMember = await db.CompanyMemberships.AsNoTracking().AnyAsync(x =>
            x.CompanyId == companyId && x.UserId == organizerUserId &&
            x.Status == CompanyMembershipStatus.Active, cancellationToken);
        var session = await db.SalesMeetingSessions.AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId && x.Id == sessionId, cancellationToken);
        if (!isMember || session is null)
            throw new SalesPresentationStageAccessException(SalesPresentationStageAccessProblemCodes.InvalidGrant,
                "The meeting stage could not be authorized.");

        var isOrganizer = await db.TeamsMeetingCalls.AsNoTracking().AnyAsync(x =>
            x.CompanyId == companyId && x.MeetingSessionId == sessionId &&
            x.OrganizerUserId == organizerUserId, cancellationToken);
        if (!isOrganizer)
            throw new SalesPresentationStageAccessException(SalesPresentationStageAccessProblemCodes.OrganizerRequired,
                "Only the verified Teams meeting organizer can share this presentation to the meeting stage.");

        var deck = await ActiveDeckAsync(companyId, sessionId, cancellationToken)
            ?? throw new SalesPresentationStageAccessException(SalesPresentationStageAccessProblemCodes.InvalidGrant,
                "An active processed presentation is required before sharing to the meeting stage.");
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (session.EndedUtc.HasValue || session.RetentionUntilUtc <= now)
            throw new SalesPresentationStageAccessException(SalesPresentationStageAccessProblemCodes.ExpiredGrant,
                "This meeting is no longer available for stage sharing.");

        var requestedExpiry = now.AddMinutes(Math.Clamp(options.StageAccessMinutes, 5, 720));
        var expiresUtc = requestedExpiry < session.RetentionUntilUtc ? requestedExpiry : session.RetentionUntilUtc;
        var payload = new GrantPayload(companyId, sessionId, deck.Id, deck.Version, organizerUserId, now);
        var token = _protector.Protect(JsonSerializer.Serialize(payload, Json), expiresUtc - now);

        await audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.User, organizerUserId,
            "sales.presentation.stage_access_issued", "sales_meeting_session", sessionId.ToString("D"),
            AuditEventOutcomes.Succeeded, "A bounded, stage-safe meeting presentation grant was issued.",
            ["sales meeting session", "active presentation deck"],
            new Dictionary<string, string?>
            {
                ["deckId"] = deck.Id.ToString("D"), ["deckVersion"] = deck.Version.ToString(),
                ["expiresUtc"] = expiresUtc.ToString("O")
            }), cancellationToken);

        return new SalesPresentationStageAccessGrantDto(token, sessionId, deck.Id, deck.Version, expiresUtc,
            $"/teams/meetings/{sessionId:D}/stage");
    }

    public async Task<SalesPresentationStageAccessContext> ValidateAsync(
        Guid sessionId, string accessToken, CancellationToken cancellationToken)
    {
        if (!configured.Value.Enabled || !configured.Value.SharedStageEnabled)
            throw new SalesPresentationStageAccessException(SalesPresentationStageAccessProblemCodes.Disabled,
                "Teams shared-stage presentation is disabled.");
        if (sessionId == Guid.Empty || string.IsNullOrWhiteSpace(accessToken) || accessToken.Length > 8192)
            throw Invalid();

        GrantPayload? payload;
        DateTimeOffset expiration;
        try
        {
            payload = JsonSerializer.Deserialize<GrantPayload>(_protector.Unprotect(accessToken, out expiration), Json);
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            throw Invalid();
        }

        if (payload is null || payload.CompanyId == Guid.Empty || payload.SessionId != sessionId ||
            payload.DeckId == Guid.Empty || payload.IssuedByUserId == Guid.Empty)
            throw Invalid();

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (expiration.UtcDateTime <= now)
            throw new SalesPresentationStageAccessException(SalesPresentationStageAccessProblemCodes.ExpiredGrant,
                "The meeting-stage grant expired. Ask the organizer to share the presentation again.");

        var session = await db.SalesMeetingSessions.AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == payload.CompanyId && x.Id == sessionId, cancellationToken);
        var organizerStillAuthorized = await db.CompanyMemberships.AsNoTracking().AnyAsync(x =>
            x.CompanyId == payload.CompanyId && x.UserId == payload.IssuedByUserId &&
            x.Status == CompanyMembershipStatus.Active, cancellationToken);
        var organizerStillOwnsMeeting = await db.TeamsMeetingCalls.AsNoTracking().AnyAsync(x =>
            x.CompanyId == payload.CompanyId && x.MeetingSessionId == sessionId &&
            x.OrganizerUserId == payload.IssuedByUserId, cancellationToken);
        if (session is null || !organizerStillAuthorized || !organizerStillOwnsMeeting ||
            session.EndedUtc.HasValue || session.RetentionUntilUtc <= now)
            throw new SalesPresentationStageAccessException(SalesPresentationStageAccessProblemCodes.ExpiredGrant,
                "The meeting-stage grant is no longer active.");

        var deck = await ActiveDeckAsync(payload.CompanyId, sessionId, cancellationToken);
        if (deck is null || deck.Id != payload.DeckId || deck.Version != payload.DeckVersion)
            throw new SalesPresentationStageAccessException(SalesPresentationStageAccessProblemCodes.DeckChanged,
                "The active presentation changed. Ask the organizer to share the stage again.");

        return new(payload.CompanyId, sessionId, deck.Id, deck.Version, payload.IssuedByUserId,
            expiration.UtcDateTime);
    }

    public async Task<SalesPresentationStageSnapshotDto> GetSnapshotAsync(
        Guid sessionId, string accessToken, CancellationToken cancellationToken)
    {
        var access = await ValidateAsync(sessionId, accessToken, cancellationToken);
        var runtime = await BuildStageSnapshotAsync(access, cancellationToken);
        return runtime;
    }

    public async Task<SalesPresentationSlideAsset> OpenSlideAsync(Guid sessionId, string accessToken,
        Guid deckId, int deckVersion, int slideNumber, CancellationToken cancellationToken)
    {
        var access = await ValidateAsync(sessionId, accessToken, cancellationToken);
        if (access.DeckId != deckId || access.DeckVersion != deckVersion)
            throw new SalesPresentationStageAccessException(SalesPresentationStageAccessProblemCodes.DeckChanged,
                "The requested slide does not belong to the granted presentation version.");

        var session = await db.SalesMeetingSessions.AsNoTracking().SingleAsync(x =>
            x.CompanyId == access.CompanyId && x.Id == sessionId, cancellationToken);
        var current = Math.Max(1, session.CurrentSlideIndex);
        if (slideNumber != current && slideNumber != current + 1)
            throw new SalesPresentationStageAccessException(SalesPresentationStageAccessProblemCodes.SlideNotAllowed,
                "Only the current and next authorized slide may be loaded by the meeting stage.");

        var deck = await ActiveDeckAsync(access.CompanyId, sessionId, cancellationToken)
            ?? throw Invalid();
        var slide = await db.SalesPresentationSlides.AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == access.CompanyId && x.DeckId == deckId &&
            x.ProcessingVersion == deck.ProcessingVersion && x.SlideNumber == slideNumber,
            cancellationToken) ?? throw new SalesPresentationStageAccessException(
                SalesPresentationStageAccessProblemCodes.SlideNotAllowed, "The authorized slide asset was not found.");
        var content = await storage.OpenReadAsync(slide.ImageStorageKey, cancellationToken);
        return new(content, ContentType(slide.ImageStorageKey), slide.ContentHash,
            slide.ImageWidthPixels, slide.ImageHeightPixels);
    }

    private async Task<SalesPresentationStageSnapshotDto> BuildStageSnapshotAsync(
        SalesPresentationStageAccessContext access, CancellationToken cancellationToken)
    {
        var session = await db.SalesMeetingSessions.AsNoTracking().SingleAsync(x =>
            x.CompanyId == access.CompanyId && x.Id == access.SessionId, cancellationToken);
        var deck = await ActiveDeckAsync(access.CompanyId, access.SessionId, cancellationToken)
            ?? throw Invalid();
        var slideNumber = Math.Clamp(session.CurrentSlideIndex < 1 ? 1 : session.CurrentSlideIndex, 1, deck.SlideCount);
        var slide = await db.SalesPresentationSlides.AsNoTracking().SingleAsync(x =>
            x.CompanyId == access.CompanyId && x.DeckId == deck.Id &&
            x.ProcessingVersion == deck.ProcessingVersion && x.SlideNumber == slideNumber, cancellationToken);
        return new(session.Id, session.Status.ToStorageValue(), session.LastPresentationSequence,
            session.ConcurrencyVersion, deck.Id, deck.Version, slide.SlideNumber, deck.SlideCount,
            slide.Title, slide.ExtractedText, null, slide.ImageWidthPixels, slide.ImageHeightPixels);
    }

    private Task<SalesPresentationDeck?> ActiveDeckAsync(Guid companyId, Guid sessionId, CancellationToken ct) =>
        db.SalesPresentationDecks.AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId && x.SessionId == sessionId && x.IsActive &&
            x.Status == SalesPresentationDeckStatus.Processed && x.SlideCount > 0, ct);

    private static SalesPresentationStageAccessException Invalid() =>
        new(SalesPresentationStageAccessProblemCodes.InvalidGrant, "The meeting-stage grant is invalid.");
    private static void ValidateIds(params Guid[] ids)
    {
        if (ids.Any(x => x == Guid.Empty)) throw new ArgumentException("Company, organizer, and meeting identifiers are required.");
    }
    private static string ContentType(string key) => Path.GetExtension(key).ToLowerInvariant() switch
    {
        ".svg" => "image/svg+xml", ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp", _ => "application/octet-stream"
    };

    private sealed record GrantPayload(Guid CompanyId, Guid SessionId, Guid DeckId, int DeckVersion,
        Guid IssuedByUserId, DateTime IssuedUtc);
}
