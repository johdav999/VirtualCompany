using VirtualCompany.Domain.ValueObjects;

namespace VirtualCompany.Domain.Entities;
public sealed class Contact : ICompanyOwnedEntity
{
    private Contact()
    {
    }

    public Contact(
        Guid id,
        Guid companyId,
        string fullName,
        string email,
        Guid? customerCompanyId = null,
        string status = SalesStatuses.Active,
        string? title = null,
        string? phone = null,
        DateTime? createdUtc = null,
        DateTime? updatedUtc = null,
        string? preferredLanguage = null)
    {
        SalesEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        CustomerCompanyId = SalesEntityText.NormalizeOptionalId(customerCompanyId, nameof(customerCompanyId));
        FullName = SalesEntityText.NormalizeRequired(fullName, nameof(fullName), 160);
        Email = SalesEntityText.NormalizeRequired(email, nameof(email), 256).ToLowerInvariant();
        Status = SalesEntityText.NormalizeRequired(status, nameof(status), 32).ToLowerInvariant();
        Title = SalesEntityText.NormalizeOptional(title, nameof(title), 120);
        Phone = SalesEntityText.NormalizeOptional(phone, nameof(phone), 64);
        PreferredLanguage = CommunicationLanguageTag.NormalizeOptional(preferredLanguage, nameof(preferredLanguage));
        CreatedUtc = SalesEntityText.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
        UpdatedUtc = SalesEntityText.NormalizeUtc(updatedUtc ?? CreatedUtc, nameof(updatedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? CustomerCompanyId { get; private set; }
    public string FullName { get; private set; } = null!;
    public string Email { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public string? Title { get; private set; }
    public string? Phone { get; private set; }
    public string? PreferredLanguage { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public CustomerCompany? CustomerCompany { get; private set; }

    public void SoftDelete()
    {
        if (IsDeleted)
        {
            return;
        }

        IsDeleted = true;
        DeletedUtc = DateTime.UtcNow;
        UpdatedUtc = DeletedUtc.Value;
    }

    public void SetPreferredLanguage(string? language)
    {
        PreferredLanguage = CommunicationLanguageTag.NormalizeOptional(language, nameof(language));
        UpdatedUtc = DateTime.UtcNow;
    }

    public void UpdateIdentity(string fullName, string email, string? title, string? phone)
    {
        FullName = SalesEntityText.NormalizeRequired(fullName, nameof(fullName), 160);
        Email = SalesEntityText.NormalizeRequired(email, nameof(email), 256).ToLowerInvariant();
        Title = SalesEntityText.NormalizeOptional(title, nameof(title), 120);
        Phone = SalesEntityText.NormalizeOptional(phone, nameof(phone), 64);
        UpdatedUtc = DateTime.UtcNow;
    }

    public void AssignCustomerCompany(Guid customerCompanyId)
    {
        CustomerCompanyId = SalesEntityText.NormalizeOptionalId(customerCompanyId, nameof(customerCompanyId));
        UpdatedUtc = DateTime.UtcNow;
    }

    public void RestoreActiveState(
        string fullName,
        string email,
        Guid? customerCompanyId,
        string? title,
        string? phone,
        string? preferredLanguage,
        DateTime updatedUtc)
    {
        FullName = SalesEntityText.NormalizeRequired(fullName, nameof(fullName), 160);
        Email = SalesEntityText.NormalizeRequired(email, nameof(email), 256).ToLowerInvariant();
        CustomerCompanyId = SalesEntityText.NormalizeOptionalId(customerCompanyId, nameof(customerCompanyId));
        Status = SalesStatuses.Active;
        Title = SalesEntityText.NormalizeOptional(title, nameof(title), 120);
        Phone = SalesEntityText.NormalizeOptional(phone, nameof(phone), 64);
        PreferredLanguage = CommunicationLanguageTag.NormalizeOptional(preferredLanguage, nameof(preferredLanguage));
        IsDeleted = false;
        DeletedUtc = null;
        UpdatedUtc = SalesEntityText.NormalizeUtc(updatedUtc, nameof(updatedUtc));
    }

}
