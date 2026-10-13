using System.Text.Json.Nodes;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Features.Agents;

public sealed record BriefingCategoryDefinition(
    string Key,
    string ShortLabel,
    string LabelResourceKey,
    string DescriptionResourceKey,
    string PlaceholderResourceKey);


public sealed class AgentBriefEditorState
{
    public AgentBriefFormModel AgentBriefModel { get; set; } = new();
    public List<AgentBriefDocumentViewModel> BriefDocuments { get; set; } = [];
    public string SelectedBriefCategory { get; set; } = "company_information";
    public bool ShareBriefDocumentsWithTeam { get; set; }
    public bool IsBriefDirty { get; set; }
    public bool IsSavingBrief { get; set; }
    public bool IsGeneratingBriefDraft { get; set; }
    public bool IsGeneratedBriefDraft { get; set; }
    public bool IsUploadingBriefDocument { get; set; }
    public string? BriefErrorMessage { get; set; }
    public string? BriefSuccessMessage { get; set; }
    public static readonly IReadOnlyList<BriefingCategoryDefinition> Categories =
    [
        new("company_information", "CO", "BriefCategoryCompanyLabel", "BriefCategoryCompanyDescription", "BriefCategoryCompanyPlaceholder"),
        new("products_and_services", "PR", "BriefCategoryProductsLabel", "BriefCategoryProductsDescription", "BriefCategoryProductsPlaceholder"),
        new("policies", "PO", "BriefCategoryPoliciesLabel", "BriefCategoryPoliciesDescription", "BriefCategoryPoliciesPlaceholder"),
        new("customer_support", "CS", "BriefCategorySupportLabel", "BriefCategorySupportDescription", "BriefCategorySupportPlaceholder"),
        new("other_instructions", "IN", "BriefCategoryOtherLabel", "BriefCategoryOtherDescription", "BriefCategoryOtherPlaceholder")
    ];

    public BriefingCategoryDefinition SelectedCategory => Categories.First(x => x.Key == SelectedBriefCategory);
    public string CurrentText { get => AgentBriefModel.Get(SelectedBriefCategory); set => AgentBriefModel.Set(SelectedBriefCategory, value); }
    public IReadOnlyList<AgentBriefDocumentViewModel> DocumentsForCategory(Guid? agentId) => AgentBriefDocumentStatusPresenter.Deduplicate(BriefDocuments.Where(document => IsBriefDocumentForAgentAndCategory(document, agentId, SelectedBriefCategory))).OrderByDescending(x => x.UpdatedUtc).ToList();
    public void SelectBriefCategory(string category)
    {
        SelectedBriefCategory = category;
        BriefErrorMessage = null;
        BriefSuccessMessage = null;
        IsGeneratedBriefDraft = false;
    }

    public void MarkBriefDirty()
    {
        IsBriefDirty = true;
        BriefSuccessMessage = null;
    }

    public void SetText(string? text)
    {
        CurrentText = text ?? string.Empty;
        MarkBriefDirty();
    }
    private static bool IsBriefDocumentForAgentAndCategory(
        AgentBriefDocumentViewModel document,
        Guid? agentId,
        string category)
    {
        if (!agentId.HasValue ||
            !TryGetMetadataString(document.Metadata, "purpose", out var purpose) ||
            !string.Equals(purpose, "agent_brief", StringComparison.OrdinalIgnoreCase) ||
            !TryGetMetadataString(document.Metadata, "briefingCategory", out var metadataCategory))
        {
            return false;
        }

        var belongsToAgent =
            TryGetMetadataString(document.Metadata, "agentId", out var metadataAgentId) &&
            Guid.TryParse(metadataAgentId, out var parsedAgentId) &&
            parsedAgentId == agentId.Value;
        var sharedWithTeam = TryGetMetadataBoolean(document.Metadata, "shareWithAgentTeam", out var shared) && shared;

        return (belongsToAgent || sharedWithTeam) &&
               string.Equals(metadataCategory, category, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetMetadataString(
        IReadOnlyDictionary<string, JsonNode?> metadata,
        string key,
        out string value)
    {
        value = string.Empty;
        if (!metadata.TryGetValue(key, out var node) ||
            node is not JsonValue jsonValue ||
            !jsonValue.TryGetValue<string>(out var candidate) ||
            string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        value = candidate;
        return true;
    }

    private static bool TryGetMetadataBoolean(
        IReadOnlyDictionary<string, JsonNode?> metadata,
        string key,
        out bool value)
    {
        value = false;
        return metadata.TryGetValue(key, out var node) &&
               node is JsonValue jsonValue &&
               jsonValue.TryGetValue<bool>(out value);
    }

}
