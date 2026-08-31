using System.IO.Compression;
using System.Text;
using System.Text.Json;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Finance;

internal sealed record AuditPackageArchiveVerification(
    bool IsValid,
    string PackageChecksum,
    string ManifestChecksum,
    int CheckedItemCount,
    int MissingItemCount,
    int CorruptItemCount,
    string ResultCode,
    string SafeSummary);

internal static class AuditPackageArchiveVerifier
{
    public static AuditPackageArchiveVerification Verify(byte[] bytes, long maximumItemBytes,
        IReadOnlyCollection<AuditPackageArtifact> databaseArtifacts, string expectedPackageChecksum,
        string expectedManifestChecksum)
    {
        var actualPackageHash = AuditPackageArchiveBuilder.Hash(bytes);
        var actualManifestHash = AuditPackageArchiveBuilder.Hash([]);
        var missing = 0;
        var corrupt = 0;
        var checkedItems = 0;
        var manifestItems = new Dictionary<string, ManifestItem>(StringComparer.Ordinal);

        if (bytes.Length == 0)
        {
            missing++;
        }
        else
        {
            try
            {
                using var stream = new MemoryStream(bytes, writable: false);
                using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false, Encoding.UTF8);
                var manifestEntry = zip.GetEntry("manifest.json");
                if (manifestEntry is null)
                {
                    missing++;
                }
                else
                {
                    var manifestBytes = ReadBounded(manifestEntry.Open(), 2_000_000);
                    actualManifestHash = AuditPackageArchiveBuilder.Hash(manifestBytes);
                    using var manifest = JsonDocument.Parse(manifestBytes);
                    if (!manifest.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                    {
                        corrupt++;
                    }
                    else
                    {
                        foreach (var item in items.EnumerateArray())
                        {
                            if (!TryReadManifestItem(item, out var manifestItem) ||
                                !manifestItems.TryAdd(manifestItem.Path, manifestItem))
                            {
                                corrupt++;
                                continue;
                            }

                            if (manifestItem.Status != AuditPackageArtifactStatuses.Included) continue;
                            checkedItems++;
                            var entry = zip.GetEntry(manifestItem.Path);
                            if (entry is null)
                            {
                                missing++;
                                continue;
                            }

                            var entryBytes = ReadBounded(entry.Open(), maximumItemBytes);
                            if (manifestItem.Checksum is null ||
                                !AuditPackageArchiveBuilder.Hash(entryBytes).Equals(manifestItem.Checksum,
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                corrupt++;
                            }
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is InvalidDataException or JsonException or IOException)
            {
                corrupt++;
            }
        }

        var orderedArtifacts = databaseArtifacts.OrderBy(x => x.Sequence).ToArray();
        checkedItems += orderedArtifacts.Length;
        if (orderedArtifacts.Any(x => x.Status == AuditPackageArtifactStatuses.Missing)) missing++;
        if (orderedArtifacts.Any(x => x.Status is AuditPackageArtifactStatuses.Corrupt or AuditPackageArtifactStatuses.Inaccessible)) corrupt++;
        foreach (var artifact in orderedArtifacts)
        {
            if (!manifestItems.TryGetValue(artifact.Path, out var manifestItem))
            {
                missing++;
                continue;
            }

            if (manifestItem.Status != artifact.Status || manifestItem.Checksum != artifact.Checksum ||
                manifestItem.SourceReference != artifact.SourceReference)
            {
                corrupt++;
            }
        }

        if (!actualPackageHash.Equals(expectedPackageChecksum, StringComparison.OrdinalIgnoreCase)) corrupt++;
        if (!actualManifestHash.Equals(expectedManifestChecksum, StringComparison.OrdinalIgnoreCase)) corrupt++;
        var valid = missing == 0 && corrupt == 0;
        var resultCode = valid ? "verified" : missing > 0 ? "missing_evidence_or_object" : "checksum_mismatch";
        var summary = valid
            ? "Database metadata, package object, manifest, and included item hashes match. Human accountant review remains separate."
            : $"Verification found {missing} missing and {corrupt} corrupt or mismatched evidence result(s).";
        return new(valid, actualPackageHash, actualManifestHash, checkedItems, missing, corrupt, resultCode, summary);
    }

    private static bool TryReadManifestItem(JsonElement item, out ManifestItem manifestItem)
    {
        manifestItem = default!;
        if (!item.TryGetProperty("path", out var pathElement) || pathElement.ValueKind != JsonValueKind.String ||
            !item.TryGetProperty("status", out var statusElement) || statusElement.ValueKind != JsonValueKind.String ||
            !item.TryGetProperty("sourceReference", out var sourceElement) || sourceElement.ValueKind != JsonValueKind.String ||
            !item.TryGetProperty("sha256", out var checksumElement))
        {
            return false;
        }

        var path = pathElement.GetString();
        var status = statusElement.GetString();
        var sourceReference = sourceElement.GetString();
        var checksum = checksumElement.ValueKind == JsonValueKind.Null ? null : checksumElement.GetString();
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(status) ||
            string.IsNullOrWhiteSpace(sourceReference) ||
            checksumElement.ValueKind is not (JsonValueKind.Null or JsonValueKind.String))
        {
            return false;
        }

        manifestItem = new(path, status, checksum, sourceReference);
        return true;
    }

    private static byte[] ReadBounded(Stream source, long maximumBytes)
    {
        using (source)
        using (var output = new MemoryStream())
        {
            var buffer = new byte[81920];
            while (true)
            {
                var read = source.Read(buffer, 0, buffer.Length);
                if (read == 0) return output.ToArray();
                if (output.Length + read > maximumBytes)
                    throw new InvalidDataException("An audit-package item exceeded the verification read bound.");
                output.Write(buffer, 0, read);
            }
        }
    }

    private sealed record ManifestItem(string Path, string Status, string? Checksum, string SourceReference);
}

