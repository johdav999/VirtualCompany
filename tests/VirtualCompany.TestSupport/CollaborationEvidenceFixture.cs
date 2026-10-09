using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Tests;

public sealed record CollaborationEvidenceFixture(Guid RootId, Guid ParentId, Guid FinanceId, Guid RevisionId,
    Guid FinanceArtifactId, Guid ProposalArtifactId)
{
    public static async Task<CollaborationEvidenceFixture> SeedAsync(TestWebApplicationFactory factory, Guid company, Guid human)
    {
        CollaborationEvidenceFixture result = null!;
        await factory.SeedAsync(async db =>
        {
            Agent Person(string name, string department) => new(Guid.NewGuid(), company, name.ToLowerInvariant().Replace(' ', '-'), name,
                department + " specialist", department, null, AgentSeniority.Senior, AgentStatus.Active);
            var alex = Person("P11 Alex", "Sales"); var laura = Person("P11 Laura", "Finance");
            var ben = Person("P11 Ben", "Support"); var reviewer = Person("P11 Reviewer", "Sales");
            db.AddRange(alex, laura, ben, reviewer);
            var root = new WorkTask(Guid.NewGuid(), company, "sales_renewal", "P11 renewal proposal review",
                "Renew the customer agreement while preserving margin and service commitments.", WorkTaskPriority.High, alex.Id, null, "user", human);
            var deal = await db.Deals.IgnoreQueryFilters().Where(x => x.CompanyId == company && !x.IsDeleted).Select(x => x.Id).FirstOrDefaultAsync();
            if (deal != Guid.Empty) root.InputPayload["dealId"] = JsonValue.Create(deal);
            root.UpdateStatus(WorkTaskStatus.AwaitingApproval, rationaleSummary: "The revised terms require the accountable human's decision. No delivery is recorded.");
            var parent = new WorkTask(Guid.NewGuid(), company, "manager_worker_collaboration", "P11 renewal collaboration",
                root.Description, WorkTaskPriority.Normal, alex.Id, root.Id, "user", human);
            WorkTask Worker(string title, Agent agent, string type) => new(Guid.NewGuid(), company, type, title, title,
                WorkTaskPriority.Normal, agent.Id, parent.Id, "user", human);
            var finance = Worker("P11 margin check", laura, "finance_review");
            var support = Worker("P11 service history", ben, "support_review");
            var proposal = Worker("P11 proposal revision", alex, "sales_review");
            var challenge = Worker("P11 terms challenge", reviewer, "sales_review");
            finance.UpdateStatus(WorkTaskStatus.Completed); support.UpdateStatus(WorkTaskStatus.Completed);
            proposal.UpdateStatus(WorkTaskStatus.AwaitingApproval); challenge.UpdateStatus(WorkTaskStatus.AwaitingApproval);
            db.AddRange(root, parent, finance, support, proposal, challenge);
            var plan = Guid.NewGuid();
            CollaborationContribution Artifact(WorkTask task, Agent agent, int sequence, int version, string status,
                string output, string rationale, OperatingCollaborationRole role = OperatingCollaborationRole.Contributor,
                OperatingCollaborationPattern pattern = OperatingCollaborationPattern.Parallel) => new(company, parent.Id,
                    task.Id, plan, agent.Id, sequence, version, role, pattern, task.Title, status, output, rationale,
                    role == OperatingCollaborationRole.Contributor ? null : rationale);
            var margin = Artifact(finance, laura, 1, 1, "completed", "The supported discount ceiling is 8%.", "Protect the recorded margin floor.");
            var failed = Artifact(support, ben, 2, 1, "failed", "", "Service-history evidence was temporarily unavailable.");
            var history = Artifact(support, ben, 2, 2, "completed", "The open service commitment must be included in the renewal.", "Retained case history supports this commitment.");
            var original = Artifact(proposal, alex, 3, 1, "completed", "Initial proposal requested 12% discount.", "Initial Sales contribution; not approved.");
            var revised = Artifact(proposal, alex, 3, 2, "needs_review", "Revised proposal uses 8% discount and the service commitment.",
                "Revision incorporates the permitted Finance and Support versions.", pattern: OperatingCollaborationPattern.SequentialHandoff);
            var challenged = Artifact(challenge, reviewer, 4, 1, "needs_review", "Human review is required before customer delivery.",
                "Sales originally requested 12%; Finance supports 8%. The revision uses 8%; the human decision is still pending.",
                OperatingCollaborationRole.Challenger, OperatingCollaborationPattern.SequentialHandoff);
            db.AddRange(margin, failed, history, original, revised, challenged);
            db.AddRange(new CollaborationArtifactHandoff(company, margin.Id, revised.Id, true),
                new CollaborationArtifactHandoff(company, history.Id, revised.Id, true),
                new CollaborationArtifactHandoff(company, revised.Id, challenged.Id, false, "The revised proposal needs a human decision."));
            var approval = ApprovalRequest.CreateForTarget(Guid.NewGuid(), company, ApprovalTargetEntityType.Task, root.Id,
                "user", human, "renewal_terms", new Dictionary<string, JsonNode?>() { ["reason"] = JsonValue.Create("Review the revised renewal terms.") }, null, human, []);
            db.Add(approval);
            result = new(root.Id, parent.Id, finance.Id, proposal.Id, margin.Id, revised.Id);
        });
        return result;
    }
}
