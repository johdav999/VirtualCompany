namespace VirtualCompany.Domain.Entities;
public static class AnnualPlanStates { public const string Draft="draft", Reviewed="reviewed", Approved="approved"; }
public sealed class AnnualPlanVersion : ICompanyOwnedEntity
{
    public Guid Id {get;init;} public Guid CompanyId {get;init;} public int FiscalYear {get;init;} public int Version {get;init;}
    public Guid? PreviousId {get;init;} public Guid RequestId {get;init;} public Guid AuthorUserId {get;init;} public DateTime SavedUtc {get;init;}
    public DateTime StartUtc {get;init;} public DateTime EndUtc {get;init;} public string Currency {get;init;}=""; public string Timezone {get;init;}="";
    public int StartMonth {get;init;} public int StartDay {get;init;} public long CalendarVersion {get;init;} public string Notes {get;init;}=""; public string Fingerprint {get;init;}=""; public string CommandHash {get;init;}="";
    public string Status {get;private set;}=AnnualPlanStates.Draft; public int StateRevision {get;private set;}=1; public Guid? ApprovalId {get;private set;}
    public DateTime? ReviewedUtc {get;private set;} public DateTime? DecidedUtc {get;private set;}
    public List<AnnualObjective> Objectives {get;init;}=[]; public List<AnnualBudgetBinding> Budgets {get;init;}=[]; public List<AnnualAllocation> Allocations {get;init;}=[];
    public void Review(DateTime utc) {if(Status!=AnnualPlanStates.Draft)throw new InvalidOperationException("Only a draft can be reviewed.");Status=AnnualPlanStates.Reviewed;ReviewedUtc=utc;StateRevision++;}
    public void BindApproval(Guid id) {if(Status!=AnnualPlanStates.Reviewed||ApprovalId.HasValue)throw new InvalidOperationException("Review already has an approval binding.");ApprovalId=id;}
    public void Decide(Guid approval,string status,DateTime utc) {if(ApprovalId!=approval)throw new InvalidOperationException("Approval does not bind this annual version.");Status=status;DecidedUtc=utc;StateRevision++;}
}
public sealed class AnnualObjective : ICompanyOwnedEntity
{
    public Guid Id {get;init;} public Guid CompanyId {get;init;} public Guid PlanId {get;init;} public Guid GoalId {get;init;} public int GoalVersion {get;init;}
    public Guid OwnerUserId {get;init;} public string OwnerName {get;init;}=""; public string Name {get;init;}=""; public string MetricKey {get;init;}=""; public string Unit {get;init;}="";
    public Guid BaselineReviewId {get;init;} public string SourceFingerprint {get;init;}=""; public decimal Baseline {get;init;} public decimal Target {get;init;}
    public List<AnnualMilestone> Milestones {get;init;}=[]; public List<AnnualDependency> Dependencies {get;init;}=[];
}
public sealed class AnnualMilestone : ICompanyOwnedEntity {public Guid Id {get;init;} public Guid CompanyId {get;init;} public Guid ObjectiveId {get;init;} public int Quarter {get;init;} public string Title {get;init;}=""; public DateTime DueUtc {get;init;}}
public sealed class AnnualDependency : ICompanyOwnedEntity {public Guid Id {get;init;} public Guid CompanyId {get;init;} public Guid ObjectiveId {get;init;} public Guid InitiativeId {get;init;} public string Description {get;init;}="";}
public sealed class AnnualBudgetBinding : ICompanyOwnedEntity {public Guid Id {get;init;} public Guid CompanyId {get;init;} public Guid PlanId {get;init;} public Guid BudgetId {get;init;} public Guid AccountId {get;init;} public string Account {get;init;}=""; public DateTime MonthUtc {get;init;} public string NativeVersion {get;init;}=""; public string Currency {get;init;}=""; public decimal Amount {get;init;} public Guid? CostCenterId {get;init;} public string SourceFingerprint {get;init;}="";}
public sealed class AnnualAllocation : ICompanyOwnedEntity {public Guid Id {get;init;} public Guid CompanyId {get;init;} public Guid PlanId {get;init;} public Guid BudgetId {get;init;} public Guid OwnerUserId {get;init;} public Guid? GoalId {get;init;} public string Kind {get;init;}=""; public string Title {get;init;}=""; public decimal Amount {get;init;} public int Quarter {get;init;}}



