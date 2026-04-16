using System.Text.Json.Nodes;

namespace VirtualCompany.Domain.Entities;

public sealed class DashboardDepartmentConfig : ICompanyOwnedEntity
{
    private const int DepartmentMaxLength = 64;
    private const int DisplayNameMaxLength = 128;
    private const int IconMaxLength = 64;

    private DashboardDepartmentConfig()
    {
    }

    public DashboardDepartmentConfig(
        Guid id,
        Guid companyId,
        string department,
        string displayName,
        int displayOrder,
        bool isEnabled,
        string? icon,
        IDictionary<string, JsonNode?>? navigation,
        IDictionary<string, JsonNode?>? visibility,
        IDictionary<string, JsonNode?>? emptyState)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (displayOrder < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(displayOrder), "Display order cannot be negative.");
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        Department = NormalizeRequired(department, nameof(department), DepartmentMaxLength).ToLowerInvariant();
        DisplayName = NormalizeRequired(displayName, nameof(displayName), DisplayNameMaxLength);
        DisplayOrder = displayOrder;
        IsEnabled = isEnabled;
        Icon = NormalizeOptional(icon, nameof(icon), IconMaxLength);
        Navigation = CloneNodes(navigation);
        Visibility = CloneNodes(visibility);
        EmptyState = CloneNodes(emptyState);
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Department { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public int DisplayOrder { get; private set; }
    public bool IsEnabled { get; private set; }
    public string? Icon { get; private set; }
    public Dictionary<string, JsonNode?> Navigation { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> Visibility { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> EmptyState { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public ICollection<DashboardWidgetConfig> Widgets { get; } = new List<DashboardWidgetConfig>();

    public void AddWidget(DashboardWidgetConfig widget)
    {
        if (widget.CompanyId != CompanyId)
        {
            throw new InvalidOperationException("Dashboard widget company must match the department dashboard company.");
        }

        Widgets.Add(widget);
        UpdatedUtc = DateTime.UtcNow;
    }

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static Dictionary<string, JsonNode?> CloneNodes(IDictionary<string, JsonNode?>? nodes) =>
        nodes is null || nodes.Count == 0
            ? new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            : nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
}

public sealed class DashboardWidgetConfig : ICompanyOwnedEntity
{
    private const int WidgetKeyMaxLength = 128;
    private const int TitleMaxLength = 160;
    private const int WidgetTypeMaxLength = 64;
    private const int SummaryBindingMaxLength = 128;

    private DashboardWidgetConfig()
    {
    }

    public DashboardWidgetConfig(
        Guid id,
        Guid companyId,
        Guid departmentConfigId,
        string widgetKey,
        string title,
        string widgetType,
        int displayOrder,
        bool isEnabled,
        string summaryBinding,
        IDictionary<string, JsonNode?>? navigation,
        IDictionary<string, JsonNode?>? visibility,
        IDictionary<string, JsonNode?>? emptyState)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (departmentConfigId == Guid.Empty)
        {
            throw new ArgumentException("DepartmentConfigId is required.", nameof(departmentConfigId));
        }

        if (displayOrder < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(displayOrder), "Display order cannot be negative.");
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        DepartmentConfigId = departmentConfigId;
        WidgetKey = NormalizeRequired(widgetKey, nameof(widgetKey), WidgetKeyMaxLength).ToLowerInvariant();
        Title = NormalizeRequired(title, nameof(title), TitleMaxLength);
        WidgetType = NormalizeRequired(widgetType, nameof(widgetType), WidgetTypeMaxLength);
        DisplayOrder = displayOrder;
        IsEnabled = isEnabled;
        SummaryBinding = NormalizeRequired(summaryBinding, nameof(summaryBinding), SummaryBindingMaxLength);
        Navigation = CloneNodes(navigation);
        Visibility = CloneNodes(visibility);
        EmptyState = CloneNodes(emptyState);
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid DepartmentConfigId { get; private set; }
    public string WidgetKey { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public string WidgetType { get; private set; } = null!;
    public int DisplayOrder { get; private set; }
    public bool IsEnabled { get; private set; }
    public string SummaryBinding { get; private set; } = null!;
    public Dictionary<string, JsonNode?> Navigation { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> Visibility { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> EmptyState { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DashboardDepartmentConfig DepartmentConfig { get; private set; } = null!;
    public Company Company { get; private set; } = null!;

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static Dictionary<string, JsonNode?> CloneNodes(IDictionary<string, JsonNode?>? nodes) =>
        nodes is null || nodes.Count == 0
            ? new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            : nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
}