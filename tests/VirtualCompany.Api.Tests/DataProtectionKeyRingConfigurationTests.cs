using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using VirtualCompany.Api;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class DataProtectionKeyRingConfigurationTests
{
    [Fact]
    public void Production_requires_an_explicit_key_ring_path()
    {
        var configuration = new ConfigurationBuilder().Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            DataProtectionKeyRingConfiguration.ResolveDirectory(
                configuration,
                new TestHostEnvironment(Environments.Production)));

        Assert.Contains(DataProtectionKeyRingConfiguration.ConfigurationKey, exception.Message, StringComparison.Ordinal);
        Assert.Contains("durable", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Relative_key_ring_paths_are_rejected()
    {
        var configuration = ConfigurationWithPath("App_Data/data-protection-keys");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            DataProtectionKeyRingConfiguration.ResolveDirectory(
                configuration,
                new TestHostEnvironment(Environments.Production)));

        Assert.Contains("absolute path", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Persisted_keys_decrypt_payloads_after_service_provider_restart()
    {
        var keyRingPath = Path.Combine(Path.GetTempPath(), $"virtualcompany-data-protection-{Guid.NewGuid():N}");

        try
        {
            var configuration = ConfigurationWithPath(keyRingPath);
            var environment = new TestHostEnvironment(Environments.Production);
            var firstServices = new ServiceCollection();
            var firstDirectory = DataProtectionKeyRingConfiguration.Configure(firstServices, configuration, environment);

            string protectedValue;
            using (var firstProvider = firstServices.BuildServiceProvider())
            {
                protectedValue = firstProvider
                    .GetRequiredService<IDataProtectionProvider>()
                    .CreateProtector("mailbox-oauth-test")
                    .Protect("refresh-token");
            }

            var secondServices = new ServiceCollection();
            var secondDirectory = DataProtectionKeyRingConfiguration.Configure(secondServices, configuration, environment);
            using var secondProvider = secondServices.BuildServiceProvider();
            var unprotectedValue = secondProvider
                .GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("mailbox-oauth-test")
                .Unprotect(protectedValue);

            Assert.Equal("refresh-token", unprotectedValue);
            Assert.Equal(firstDirectory.FullName, secondDirectory.FullName);
            Assert.NotEmpty(Directory.GetFiles(keyRingPath, "key-*.xml"));
        }
        finally
        {
            if (Directory.Exists(keyRingPath))
            {
                Directory.Delete(keyRingPath, recursive: true);
            }
        }
    }

    private static IConfiguration ConfigurationWithPath(string path) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DataProtectionKeyRingConfiguration.ConfigurationKey] = path
            })
            .Build();

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = nameof(VirtualCompany);
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

file static class VirtualCompany
{
}
