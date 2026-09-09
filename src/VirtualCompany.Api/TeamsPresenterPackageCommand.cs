using VirtualCompany.Application.Sales;

namespace VirtualCompany.Api;

internal static class TeamsPresenterPackageCommand
{
    private const string CommandName = "package-teams-presenter";

    public static async Task<int?> TryExecuteAsync(
        string[] args,
        IServiceProvider services,
        TextWriter standardOutput,
        TextWriter standardError,
        CancellationToken cancellationToken)
    {
        if (args.Length == 0 || !string.Equals(args[0], CommandName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!TryParse(args, out var outputPath, out var overwrite, out var error))
        {
            await standardError.WriteLineAsync(error);
            return 2;
        }

        var fullOutputPath = Path.GetFullPath(outputPath!);
        if (!string.Equals(Path.GetExtension(fullOutputPath), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            await standardError.WriteLineAsync("The Teams presenter package output path must end in .zip.");
            return 2;
        }

        if (File.Exists(fullOutputPath) && !overwrite)
        {
            await standardError.WriteLineAsync($"The output already exists: {fullOutputPath}. Pass --force to replace this exact package.");
            return 2;
        }

        var directory = Path.GetDirectoryName(fullOutputPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            await standardError.WriteLineAsync("The Teams presenter package output directory is invalid.");
            return 2;
        }

        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullOutputPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var builder = services.GetRequiredService<ITeamsPresenterPackageBuilder>();
            TeamsPresenterPackageBuildResult result;
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                result = await builder.BuildAsync(output, cancellationToken);
            }

            File.Move(temporaryPath, fullOutputPath, overwrite);
            await standardOutput.WriteLineAsync(
                $"Built Teams presenter package {result.PackageVersion} ({result.ManifestVersion}, {result.EntryCount} entries, SHA-256 {result.Sha256}) at {fullOutputPath}.");
            return 0;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            await standardError.WriteLineAsync(exception.Message);
            return 1;
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static bool TryParse(string[] args, out string? outputPath, out bool overwrite, out string? error)
    {
        outputPath = null;
        overwrite = false;
        error = null;
        for (var index = 1; index < args.Length; index++)
        {
            if (string.Equals(args[index], "--force", StringComparison.OrdinalIgnoreCase))
            {
                overwrite = true;
                continue;
            }

            if (string.Equals(args[index], "--output", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < args.Length &&
                !string.IsNullOrWhiteSpace(args[index + 1]))
            {
                outputPath = args[++index];
                continue;
            }

            error = $"Unknown or incomplete option '{args[index]}'. Usage: {CommandName} --output <package.zip> [--force]";
            return false;
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            error = $"Usage: {CommandName} --output <package.zip> [--force]";
            return false;
        }

        return true;
    }
}
