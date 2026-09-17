using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class SalesPresentationPreset : ICompanyOwnedEntity
{
    private SalesPresentationPreset() { }
    public SalesPresentationPreset(Guid id, Guid companyId, string name, string? description, Guid ownerUserId, DateTime nowUtc)
    {
        RequiredId(companyId, nameof(companyId)); RequiredId(ownerUserId, nameof(ownerUserId));
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; Name = Required(name, 200);
        Description = Optional(description, 2000); OwnerUserId = ownerUserId; Lifecycle = SalesPresentationPresetLifecycle.Draft;
        CreatedUtc = UpdatedUtc = Utc(nowUtc); ConcurrencyVersion = 1;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public SalesPresentationPresetLifecycle Lifecycle { get; private set; }
    public Guid? CurrentPublishedVersionId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? ArchivedUtc { get; private set; }
    public long ConcurrencyVersion { get; private set; }
    public Company Company { get; private set; } = null!;
    public SalesPresentationPresetVersion? CurrentPublishedVersion { get; private set; }
    public ICollection<SalesPresentationPresetVersion> Versions { get; } = new List<SalesPresentationPresetVersion>();
    public void Update(string name, string? description, Guid ownerUserId, DateTime nowUtc)
    {
        if (Lifecycle == SalesPresentationPresetLifecycle.Archived) throw new InvalidOperationException("Archived presets cannot be edited.");
        RequiredId(ownerUserId, nameof(ownerUserId)); Name = Required(name, 200); Description = Optional(description, 2000);
        OwnerUserId = ownerUserId; Touch(nowUtc);
    }
    public void Publish(Guid versionId, DateTime nowUtc)
    {
        if (Lifecycle == SalesPresentationPresetLifecycle.Archived) throw new InvalidOperationException("Archived presets cannot be published.");
        RequiredId(versionId, nameof(versionId)); CurrentPublishedVersionId = versionId; Lifecycle = SalesPresentationPresetLifecycle.Published; Touch(nowUtc);
    }
    public void Archive(DateTime nowUtc)
    {
        if (Lifecycle == SalesPresentationPresetLifecycle.Archived) return;
        Lifecycle = SalesPresentationPresetLifecycle.Archived; ArchivedUtc = Utc(nowUtc); Touch(nowUtc);
    }
    private void Touch(DateTime nowUtc) { UpdatedUtc = Utc(nowUtc); ConcurrencyVersion++; }
    internal static string Required(string? value, int max) { var result = value?.Trim(); if (string.IsNullOrEmpty(result)) throw new ArgumentException("A required value is missing."); if (result.Length > max) throw new ArgumentOutOfRangeException(nameof(value)); return result; }
    internal static string? Optional(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : Required(value, max);
    internal static void RequiredId(Guid value, string name) { if (value == Guid.Empty) throw new ArgumentException($"{name} is required.", name); }
    internal static DateTime Utc(DateTime value) => value == default ? throw new ArgumentException("A timestamp is required.") : value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
