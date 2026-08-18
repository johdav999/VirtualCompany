using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.GuidedWork;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FinanceBudgetGuidedArtifactDefinition(
    IFinanceCommandService finance, VirtualCompanyDbContext db, IAgentCapabilityCatalog capabilities) : IGuidedArtifactDefinition
{
    public string ArtifactType=>GuidedArtifactTypes.FinanceBudget; public string SchemaVersion=>"1.0"; public string DisplayName=>"Finance budget line";
    public IReadOnlyList<string> QuestionPriorities=>["period_start","finance_account_id","amount","currency","version","cost_center_id"];
    public IReadOnlyList<GuidedFieldDefinition> Fields {get;}=
    [
        new("finance_account_id","Finance account","Select an existing finance account.",GuidedFieldValueTypes.Identifier,true),
        new("period_start","Period start","Set the budget period start date.",GuidedFieldValueTypes.Date,true),
        new("version","Budget version","Name the planning version, for example Base or Revised.",GuidedFieldValueTypes.Text,true,MaxLength:64),
        new("amount","Amount","Set the budget amount for this account and period.",GuidedFieldValueTypes.Number,true),
        new("currency","Currency","Use a three-letter ISO currency code.",GuidedFieldValueTypes.Text,true,MaxLength:3),
        new("cost_center_id","Cost center","Optionally provide the current planning cost-center identifier.",GuidedFieldValueTypes.Identifier,false)
    ];
    public async Task EnsureEligibleAsync(Guid c,Guid a,CancellationToken ct){var catalog=await capabilities.GetEffectiveCatalogAsync(c,a,ct);var cap=catalog.Capabilities.SingleOrDefault(x=>x.Id==AgentCapabilityIds.FinanceOperatingCadence);if(cap is null||cap.State is AgentCapabilityStates.PermissionDenied or AgentCapabilityStates.NotImplemented)throw new GuidedArtifactNotEligibleException("The selected agent does not have the tools and data permissions required for Finance budget workshops.");}
    public async Task<GuidedArtifactInitialization> InitializeAsync(Guid companyId,Guid agentId,Guid? targetId,CancellationToken ct)
    {
        var values=new Dictionary<string,JsonNode?>(StringComparer.OrdinalIgnoreCase);
        if(targetId is Guid id)
        {
            var budget=await db.Budgets.AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==companyId&&x.Id==id,ct)??throw new KeyNotFoundException("Budget line not found.");
            values["finance_account_id"]=budget.FinanceAccountId.ToString();values["period_start"]=budget.PeriodStartUtc.ToString("yyyy-MM-dd");values["version"]=budget.Version;values["amount"]=budget.Amount;values["currency"]=budget.Currency;if(budget.CostCenterId is Guid cost)values["cost_center_id"]=cost.ToString();
            return new(DisplayName,id,budget.UpdatedUtc.ToString("O"),values,OpeningSummary:"Review the existing budget line and its assumptions.",OpeningQuestion:"What business activity should this budget amount support?");
        }
        return new(DisplayName,null,null,values,OpeningSummary:"Create one budget line using the existing Finance planning model.",OpeningQuestion:"Which finance account and period should this budget line cover?");
    }
    public async Task<IReadOnlyList<string>> ValidateAsync(Guid companyId,Guid agentId,Guid? targetId,IReadOnlyDictionary<string,JsonNode?> values,CancellationToken ct)
    {
        var gaps=Fields.Where(x=>x.IsRequired&&(!values.TryGetValue(x.Path,out var v)||v is null||string.IsNullOrWhiteSpace(v.ToString()))).Select(x=>x.Label).ToList();
        if(Guid.TryParse(Text(values,"finance_account_id"),out var account)&&!await db.FinanceAccounts.AsNoTracking().AnyAsync(x=>x.CompanyId==companyId&&x.Id==account,ct))gaps.Add("Finance account is not available in this company");
        if(Text(values,"currency") is {Length:>0} currency&&(currency.Length!=3||!currency.All(char.IsLetter)))gaps.Add("Currency must be a three-letter ISO code");
        return gaps;
    }
    public Task<IReadOnlyList<GuidedReviewInsightDto>> BuildReviewInsightsAsync(Guid c,Guid a,Guid? t,IReadOnlyDictionary<string,JsonNode?>v,CancellationToken ct)=>Task.FromResult<IReadOnlyList<GuidedReviewInsightDto>>([new("Budget line",$"{Text(v,"amount")} {Text(v,"currency")}", $"Applies to the selected Finance account from {Text(v,"period_start")}. Finance owns totals and validation."),new("Confirmation effect","Save budget planning entry only","Confirmation does not approve spending, pay, post accounting entries, or alter forecasts.")]);
    public async Task<GuidedArtifactCommitResult> CommitAsync(GuidedArtifactCommitContext c,CancellationToken ct)
    {
        if(c.TargetArtifactId is Guid existingId&&!string.IsNullOrWhiteSpace(c.TargetArtifactVersion))
        {var updated=await db.Budgets.AsNoTracking().Where(x=>x.CompanyId==c.CompanyId&&x.Id==existingId).Select(x=>(DateTime?)x.UpdatedUtc).SingleOrDefaultAsync(ct);if(updated is null||updated.Value.ToString("O")!=c.TargetArtifactVersion)throw new GuidedWorkConflictException("The budget line changed after this workshop started.");}
        var input=new FinancePlanningEntryUpsertDto(Guid.Parse(Text(c.Values,"finance_account_id")),DateTime.SpecifyKind(DateTime.Parse(Text(c.Values,"period_start")),DateTimeKind.Utc),Text(c.Values,"version"),decimal.Parse(c.Values["amount"]!.ToString()),Text(c.Values,"currency"),Guid.TryParse(Text(c.Values,"cost_center_id"),out var cost)?cost:null);
        var result=c.TargetArtifactId is Guid id?await finance.UpdateBudgetAsync(new(c.CompanyId,id,input),ct):await finance.CreateBudgetAsync(new(c.CompanyId,input),ct);
        return new(result.Id,result.UpdatedUtc.ToString("O"),$"Saved {result.AccountName} budget for {result.PeriodStartUtc:yyyy-MM-dd}.");
    }
    private static string Text(IReadOnlyDictionary<string,JsonNode?>v,string k)=>v.TryGetValue(k,out var n)?n?.ToString().Trim('"')??"":"";
}
