using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Sales;

public sealed partial class SalesPresentationPresetService
{
    public Task<SalesPresentationPresetCover?> GetCoverAsync(Guid companyId, Guid actorUserId,
        Guid presetId, Guid slideId, CancellationToken ct) =>
        ReadSlideImageAsync(companyId, actorUserId, presetId, null, slideId, true, ct);

    public Task<SalesPresentationPresetCover?> GetSlideImageAsync(Guid companyId, Guid actorUserId,
        Guid presetId, Guid versionId, Guid slideId, CancellationToken ct) =>
        ReadSlideImageAsync(companyId, actorUserId, presetId, versionId, slideId, false, ct);

    private async Task<SalesPresentationPresetCover?> ReadSlideImageAsync(Guid companyId, Guid actorUserId,
        Guid presetId, Guid? versionId, Guid slideId, bool firstOnly, CancellationToken ct)
    {
        await EnsureMemberAsync(companyId, actorUserId, ct);
        var key = await db.SalesPresentationPresetSlides.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Id == slideId && (!firstOnly || x.SlideNumber == 1) &&
                (!versionId.HasValue || x.Asset.PresetVersionId == versionId.Value) &&
                x.Asset.CompanyId == companyId && x.Asset.PresetVersion.CompanyId == companyId &&
                x.Asset.PresetVersion.PresetId == presetId &&
                x.Asset.Status == SalesPresentationPresetAssetStatus.Processed &&
                x.ProcessingVersion == x.Asset.ProcessingVersion)
            .Select(x => x.ImageStorageKey).SingleOrDefaultAsync(ct);
        if (key is null) return null;
        try
        {
            await using var source = await storage.OpenReadAsync(key, ct);
            using var buffer = new MemoryStream();
            var chunk = new byte[16384];
            int count;
            while ((count = await source.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + count > 10_485_760) return null;
                buffer.Write(chunk, 0, count);
            }
            var bytes = buffer.ToArray();
            // Only serve renderer-produced raster data, never executable SVG/HTML.
            var contentType = bytes.AsSpan().StartsWith(new byte[] {137,80,78,71,13,10,26,10}) ? "image/png" :
                bytes.AsSpan().StartsWith(new byte[] {255,216,255}) ? "image/jpeg" : null;
            return contentType is null ? null : new(bytes, contentType);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or KeyNotFoundException)
        {
            return null;
        }
    }
}
