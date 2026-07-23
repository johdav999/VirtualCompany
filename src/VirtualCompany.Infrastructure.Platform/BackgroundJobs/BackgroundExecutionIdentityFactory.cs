using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using VirtualCompany.Application.BackgroundExecution;

namespace VirtualCompany.Infrastructure.BackgroundJobs;

public sealed class DefaultBackgroundExecutionIdentityFactory : IBackgroundExecutionIdentityFactory
{
    private const int IdempotencyHashLength = 32;
    private const int OperationPrefixMaxLength = 80;

    public string CreateCorrelationId() => Guid.NewGuid().ToString("N");

    public string EnsureCorrelationId(string? correlationId) =>
        string.IsNullOrWhiteSpace(correlationId)
            ? CreateCorrelationId()
            : correlationId.Trim();

    public string CreateIdempotencyKey(string operationName, params object?[] stableSegments)
    {
        var normalizedOperation = NormalizeOperationName(operationName);
        var canonicalSegments = stableSegments is { Length: > 0 }
            ? string.Join(":", stableSegments.Select(NormalizeSegment))
            : "_";
        var canonicalValue = $"{normalizedOperation}:{canonicalSegments}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalValue)))
            .ToLowerInvariant();

        return $"{normalizedOperation}:{hash[..IdempotencyHashLength]}";
    }

    public BackgroundExecutionIdentity Create(
        Guid companyId,
        string operationName,
        string? correlationId,
        params object?[] stableSegments)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        var effectiveCorrelationId = EnsureCorrelationId(correlationId);
        var segments = stableSegments is { Length: > 0 }
            ? new object?[stableSegments.Length + 1]
            : new object?[1];
        segments[0] = companyId;

        if (stableSegments is { Length: > 0 })
        {
            Array.Copy(stableSegments, 0, segments, 1, stableSegments.Length);
        }

        return new BackgroundExecutionIdentity(
            companyId,
            effectiveCorrelationId,
            CreateIdempotencyKey(operationName, segments));
    }

    public BackgroundExecutionIdentity FromExisting(
        Guid companyId,
        string correlationId,
        string idempotencyKey) =>
        new(companyId, EnsureCorrelationId(correlationId), NormalizeRequired(idempotencyKey, nameof(idempotencyKey)));

    private static string NormalizeOperationName(string operationName)
    {
        var normalized = NormalizeRequired(operationName, nameof(operationName))
            .ToLowerInvariant();
        var builder = new StringBuilder(normalized.Length);
        var previousWasSeparator = false;

        foreach (var character in normalized)
        {
            if (char.IsLetterOrDigit(character) || character is '.' or ':' or '-' or '_')
            {
                builder.Append(character);
                previousWasSeparator = false;
                continue;
            }

            if (!previousWasSeparator)
            {
                builder.Append('-');
                previousWasSeparator = true;
            }
        }

        var value = builder.ToString().Trim('-', ':', '.', '_');
        if (value.Length == 0)
        {
            throw new ArgumentException("Operation name must include at least one alphanumeric character.", nameof(operationName));
        }

        return value.Length <= OperationPrefixMaxLength ? value : value[..OperationPrefixMaxLength];
    }

    private static string NormalizeSegment(object? segment) =>
        segment switch
        {
            null => "_",
            Guid value => value.ToString("N"),
            DateTime value => (value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime()).ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset value => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            IFormattable value => value.ToString(null, CultureInfo.InvariantCulture)?.Trim() ?? "_",
            _ => segment.ToString()?.Trim() ?? "_"
        };

    private static string NormalizeRequired(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        return value.Trim();
    }
}