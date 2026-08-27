using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CompanyStatutoryProfileService : ICompanyStatutoryProfileService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly AccountingOperationsTelemetry _telemetry;
    private readonly TimeProvider _timeProvider;

    public CompanyStatutoryProfileService(
        VirtualCompanyDbContext dbContext,
        IAuditEventWriter auditEventWriter,
        AccountingOperationsTelemetry telemetry,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _auditEventWriter = auditEventWriter;
        _telemetry = telemetry;
        _timeProvider = timeProvider;
    }

    public async Task<CompanyStatutoryProfileStatusDto> GetAsync(
        GetCompanyStatutoryProfileQuery query,
        CancellationToken cancellationToken)
    {
        ValidateCompanyId(query.CompanyId);
        var profile = await _dbContext.CompanyStatutoryProfiles.AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == query.CompanyId, cancellationToken);
        return BuildStatus(query.CompanyId, profile);
    }

    public async Task<CompanyStatutoryProfileStatusDto> CreateAsync(
        CreateCompanyStatutoryProfileCommand command,
        CancellationToken cancellationToken)
    {
        ValidateCompanyId(command.CompanyId);
        ValidateActor(command.ActorUserId);
        if (await _dbContext.CompanyStatutoryProfiles.AnyAsync(
                profile => profile.CompanyId == command.CompanyId, cancellationToken))
        {
            throw AlreadyExists();
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        CompanyStatutoryProfile profile;
        try
        {
            profile = new CompanyStatutoryProfile(
                Guid.NewGuid(), command.CompanyId, MapValues(command.Profile), command.ActorUserId, nowUtc);
        }
        catch (ArgumentException exception)
        {
            throw InvalidProfile(exception);
        }

        _dbContext.CompanyStatutoryProfiles.Add(profile);
        await WriteAuditAsync(profile, command.ActorUserId, AuditEventActions.CompanyStatutoryProfileCreated,
            beforeHash: null, afterHash: ComputeEvidenceHash(profile), command.CorrelationId, nowUtc, cancellationToken);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConflict(exception))
        {
            throw AlreadyExists();
        }

        _telemetry.StatutoryProfileChanged(command.CompanyId, "created", profile.IsFormatComplete,
            profile.IsUserAttested, profile.VerificationStatus, command.CorrelationId);
        return BuildStatus(command.CompanyId, profile);
    }

    public async Task<CompanyStatutoryProfileStatusDto> UpdateAsync(
        UpdateCompanyStatutoryProfileCommand command,
        CancellationToken cancellationToken)
    {
        ValidateCompanyId(command.CompanyId);
        ValidateActor(command.ActorUserId);
        var profile = await _dbContext.CompanyStatutoryProfiles
            .SingleOrDefaultAsync(item => item.CompanyId == command.CompanyId, cancellationToken)
            ?? throw NotFound();
        if (profile.Version != command.ExpectedVersion)
        {
            throw ConcurrencyConflict();
        }

        var beforeHash = ComputeEvidenceHash(profile);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        try
        {
            profile.Update(MapValues(command.Profile), command.ActorUserId, nowUtc);
        }
        catch (ArgumentException exception)
        {
            throw InvalidProfile(exception);
        }

        await WriteAuditAsync(profile, command.ActorUserId, AuditEventActions.CompanyStatutoryProfileUpdated,
            beforeHash, ComputeEvidenceHash(profile), command.CorrelationId, nowUtc, cancellationToken);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw ConcurrencyConflict();
        }

        _telemetry.StatutoryProfileChanged(command.CompanyId, "updated", profile.IsFormatComplete,
            profile.IsUserAttested, profile.VerificationStatus, command.CorrelationId);
        return BuildStatus(command.CompanyId, profile);
    }

    internal static CompanyStatutoryProfileStatusDto BuildStatus(Guid companyId, CompanyStatutoryProfile? profile)
    {
        var missing = BuildMissingFacts(profile);
        if (profile is null)
        {
            return new CompanyStatutoryProfileStatusDto(
                companyId, false, false, false, false, false,
                "No Swedish statutory profile has been recorded. Nothing has been verified.",
                missing,
                ["Create the statutory profile and provide the missing legal facts."],
                null);
        }

        var externallyVerified = profile.VerificationStatus == StatutoryVerificationStatusValues.ExternallyVerified;
        var nextActions = new List<string>();
        if (!profile.IsFormatComplete)
            nextActions.Add("Complete the missing legal facts using the expected Swedish formats.");
        if (profile.IsFormatComplete && !profile.IsUserAttested)
            nextActions.Add("Review the legal facts and attest that they are accurate to the best of your knowledge.");
        if (!externallyVerified)
            nextActions.Add("External registry verification has not been recorded; do not treat this profile as government verified.");
        if (nextActions.Count == 0)
            nextActions.Add("Keep the source and verification evidence current when legal facts change.");

        return new CompanyStatutoryProfileStatusDto(
            companyId,
            true,
            profile.IsFormatComplete,
            profile.IsUserAttested,
            externallyVerified,
            missing.Count == 0,
            externallyVerified
                ? "External verification evidence is recorded for this profile version."
                : "The profile has not been externally verified. Format checks and user attestation are separate from government verification.",
            missing,
            nextActions,
            MapDto(profile));
    }

    internal static IReadOnlyList<string> BuildMissingFacts(CompanyStatutoryProfile? profile)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(profile?.LegalName)) missing.Add(StatutoryProfileFactKeys.LegalName);
        if (string.IsNullOrWhiteSpace(profile?.SwedishOrganisationNumber)) missing.Add(StatutoryProfileFactKeys.OrganisationNumber);
        if (profile?.HasCompleteRegisteredAddress != true) missing.Add(StatutoryProfileFactKeys.RegisteredAddress);
        if (!string.Equals(profile?.CountryCode, "SE", StringComparison.Ordinal)) missing.Add(StatutoryProfileFactKeys.CountryCode);
        if (!string.Equals(profile?.AccountingCurrency, "SEK", StringComparison.Ordinal)) missing.Add(StatutoryProfileFactKeys.AccountingCurrency);
        if (string.IsNullOrWhiteSpace(profile?.FiscalYearBasis)) missing.Add(StatutoryProfileFactKeys.FiscalYearBasis);
        if (string.IsNullOrWhiteSpace(profile?.BookkeepingMethod) || profile.BookkeepingMethod == StatutoryBookkeepingMethodValues.NotSpecified)
            missing.Add(StatutoryProfileFactKeys.BookkeepingMethod);
        if (profile?.OrganisationRegistrationEffectiveFrom is null) missing.Add(StatutoryProfileFactKeys.OrganisationRegistrationDate);
        if (profile?.VatRegistrationStatus == StatutoryVatRegistrationStatusValues.Registered)
        {
            if (string.IsNullOrWhiteSpace(profile.VatRegistrationNumber)) missing.Add(StatutoryProfileFactKeys.VatRegistrationNumber);
            if (profile.VatRegistrationEffectiveFrom is null) missing.Add(StatutoryProfileFactKeys.VatRegistrationDate);
        }
        if (profile?.IsUserAttested != true) missing.Add(StatutoryProfileFactKeys.UserAttestation);
        return missing;
    }

    private static CompanyStatutoryProfileValues MapValues(CompanyStatutoryProfileInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.RegisteredAddress);
        var correspondence = input.CorrespondenceAddress;
        return new CompanyStatutoryProfileValues(
            input.LegalName,
            input.SwedishOrganisationNumber,
            input.VatRegistrationNumber,
            input.VatRegistrationStatus,
            input.RegisteredAddress.AddressLine1,
            input.RegisteredAddress.AddressLine2,
            input.RegisteredAddress.PostalCode,
            input.RegisteredAddress.City,
            input.RegisteredAddress.CountryCode,
            correspondence?.AddressLine1,
            correspondence?.AddressLine2,
            correspondence?.PostalCode,
            correspondence?.City,
            correspondence?.CountryCode,
            input.CountryCode,
            input.AccountingCurrency,
            input.FiscalYearBasis,
            input.BookkeepingMethod,
            input.OrganisationRegistrationEffectiveFrom,
            input.VatRegistrationEffectiveFrom,
            input.VatRegistrationEffectiveTo,
            input.IsUserAttested,
            input.VerificationStatus,
            input.SourceKind,
            input.SourceReference,
            input.SourceCapturedUtc,
            input.ExternalVerifier,
            input.ExternallyVerifiedUtc);
    }

    private static CompanyStatutoryProfileDto MapDto(CompanyStatutoryProfile profile) => new(
        profile.Id,
        profile.CompanyId,
        profile.LegalName,
        profile.SwedishOrganisationNumber,
        profile.VatRegistrationNumber,
        profile.VatRegistrationStatus,
        new StatutoryAddressDto(profile.RegisteredAddressLine1, profile.RegisteredAddressLine2,
            profile.RegisteredPostalCode, profile.RegisteredCity, profile.RegisteredCountryCode),
        new StatutoryAddressDto(profile.CorrespondenceAddressLine1, profile.CorrespondenceAddressLine2,
            profile.CorrespondencePostalCode, profile.CorrespondenceCity, profile.CorrespondenceCountryCode),
        profile.CountryCode,
        profile.AccountingCurrency,
        profile.FiscalYearBasis,
        profile.BookkeepingMethod,
        profile.OrganisationRegistrationEffectiveFrom,
        profile.VatRegistrationEffectiveFrom,
        profile.VatRegistrationEffectiveTo,
        profile.IsFormatComplete,
        profile.IsUserAttested,
        profile.AttestedByUserId,
        profile.AttestedUtc,
        profile.VerificationStatus,
        profile.SourceKind,
        profile.SourceReference,
        profile.SourceCapturedUtc,
        profile.ExternalVerifier,
        profile.ExternallyVerifiedUtc,
        profile.Version,
        profile.CreatedByUserId,
        profile.UpdatedByUserId,
        profile.CreatedUtc,
        profile.UpdatedUtc);

    private Task WriteAuditAsync(
        CompanyStatutoryProfile profile,
        Guid actorUserId,
        string action,
        string? beforeHash,
        string afterHash,
        string? correlationId,
        DateTime occurredUtc,
        CancellationToken cancellationToken) =>
        _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                profile.CompanyId,
                AuditActorTypes.User,
                actorUserId,
                action,
                AuditTargetTypes.CompanyStatutoryProfile,
                profile.Id.ToString("D"),
                AuditEventOutcomes.Succeeded,
                "The company statutory profile was saved with versioned legal-fact evidence.",
                DataSources: [profile.SourceKind],
                Metadata: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["beforeHash"] = beforeHash,
                    ["afterHash"] = afterHash,
                    ["version"] = profile.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["formatComplete"] = profile.IsFormatComplete ? "true" : "false",
                    ["userAttested"] = profile.IsUserAttested ? "true" : "false",
                    ["verificationStatus"] = profile.VerificationStatus
                },
                CorrelationId: correlationId,
                OccurredUtc: occurredUtc,
                PayloadDiffJson: JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["before"] = beforeHash is null
                        ? null
                        : new { hash = beforeHash, version = profile.Version - 1 },
                    ["after"] = new { hash = afterHash, version = profile.Version }
                })),
            cancellationToken);

    private static string ComputeEvidenceHash(CompanyStatutoryProfile profile)
    {
        var evidence = new
        {
            profile.LegalName,
            profile.SwedishOrganisationNumber,
            profile.VatRegistrationNumber,
            profile.VatRegistrationStatus,
            RegisteredAddress = new { profile.RegisteredAddressLine1, profile.RegisteredAddressLine2, profile.RegisteredPostalCode, profile.RegisteredCity, profile.RegisteredCountryCode },
            CorrespondenceAddress = new { profile.CorrespondenceAddressLine1, profile.CorrespondenceAddressLine2, profile.CorrespondencePostalCode, profile.CorrespondenceCity, profile.CorrespondenceCountryCode },
            profile.CountryCode,
            profile.AccountingCurrency,
            profile.FiscalYearBasis,
            profile.BookkeepingMethod,
            profile.OrganisationRegistrationEffectiveFrom,
            profile.VatRegistrationEffectiveFrom,
            profile.VatRegistrationEffectiveTo,
            profile.IsUserAttested,
            profile.VerificationStatus,
            profile.SourceKind,
            profile.SourceReference,
            profile.SourceCapturedUtc,
            profile.ExternalVerifier,
            profile.ExternallyVerifiedUtc,
            profile.Version
        };
        var json = JsonSerializer.Serialize(evidence);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    private static void ValidateCompanyId(Guid companyId)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
    }

    private static void ValidateActor(Guid actorUserId)
    {
        if (actorUserId == Guid.Empty)
            throw new UnauthorizedAccessException("A resolved company user is required for statutory profile changes.");
    }

    private static CompanyStatutoryProfileException InvalidProfile(ArgumentException exception) =>
        new(CompanyStatutoryProfileReasonCodes.InvalidProfile, exception.Message);
    private static CompanyStatutoryProfileException NotFound() =>
        new(CompanyStatutoryProfileReasonCodes.NotFound, "No statutory profile exists for this company.");
    private static CompanyStatutoryProfileException AlreadyExists() =>
        new(CompanyStatutoryProfileReasonCodes.AlreadyExists,
            "A statutory profile already exists for this company. Reload it and use the versioned update operation.", isConflict: true);
    private static CompanyStatutoryProfileException ConcurrencyConflict() =>
        new(CompanyStatutoryProfileReasonCodes.ConcurrencyConflict,
            "The statutory profile changed after it was loaded. Reload it and try again; no partial update was saved.", isConflict: true);

    private static bool IsUniqueConflict(DbUpdateException exception)
    {
        if (exception.InnerException is SqlException { Number: 2601 or 2627 }) return true;
        var text = exception.ToString();
        return text.Contains("IX_company_statutory_profiles_company_id", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("UNIQUE constraint failed: company_statutory_profiles.company_id", StringComparison.OrdinalIgnoreCase);
    }
}
