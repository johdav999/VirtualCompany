using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class TeamsMediaHostRuntimeTests
{
    [Fact]
    public async Task Drain_is_idempotent_and_rejects_new_call_admission()
    {
        var options = Options.Create(new TeamsPresenterOptions
        {
            Enabled = true,
            CallControlEnabled = true,
            AudioEnabled = true,
            MediaRoute = "teams_application_hosted",
            MediaRouteApproved = true,
            MediaHostDeploymentApproved = true,
            CallControlHostId = "vmss-7",
            MaxActiveCallsPerHost = 2,
            MediaSdkMaximumAgeDays = 92,
            MediaHostDrainMinutes = 15
        });
        var protection = new RecordingProtection();
        var runtime = new TeamsMediaHostRuntime(options, protection, new ReadyProbe(), TimeProvider.System,
            NullLogger<TeamsMediaHostRuntime>.Instance);

        var first = await runtime.BeginDrainAsync(TimeSpan.FromMinutes(3), "deployment", default);
        var duplicate = await runtime.BeginDrainAsync(TimeSpan.FromMinutes(30), "duplicate", default);
        var exception = await Assert.ThrowsAsync<TeamsMeetingMediaException>(() =>
            runtime.ReserveCallAsync(Guid.NewGuid(), default));

        Assert.False(first.AlreadyDraining);
        Assert.True(duplicate.AlreadyDraining);
        Assert.Equal(first.Status.DrainDeadlineUtc, duplicate.Status.DrainDeadlineUtc);
        Assert.False(duplicate.Status.AcceptingNewCalls);
        Assert.Equal(TeamsMediaHostStates.Draining, duplicate.Status.State);
        Assert.Equal(TeamsMediaHostProblemCodes.Draining, exception.Code);
        Assert.Empty(protection.Values);
    }

    [Fact]
    public async Task Capacity_protects_the_instance_and_releases_protection_after_the_last_call()
    {
        var options = Options.Create(new TeamsPresenterOptions
        {
            Enabled = true,
            AudioEnabled = true,
            MediaRoute = "teams_application_hosted",
            CallControlHostId = "vmss-2",
            MaxActiveCallsPerHost = 1
        });
        var protection = new RecordingProtection();
        var runtime = new TeamsMediaHostRuntime(options, protection, new ReadyProbe(), TimeProvider.System,
            NullLogger<TeamsMediaHostRuntime>.Instance);
        var callId = Guid.NewGuid();

        await runtime.ReserveCallAsync(callId, default);
        var capacity = await Assert.ThrowsAsync<TeamsMeetingMediaException>(() =>
            runtime.ReserveCallAsync(Guid.NewGuid(), default));
        await runtime.ReleaseCallAsync(callId, default);

        Assert.Equal(TeamsMediaHostProblemCodes.CapacityReached, capacity.Code);
        Assert.Equal([true, false], protection.Values);
        Assert.Equal(0, runtime.GetStatus().ActiveCalls);
    }

    [Fact]
    public void Infrastructure_template_has_required_security_affinity_monitoring_and_cost_controls()
    {
        var root = RepositoryRoot();
        var main = File.ReadAllText(Path.Combine(root, "infra", "teams-media", "main.bicep"));
        var subscription = File.ReadAllText(Path.Combine(root, "infra", "teams-media", "subscription.bicep"));
        var bootstrap = File.ReadAllText(Path.Combine(root, "scripts", "Install-TeamsMediaHost.ps1"));

        Assert.Contains("2022-datacenter-azure-edition", main, StringComparison.Ordinal);
        Assert.Contains("publicIPAddressConfiguration", main, StringComparison.Ordinal);
        Assert.Contains("/health/media-host/live", main, StringComparison.Ordinal);
        Assert.Contains("automaticRepairsPolicy", main, StringComparison.Ordinal);
        Assert.Contains("autoscaleSettings", main, StringComparison.Ordinal);
        Assert.Contains("AzureMonitorWindowsAgent", main, StringComparison.Ordinal);
        Assert.Contains("Microsoft.Insights/workbooks", main, StringComparison.Ordinal);
        Assert.Contains("Key Vault Secrets User", main, StringComparison.Ordinal);
        Assert.Contains("deny-management-inbound", main, StringComparison.Ordinal);
        Assert.DoesNotContain("allow-rdp", main, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Microsoft.Consumption/budgets", subscription, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash", bootstrap, StringComparison.Ordinal);
        Assert.Contains("LocalMachine", bootstrap, StringComparison.Ordinal);
        Assert.Contains("metadata/instance", bootstrap, StringComparison.Ordinal);
        Assert.DoesNotContain("ClientSecret", bootstrap, StringComparison.Ordinal);
    }

    [Fact]
    public void Reviewed_media_sdk_lock_matches_project_and_runtime_gate()
    {
        var root = RepositoryRoot();
        using var lockDocument = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, "infra", "teams-media", "media-sdk-lock.json")));
        var version = lockDocument.RootElement.GetProperty("version").GetString();
        var published = lockDocument.RootElement.GetProperty("publishedUtc").GetDateTime();
        var maximumAge = lockDocument.RootElement.GetProperty("maximumAgeDays").GetInt32();
        var project = File.ReadAllText(Path.Combine(root, "src", "VirtualCompany.Infrastructure.Sales",
            "VirtualCompany.Infrastructure.Sales.csproj"));

        Assert.Equal(TeamsMediaSdkCompatibility.PackageVersion, version);
        Assert.Contains($"Microsoft.Graph.Communications.Calls.Media\" Version=\"{version}\"", project,
            StringComparison.Ordinal);
        Assert.Equal(TeamsMediaSdkCompatibility.PublishedUtc, published.ToUniversalTime());
        Assert.InRange((DateTime.UtcNow - published.ToUniversalTime()).TotalDays, 0, maximumAge);
    }

    [Fact]
    public void Media_host_assignment_is_one_way_before_join()
    {
        var call = new VirtualCompany.Domain.Entities.TeamsMeetingCall(Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new string('A', 64), 1, 1, "pending", DateTime.UtcNow);

        call.AssignMediaHost("vmss-3", DateTime.UtcNow);
        call.BeginJoin(DateTime.UtcNow);

        Assert.Equal("vmss-3", call.MediaHostInstanceId);
        Assert.Throws<InvalidOperationException>(() => call.AssignMediaHost("vmss-4", DateTime.UtcNow));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "VirtualCompany.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private sealed class RecordingProtection : ITeamsVmssInstanceProtection
    {
        public List<bool> Values { get; } = [];
        public Task SetAsync(bool protect, CancellationToken cancellationToken)
        {
            Values.Add(protect);
            return Task.CompletedTask;
        }
    }

    private sealed class ReadyProbe : ITeamsMediaHostPrerequisiteProbe
    {
        public string MediaSdkVersion => TeamsMediaSdkCompatibility.PackageVersion;
        public IReadOnlyList<TeamsMediaHostValidationCheck> Validate(TeamsPresenterOptions options, DateTime nowUtc) =>
            [new("test", true, TeamsMediaHostProblemCodes.Ready, "Ready")];
    }
}
