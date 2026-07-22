namespace VirtualCompany.Domain.Entities;
public sealed class SalesSequenceStep : ICompanyOwnedEntity
{
    private SalesSequenceStep()
    {
    }

    public SalesSequenceStep(
        Guid id,
        Guid companyId,
        Guid salesSequenceId,
        int stepOrder,
        int delayDays,
        string templateContent,
        string channel = "email",
        string? templateSubject = null,
        bool aiPersonalizationEnabled = true,
        DateTime? createdUtc = null,
        DateTime? updatedUtc = null)
    {
        SalesEntityText.EnsureCompany(companyId);
        if (stepOrder <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stepOrder), "Step order must be positive.");
        }

        if (delayDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(delayDays), "Delay days cannot be negative.");
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SalesSequenceId = salesSequenceId == Guid.Empty ? throw new ArgumentException("SalesSequenceId is required.", nameof(salesSequenceId)) : salesSequenceId;
        StepOrder = stepOrder;
        DelayDays = delayDays;
        Channel = SalesEntityText.NormalizeRequired(channel, nameof(channel), 32).ToLowerInvariant();
        TemplateSubject = SalesEntityText.NormalizeOptional(templateSubject, nameof(templateSubject), 300);
        TemplateContent = SalesEntityText.NormalizeRequired(templateContent, nameof(templateContent), 8000);
        AiPersonalizationEnabled = aiPersonalizationEnabled;
        CreatedUtc = SalesEntityText.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
        UpdatedUtc = SalesEntityText.NormalizeUtc(updatedUtc ?? CreatedUtc, nameof(updatedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SalesSequenceId { get; private set; }
    public int StepOrder { get; private set; }
    public int DelayDays { get; private set; }
    public string Channel { get; private set; } = null!;
    public string? TemplateSubject { get; private set; }
    public string TemplateContent { get; private set; } = null!;
    public bool AiPersonalizationEnabled { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public SalesSequence SalesSequence { get; private set; } = null!;

    public void Update(int stepOrder, int delayDays, string channel, string? templateSubject, string templateContent, bool aiPersonalizationEnabled)
    {
        if (stepOrder <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stepOrder), "Step order must be positive.");
        }

        if (delayDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(delayDays), "Delay days cannot be negative.");
        }

        StepOrder = stepOrder;
        DelayDays = delayDays;
        Channel = SalesEntityText.NormalizeRequired(channel, nameof(channel), 32).ToLowerInvariant();
        TemplateSubject = SalesEntityText.NormalizeOptional(templateSubject, nameof(templateSubject), 300);
        TemplateContent = SalesEntityText.NormalizeRequired(templateContent, nameof(templateContent), 8000);
        AiPersonalizationEnabled = aiPersonalizationEnabled;
        UpdatedUtc = DateTime.UtcNow;
    }
}

