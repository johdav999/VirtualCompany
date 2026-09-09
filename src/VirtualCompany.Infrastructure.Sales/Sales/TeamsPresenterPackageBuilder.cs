using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Sales;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class TeamsPresenterPackageBuilder(IOptions<TeamsPresenterOptions> configured) : ITeamsPresenterPackageBuilder
{
    public const string ManifestVersion = "1.29";
    public const string ManifestResourceName = "VirtualCompany.TeamsPresenter.manifest.template.json";
    public const string ColorIconResourceName = "VirtualCompany.TeamsPresenter.color.png";
    public const string OutlineIconResourceName = "VirtualCompany.TeamsPresenter.outline.png";

    private static readonly DateTimeOffset ReproducibleTimestamp = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions ManifestJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public IReadOnlyList<TeamsPresenterPackageValidationIssue> Validate()
    {
        var issues = TeamsPresenterConfigurationEvaluator.Evaluate(configured.Value)
            .Select(issue => new TeamsPresenterPackageValidationIssue(issue.Code, issue.Message))
            .ToList();

        ValidateManifestResource(issues);
        ValidateIcon(ColorIconResourceName, 192, 192, requireAlpha: false, "color_icon", issues);
        ValidateIcon(OutlineIconResourceName, 32, 32, requireAlpha: true, "outline_icon", issues);
        return issues;
    }

    public async Task<TeamsPresenterPackageBuildResult> BuildAsync(Stream output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (!output.CanWrite) throw new ArgumentException("The package output stream must be writable.", nameof(output));

        var issues = Validate();
        if (issues.Count > 0)
        {
            throw new InvalidOperationException($"The Teams presenter package is invalid: {string.Join(" ", issues.Select(x => x.Message))}");
        }

        await using var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8))
        {
            await WriteEntryAsync(archive, "manifest.json", BuildManifestBytes(), cancellationToken);
            await WriteEntryAsync(archive, "color.png", ReadResource(ColorIconResourceName), cancellationToken);
            await WriteEntryAsync(archive, "outline.png", ReadResource(OutlineIconResourceName), cancellationToken);
        }

        package.Position = 0;
        var hash = Convert.ToHexString(SHA256.HashData(package));
        package.Position = 0;
        await package.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);

        return new TeamsPresenterPackageBuildResult(ManifestVersion, configured.Value.PackageVersion, hash, 3, package.Length);
    }

    internal byte[] BuildManifestBytes()
    {
        var template = JsonNode.Parse(ReadResource(ManifestResourceName))?.AsObject()
            ?? throw new InvalidOperationException("The embedded Teams manifest template is invalid.");
        var options = configured.Value;
        var webOrigin = new Uri(options.WebOrigin, UriKind.Absolute);
        var apiOrigin = new Uri(options.PublicApiOrigin, UriKind.Absolute);

        template["id"] = options.TeamsAppId;
        template["version"] = options.PackageVersion;

        var developer = template["developer"]!.AsObject();
        developer["websiteUrl"] = options.WebOrigin;
        developer["privacyUrl"] = new Uri(webOrigin, "/privacy").AbsoluteUri;
        developer["termsOfUseUrl"] = new Uri(webOrigin, "/terms").AbsoluteUri;

        var tab = template["configurableTabs"]!.AsArray()[0]!.AsObject();
        tab["configurationUrl"] = options.ConfigurationUrl;

        var routeSupportsCalling = options.MediaRouteApproved &&
                                   options.MediaRoute is "teams_application_hosted" or "certified_provider";
        var bot = template["bots"]!.AsArray()[0]!.AsObject();
        bot["botId"] = options.BotApplicationId;
        bot["supportsCalling"] = options.CallControlEnabled && routeSupportsCalling;
        bot["supportsVideo"] = options.VisualMediaEnabled && options.CallControlEnabled && routeSupportsCalling;

        var domains = new[] { webOrigin.Host, apiOrigin.Host }
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Select(value => (JsonNode?)JsonValue.Create(value))
            .ToArray();
        template["validDomains"] = new JsonArray(domains);

        var webApplicationInfo = template["webApplicationInfo"]!.AsObject();
        webApplicationInfo["id"] = options.WebApplicationId;
        webApplicationInfo["resource"] = options.WebApplicationResource;

        if (!options.SharedStageEnabled)
        {
            template.Remove("authorization");
        }

        return Encoding.UTF8.GetBytes(template.ToJsonString(ManifestJsonOptions) + Environment.NewLine);
    }

    private void ValidateManifestResource(ICollection<TeamsPresenterPackageValidationIssue> issues)
    {
        try
        {
            var manifest = JsonNode.Parse(ReadResource(ManifestResourceName))?.AsObject();
            if (manifest is null ||
                !string.Equals(manifest["manifestVersion"]?.GetValue<string>(), ManifestVersion, StringComparison.Ordinal) ||
                !string.Equals(manifest["$schema"]?.GetValue<string>(),
                    $"https://developer.microsoft.com/json-schemas/teams/v{ManifestVersion}/MicrosoftTeams.schema.json",
                    StringComparison.Ordinal))
            {
                issues.Add(new("manifest_schema_invalid", $"The embedded manifest must target Teams schema {ManifestVersion}."));
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or InvalidOperationException)
        {
            issues.Add(new("manifest_template_invalid", "The embedded Teams manifest template could not be parsed."));
        }
    }

    private void ValidateIcon(
        string resourceName,
        int expectedWidth,
        int expectedHeight,
        bool requireAlpha,
        string codePrefix,
        ICollection<TeamsPresenterPackageValidationIssue> issues)
    {
        try
        {
            var png = ReadResource(resourceName);
            var metadata = ReadPngMetadata(png);
            if (metadata.Width != expectedWidth || metadata.Height != expectedHeight)
            {
                issues.Add(new($"{codePrefix}_dimensions_invalid", $"The {codePrefix.Replace('_', ' ')} must be {expectedWidth}x{expectedHeight} pixels."));
            }

            if (requireAlpha && !metadata.HasAlpha)
            {
                issues.Add(new($"{codePrefix}_alpha_missing", $"The {codePrefix.Replace('_', ' ')} must contain transparency."));
            }
        }
        catch (InvalidDataException)
        {
            issues.Add(new($"{codePrefix}_invalid", $"The embedded {codePrefix.Replace('_', ' ')} is not a valid PNG."));
        }
    }

    private static PngMetadata ReadPngMetadata(byte[] value)
    {
        ReadOnlySpan<byte> png = value;
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (png.Length < 33 || !png[..8].SequenceEqual(signature) || !png.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            throw new InvalidDataException("The icon is not a PNG image.");
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(png.Slice(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(png.Slice(20, 4));
        var colorType = png[25];
        var hasAlpha = colorType is 4 or 6 || ContainsChunk(png, "tRNS"u8);
        return new PngMetadata(width, height, hasAlpha);
    }

    private static bool ContainsChunk(ReadOnlySpan<byte> png, ReadOnlySpan<byte> chunkName)
    {
        var offset = 8;
        while (offset + 12 <= png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.Slice(offset, 4));
            if (length < 0 || offset + 12L + length > png.Length) return false;
            if (png.Slice(offset + 4, 4).SequenceEqual(chunkName)) return true;
            offset += 12 + length;
        }

        return false;
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string name, byte[] value, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = ReproducibleTimestamp;
        await using var stream = entry.Open();
        await stream.WriteAsync(value, cancellationToken);
    }

    private static byte[] ReadResource(string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException($"Embedded Teams package resource '{resourceName}' was not found.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private sealed record PngMetadata(int Width, int Height, bool HasAlpha);
}
