using Microsoft.AspNetCore.DataProtection;
using VirtualCompany.Infrastructure.Security;
using Xunit;

namespace VirtualCompany.Infrastructure.Platform.Tests;

public sealed class PlatformSecretStoreTests
{
    [Fact]
    public async Task Encrypted_file_store_persists_versions_without_plaintext()
    {
        var root = Path.Combine(Path.GetTempPath(), "virtual-company-platform-secrets", Guid.NewGuid().ToString("N"));
        var keyPath = Path.Combine(root, "keys");
        var secretPath = Path.Combine(root, "platform-secrets.json");
        Directory.CreateDirectory(root);

        try
        {
            var protectionProvider = DataProtectionProvider.Create(new DirectoryInfo(keyPath));
            var firstStore = new DataProtectionFilePlatformSecretStore(
                protectionProvider,
                TimeProvider.System,
                secretPath);

            var firstWrite = await firstStore.SetAsync(
                "finance-provider-fortnox-oauth-credentials",
                """{"clientId":"client-one","clientSecret":"never-store-plaintext"}""",
                CancellationToken.None);
            var secondWrite = await firstStore.SetAsync(
                "finance-provider-fortnox-oauth-credentials",
                """{"clientId":"client-two","clientSecret":"rotated-secret"}""",
                CancellationToken.None);

            var restartedStore = new DataProtectionFilePlatformSecretStore(
                DataProtectionProvider.Create(new DirectoryInfo(keyPath)),
                TimeProvider.System,
                secretPath);
            var current = await restartedStore.GetAsync(
                "finance-provider-fortnox-oauth-credentials",
                secondWrite.Version,
                CancellationToken.None);
            var previous = await restartedStore.GetAsync(
                "finance-provider-fortnox-oauth-credentials",
                firstWrite.Version,
                CancellationToken.None);
            var persistedDocument = await File.ReadAllTextAsync(secretPath);

            Assert.Equal("""{"clientId":"client-two","clientSecret":"rotated-secret"}""", current?.Value);
            Assert.Equal("""{"clientId":"client-one","clientSecret":"never-store-plaintext"}""", previous?.Value);
            Assert.DoesNotContain("client-one", persistedDocument, StringComparison.Ordinal);
            Assert.DoesNotContain("never-store-plaintext", persistedDocument, StringComparison.Ordinal);
            Assert.DoesNotContain("rotated-secret", persistedDocument, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
