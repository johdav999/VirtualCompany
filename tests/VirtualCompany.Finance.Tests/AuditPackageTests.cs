using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

public sealed class AuditPackageTests
{
    private static readonly Guid CompanyId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid PeriodId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly DateTime FrozenUtc = new(2026, 8, 30, 10, 0, 0, DateTimeKind.Utc);
    private static readonly string ScopeHash = new('a', 64);

    [Fact]
    public void Same_snapshot_produces_identical_manifest_and_package_checksums()
    {
        var items = new[]
        {
            Included("trial_balance", "ledger/trial-balance.json", "period:2026-08", "{\"balanced\":true}"),
            Included("general_ledger", "ledger/general-ledger.json", "period:2026-08", "{\"entries\":[1,2]}")
        };

        var first = Build(items);
        var second = Build(items.Reverse());

        Assert.True(first.IsComplete);
        Assert.Equal(first.ManifestChecksum, second.ManifestChecksum);
        Assert.Equal(first.PackageChecksum, second.PackageChecksum);
        Assert.Equal(first.ManifestJson, second.ManifestJson);
        Assert.Equal(first.Archive, second.Archive);
    }

    [Fact]
    public void Required_missing_or_inaccessible_evidence_blocks_final_label_with_bounded_details()
    {
        var result = Build([
            Included("trial_balance", "ledger/trial-balance.json", "period:2026-08", "{}"),
            new("source_document", "documents/bank-confirmation.pdf", AuditPackageArtifactStatuses.Inaccessible,
                true, "company_document", "document:42", "v3", null, null,
                "The requesting review scope cannot access this linked document.")
        ]);

        Assert.False(result.IsComplete);
        var blocked = Assert.Single(result.Items, x => x.Status == AuditPackageArtifactStatuses.Inaccessible);
        Assert.True(blocked.IsRequired);
        Assert.Null(blocked.Sha256);
        Assert.Contains("cannot access", blocked.SafeDetail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("documents/bank-confirmation.pdf", ZipEntries(result.Archive));
    }

    [Fact]
    public void Archive_uses_deterministic_order_and_per_item_sha256()
    {
        var result = Build([
            Included("policy", "z/policy.json", "policy:1", "policy"),
            Included("close", "a/close.json", "close:1", "close")
        ]);

        Assert.Equal(result.Items.OrderBy(x => x.Path, StringComparer.Ordinal).Select(x => x.Path), result.Items.Select(x => x.Path));
        Assert.All(result.Items, x => Assert.Matches("^[0-9a-f]{64}$", x.Sha256!));
        Assert.Equal(["a/close.json", "index.html", "z/policy.json", "manifest.json"], ZipEntries(result.Archive));
    }

    [Fact]
    public void Domain_requires_independent_approval_and_honors_cancellation_before_finalization()
    {
        var requester = Guid.NewGuid();
        var package = NewPackage(requester);

        Assert.Throws<InvalidOperationException>(() => package.Approve(requester, FrozenUtc.AddMinutes(1)));
        package.Approve(Guid.NewGuid(), FrozenUtc.AddMinutes(1));
        Assert.True(package.TryStart(FrozenUtc.AddMinutes(2)));
        package.RequestCancellation(FrozenUtc.AddMinutes(3));
        package.Complete("{}", new string('b', 64), new string('c', 64), "audit/package.zip",
            "package.zip", "application/zip", 10, true, FrozenUtc.AddMinutes(4));

        Assert.Equal(AuditPackageStatuses.Cancelled, package.Status);
        Assert.False(package.IsFinal);
    }

    [Fact]
    public void Download_authorization_is_one_time_and_expires()
    {
        var authorization = new AuditPackageDownloadAuthorization(Guid.NewGuid(), CompanyId, Guid.NewGuid(),
            Guid.NewGuid(), new string('d', 64), FrozenUtc, FrozenUtc.AddMinutes(5));
        authorization.Redeem(FrozenUtc.AddMinutes(1));
        Assert.Throws<InvalidOperationException>(() => authorization.Redeem(FrozenUtc.AddMinutes(2)));

        var expired = new AuditPackageDownloadAuthorization(Guid.NewGuid(), CompanyId, Guid.NewGuid(),
            Guid.NewGuid(), new string('e', 64), FrozenUtc, FrozenUtc.AddMinutes(1));
        Assert.Throws<InvalidOperationException>(() => expired.Redeem(FrozenUtc.AddMinutes(2)));
    }

    [Fact]
    public void Final_package_expires_at_retention_boundary_without_rewriting_evidence_identity()
    {
        var package = NewPackage(Guid.NewGuid());
        package.Approve(Guid.NewGuid(), FrozenUtc.AddMinutes(1));
        Assert.True(package.TryStart(FrozenUtc.AddMinutes(2)));
        var manifestChecksum = new string('b', 64);
        var packageChecksum = new string('c', 64);
        package.Complete("{}", manifestChecksum, packageChecksum, "audit/package.zip",
            "package.zip", "application/zip", 10, true, FrozenUtc.AddMinutes(3));

        package.Expire(package.RetainUntilUtc);

        Assert.Equal(AuditPackageStatuses.Expired, package.Status);
        Assert.Equal(manifestChecksum, package.ManifestChecksum);
        Assert.Equal(packageChecksum, package.PackageChecksum);
        Assert.Equal(ScopeHash, package.ScopeHash);
    }

    [Fact]
    public void Expired_generation_lease_is_reclaimed_after_worker_restart()
    {
        var package = NewPackage(Guid.NewGuid());
        package.Approve(Guid.NewGuid(), FrozenUtc.AddMinutes(1));
        Assert.True(package.TryStart(FrozenUtc.AddMinutes(2), TimeSpan.FromMinutes(5)));
        Assert.False(package.TryStart(FrozenUtc.AddMinutes(6), TimeSpan.FromMinutes(5)));
        Assert.True(package.TryStart(FrozenUtc.AddMinutes(7), TimeSpan.FromMinutes(5)));
        Assert.Equal(2, package.AttemptCount);
    }

    [Fact]
    public void Large_manifest_remains_bounded_and_deterministic()
    {
        var items = Enumerable.Range(0, 1000).Select(index => Included("journal",
            $"ledger/pages/{index:D4}.json", $"page:{index}", $"{{\"page\":{index}}}"));
        var result = Build(items);
        Assert.True(result.IsComplete);
        Assert.Equal(1001, result.Items.Count);
        Assert.True(result.Archive.LongLength < 5 * 1024 * 1024);
    }

    [Fact]
    public void Provider_exception_evidence_uses_safe_projection_without_payload_or_metadata()
    {
        var root = RepositoryRoot();
        var service = File.ReadAllText(Path.Combine(root, "src", "VirtualCompany.Infrastructure.Finance", "Finance", "AuditPackageService.cs"));
        var start = service.IndexOf("var providerExceptions", StringComparison.Ordinal);
        var end = service.IndexOf("var approvals", start, StringComparison.Ordinal);
        var projection = service[start..end];
        Assert.DoesNotContain("PayloadDiffJson", projection, StringComparison.Ordinal);
        Assert.DoesNotContain(".Metadata", projection, StringComparison.Ordinal);
        Assert.Contains("RationaleSummary", projection, StringComparison.Ordinal);
    }

    [Fact]
    public void Restored_archive_verification_matches_database_object_manifest_and_item_hashes()
    {
        var build = Build([
            Included("trial_balance", "ledger/trial-balance.json", "period:2026-08", "{}"),
            Included("approval", "approvals/signoff.json", "approval:42", "{\"approved\":true}")
        ]);
        var artifacts = PersistedArtifacts(build);

        var verification = AuditPackageArchiveVerifier.Verify(build.Archive, 25 * 1024 * 1024,
            artifacts, build.PackageChecksum, build.ManifestChecksum);

        Assert.True(verification.IsValid);
        Assert.Equal("verified", verification.ResultCode);
        Assert.Equal(build.PackageChecksum, verification.PackageChecksum);
        Assert.Equal(build.ManifestChecksum, verification.ManifestChecksum);
        Assert.Equal(0, verification.MissingItemCount);
        Assert.Equal(0, verification.CorruptItemCount);
    }

    [Fact]
    public void Missing_or_corrupt_restored_object_fails_integrity_verification()
    {
        var build = Build([Included("trial_balance", "ledger/trial-balance.json", "period:2026-08", "{}")]);
        var artifacts = PersistedArtifacts(build);
        var missing = AuditPackageArchiveVerifier.Verify([], 25 * 1024 * 1024,
            artifacts, build.PackageChecksum, build.ManifestChecksum);
        var corruptBytes = build.Archive.ToArray();
        corruptBytes[^1] ^= 0xff;
        var corrupt = AuditPackageArchiveVerifier.Verify(corruptBytes, 25 * 1024 * 1024,
            artifacts, build.PackageChecksum, build.ManifestChecksum);

        Assert.False(missing.IsValid);
        Assert.True(missing.MissingItemCount > 0);
        Assert.False(corrupt.IsValid);
        Assert.True(corrupt.CorruptItemCount > 0);
    }

    [Fact]
    public void All_audit_package_entities_have_tenant_query_filters()
    {
        var options = new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite("Data Source=:memory:").Options;
        using var db = new VirtualCompanyDbContext(options);
        var types = new[] { typeof(AuditPackage), typeof(AuditPackageApproval), typeof(AuditPackageArtifact),
            typeof(AuditPackageGenerationAttempt), typeof(AuditPackageDownloadAuthorization), typeof(AuditPackageVerificationResult) };
        Assert.All(types, type => Assert.NotNull(db.Model.FindEntityType(type)?.GetQueryFilter()));
    }

    private static AuditPackageBuildResult Build(IEnumerable<AuditPackageContentItem> items) =>
        AuditPackageArchiveBuilder.Build(CompanyId, PeriodId, "August 2026", "period_close",
            "audit-package-v1", ScopeHash, "{\"ledger\":\"v1\"}", FrozenUtc, items);

    private static AuditPackageContentItem Included(string type, string path, string source, string content) =>
        new(type, path, AuditPackageArtifactStatuses.Included, true, type, source, "v1", "definition-v1", Encoding.UTF8.GetBytes(content));

    private static AuditPackage NewPackage(Guid requester) => new(Guid.NewGuid(), CompanyId, PeriodId,
        "period_close", "audit-package-v1", ScopeHash, "{}", requester, "admin", "request-1",
        FrozenUtc, FrozenUtc.AddYears(7), 4);

    private static AuditPackageArtifact[] PersistedArtifacts(AuditPackageBuildResult build) =>
        build.Items.Select(item => new AuditPackageArtifact(Guid.NewGuid(), CompanyId, Guid.NewGuid(),
            item.Sequence, item.ArtifactType, item.Path, item.Status, item.IsRequired, item.SourceType,
            item.SourceReference, item.SourceVersion, item.DefinitionVersion, item.Sha256,
            item.ContentLength, item.SafeDetail)).ToArray();

    private static string[] ZipEntries(byte[] content)
    {
        using var stream = new MemoryStream(content);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        return archive.Entries.Select(x => x.FullName).ToArray();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
