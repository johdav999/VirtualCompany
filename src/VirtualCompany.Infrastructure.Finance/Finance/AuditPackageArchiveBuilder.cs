using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Finance;

public sealed record AuditPackageContentItem(
    string ArtifactType,
    string Path,
    string Status,
    bool IsRequired,
    string SourceType,
    string SourceReference,
    string? SourceVersion,
    string? DefinitionVersion,
    byte[]? Content,
    string? SafeDetail = null);

public sealed record AuditPackageManifestItem(
    int Sequence,
    string ArtifactType,
    string Path,
    string Status,
    bool IsRequired,
    string SourceType,
    string SourceReference,
    string? SourceVersion,
    string? DefinitionVersion,
    string? Sha256,
    long? ContentLength,
    string? SafeDetail);

public sealed record AuditPackageBuildResult(
    byte[] Archive,
    string ManifestJson,
    string ManifestChecksum,
    string PackageChecksum,
    bool IsComplete,
    IReadOnlyList<AuditPackageManifestItem> Items);

public static class AuditPackageArchiveBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Default
    };
    private static readonly DateTimeOffset StableZipTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static AuditPackageBuildResult Build(Guid companyId, Guid fiscalPeriodId, string fiscalPeriodName,
        string scopeKey, string scopeVersion, string scopeHash, string snapshotVersionsJson,
        DateTime frozenSnapshotUtc, IEnumerable<AuditPackageContentItem> sourceItems)
    {
        var ordered = sourceItems
            .OrderBy(x => NormalizePath(x.Path), StringComparer.Ordinal)
            .ThenBy(x => x.SourceReference, StringComparer.Ordinal)
            .ToArray();

        var indexContent = BuildIndex(fiscalPeriodName, scopeKey, scopeVersion, ordered);
        var allItems = ordered.Append(new AuditPackageContentItem(
                "reviewer_index", "index.html", AuditPackageArtifactStatuses.Included, true,
                "audit_package", $"period:{fiscalPeriodId:D}", scopeVersion, scopeHash, indexContent))
            .OrderBy(x => NormalizePath(x.Path), StringComparer.Ordinal)
            .ThenBy(x => x.SourceReference, StringComparer.Ordinal)
            .ToArray();

        var manifestItems = allItems.Select((item, index) =>
        {
            var content = item.Status == AuditPackageArtifactStatuses.Included ? item.Content : null;
            return new AuditPackageManifestItem(index + 1, item.ArtifactType, NormalizePath(item.Path),
                item.Status, item.IsRequired, item.SourceType, item.SourceReference,
                item.SourceVersion, item.DefinitionVersion,
                content is null ? null : Hash(content), content?.LongLength, item.SafeDetail);
        }).ToArray();

        var incomplete = manifestItems.Any(x => x.IsRequired && x.Status != AuditPackageArtifactStatuses.Included);
        using var snapshotDocument = JsonDocument.Parse(snapshotVersionsJson);
        var manifest = new
        {
            schema = "virtual-company-audit-package-manifest-v1",
            companyId,
            fiscalPeriodId,
            fiscalPeriodName,
            scope = new { key = scopeKey, version = scopeVersion, sha256 = scopeHash },
            frozenSnapshotUtc = frozenSnapshotUtc.ToUniversalTime(),
            snapshotVersions = snapshotDocument.RootElement.Clone(),
            completeness = incomplete ? "incomplete" : "complete",
            ordering = "path-ordinal-then-source-reference-ordinal",
            checksumAlgorithm = "SHA-256",
            items = manifestItems
        };
        var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions).Replace("\r\n", "\n", StringComparison.Ordinal);
        var manifestBytes = Encoding.UTF8.GetBytes(manifestJson);
        var manifestChecksum = Hash(manifestBytes);

        using var archiveBuffer = new MemoryStream();
        using (var archive = new ZipArchive(archiveBuffer, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8))
        {
            foreach (var item in allItems.Where(x => x.Status == AuditPackageArtifactStatuses.Included && x.Content is not null))
                WriteEntry(archive, NormalizePath(item.Path), item.Content!);
            WriteEntry(archive, "manifest.json", manifestBytes);
        }

        var archiveBytes = archiveBuffer.ToArray();
        return new AuditPackageBuildResult(archiveBytes, manifestJson, manifestChecksum,
            Hash(archiveBytes), !incomplete, manifestItems);
    }

    private static byte[] BuildIndex(string periodName, string scopeKey, string scopeVersion,
        IReadOnlyList<AuditPackageContentItem> items)
    {
        static string Escape(string? value) => System.Net.WebUtility.HtmlEncode(value ?? string.Empty);
        var rows = string.Join("", items.Select(item =>
            $"<tr><td>{Escape(item.ArtifactType)}</td><td>{Escape(NormalizePath(item.Path))}</td><td>{Escape(item.Status)}</td><td>{(item.IsRequired ? "Required" : "Optional")}</td><td>{Escape(item.SourceReference)}</td><td>{Escape(item.SafeDetail)}</td></tr>"));
        var html = $$"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><title>Audit package index</title>
            <style>body{font-family:Inter,Arial,sans-serif;margin:32px;color:#0f172a}h1{margin-bottom:4px}p{color:#475569}table{border-collapse:collapse;width:100%;font-size:13px}th,td{border:1px solid #cbd5e1;padding:8px;text-align:left;vertical-align:top}th{background:#f1f5f9}.included{color:#166534}.notice{padding:12px;background:#fff7ed;border:1px solid #fdba74}</style></head>
            <body><h1>Audit package index</h1><p>Period: {{Escape(periodName)}} · Scope: {{Escape(scopeKey)}} {{Escape(scopeVersion)}}</p>
            <p class="notice">This package is engineering and accounting evidence. It does not constitute statutory approval or a signed professional opinion. Verify manifest.json and every SHA-256 before review.</p>
            <table><thead><tr><th>Artifact</th><th>Path</th><th>Status</th><th>Requirement</th><th>Source reference</th><th>Detail</th></tr></thead><tbody>{{rows}}</tbody></table></body></html>
            """;
        return Encoding.UTF8.GetBytes(html.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    private static void WriteEntry(ZipArchive archive, string path, byte[] content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        entry.LastWriteTime = StableZipTimestamp;
        using var output = entry.Open();
        output.Write(content);
    }

    public static string Hash(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static string NormalizePath(string path)
    {
        var normalized = path.Replace('\\', '/').Trim().TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Split('/').Any(x => x is ".." or "." || x.Length == 0))
            throw new ArgumentException("An audit package path must be a bounded relative path.", nameof(path));
        return normalized;
    }
}
