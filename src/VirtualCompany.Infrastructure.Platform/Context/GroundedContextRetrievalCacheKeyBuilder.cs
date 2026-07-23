using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using VirtualCompany.Application.Context;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Context;

public sealed class GroundedContextRetrievalCacheKeyBuilder
{
    public string BuildKnowledgeSectionKey(
        string keyVersion,
        GroundedContextRetrievalRequest request,
        RetrievalAccessDecision accessDecision,
        string retrievalIntent,
        int limit) =>
        BuildSectionKey(
            keyVersion,
            "knowledge",
            request,
            accessDecision,
            retrievalIntent,
            limit,
            asOfUtc: null);

    public string BuildMemorySectionKey(
        string keyVersion,
        GroundedContextRetrievalRequest request,
        RetrievalAccessDecision accessDecision,
        string retrievalIntent,
        int limit,
        DateTime asOfUtc) =>
        BuildSectionKey(
            keyVersion,
            "memory",
            request,
            accessDecision,
            retrievalIntent,
            limit,
            EnsureUtc(asOfUtc));

    private static string BuildSectionKey(
        string keyVersion,
        string sectionId,
        GroundedContextRetrievalRequest request,
        RetrievalAccessDecision accessDecision,
        string retrievalIntent,
        int limit,
        DateTime? asOfUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(accessDecision);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(retrievalIntent);

        var normalizedScopes = accessDecision.EffectiveReadScopes
            .Where(static scope => !string.IsNullOrWhiteSpace(scope))
            .Select(static scope => NormalizeValue(scope) ?? string.Empty)
            .OrderBy(static scope => scope, StringComparer.Ordinal)
            .ToArray();

        var payload = string.Join(
            "|",
            [
                NormalizeValue(keyVersion) ?? "default",
                NormalizeValue(sectionId) ?? "section",
                request.CompanyId.ToString("N"),
                request.AgentId.ToString("N"),
                request.ActorUserId?.ToString("N") ?? "none",
                accessDecision.ActorMembershipId?.ToString("N") ?? "none",
                NormalizeValue(accessDecision.ActorMembershipRole?.ToStorageValue()) ?? "none",
                accessDecision.MembershipResolved ? "1" : "0",
                accessDecision.CanRetrieve ? "1" : "0",
                limit.ToString(CultureInfo.InvariantCulture),
                asOfUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "none",
                NormalizeValue(retrievalIntent) ?? string.Empty,
                string.Join(",", normalizedScopes)
            ]);

        var payloadHash = ComputeSha256(payload);
        return $"grounded-context:{SanitizeSegment(keyVersion)}:{SanitizeSegment(sectionId)}:company:{request.CompanyId:N}:agent:{request.AgentId:N}:{payloadHash}";
    }

    private static string ComputeSha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string? NormalizeValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var collapsed = string.Join(" ", value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.ToLowerInvariant();
    }

    private static string SanitizeSegment(string value)
    {
        var normalized = NormalizeValue(value) ?? "default";
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            builder.Append(char.IsLetterOrDigit(character) || character is '-' or '_' or '.'
                ? character
                : '-');
        }

        return builder.ToString();
    }

    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : value.ToUniversalTime();
}
