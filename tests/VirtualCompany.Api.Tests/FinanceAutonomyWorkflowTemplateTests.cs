using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using VirtualCompany.Api.Controllers;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Companies;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceAutonomyWorkflowTemplateTests
{
    public static IEnumerable<object[]> Templates() =>
        FinanceAutonomyWorkflowTemplateCatalogue.All.Select(x => new object[] { x });

    [Fact]
    public void Catalogue_contains_exactly_eight_complete_conservative_reviewed_templates()
    {
        var templates = FinanceAutonomyWorkflowTemplateCatalogue.All;

        Assert.Equal(8, templates.Count);
        Assert.Equal(8, templates.Select(x => x.Code).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(8, templates.Select(x => x.CapabilityId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(templates, template =>
        {
            Assert.Equal(FinanceAutonomyWorkflowTemplateVersions.V1, template.Version);
            Assert.NotEmpty(template.Triggers);
            Assert.NotEmpty(template.Evidence);
            Assert.NotEmpty(template.ToolName);
            Assert.Contains(template.ActionClass, new[] { "read", "recommend" });
            Assert.NotEmpty(template.Outputs);
            Assert.NotEmpty(template.StopConditions);
            Assert.NotEmpty(template.ExpectedTasksOrDrafts);
            Assert.NotEmpty(template.OwnerRole);
            Assert.NotEmpty(template.ApprovalBehavior);
            Assert.NotEmpty(template.Name.En);
            Assert.NotEmpty(template.Name.Sv);
            Assert.NotEmpty(template.TaskTitle.En);
            Assert.NotEmpty(template.TaskTitle.Sv);
            Assert.NotEmpty(template.NextHumanAction.En);
            Assert.NotEmpty(template.NextHumanAction.Sv);
            Assert.InRange(template.Limits.MaximumRecordsPerRun, 1, 100);
            Assert.InRange(template.Limits.MaximumActionsPerRun, 1, 1);
            Assert.Contains("accounting_posting", template.UnsupportedEffects);
            Assert.Contains("payment_or_money_movement", template.UnsupportedEffects);
            Assert.Contains("final_close_or_year_end", template.UnsupportedEffects);
            Assert.Contains("statutory_filing_or_signoff", template.UnsupportedEffects);
            Assert.Contains("provider_or_credential_change", template.UnsupportedEffects);
            Assert.Contains("external_communication", template.UnsupportedEffects);
        });
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void Every_template_classifies_healthy_exception_stale_and_missing_evidence(
        FinanceAutonomyWorkflowTemplate template)
    {
        var now = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
        var healthy = new Dictionary<string, JsonNode?>
        {
            ["isHealthy"] = true,
            ["items"] = new JsonArray()
        };
        var exception = new Dictionary<string, JsonNode?>
        {
            ["exceptionCount"] = 1
        };

        Assert.Equal(FinanceAutonomyWorkflowOutcomeStates.Healthy,
            FinanceAutonomyExecutor.ClassifyWorkflowOutcomeForTest(
                FinanceAutonomyTriggers.Schedule, now, template, healthy, now));
        Assert.Equal(FinanceAutonomyWorkflowOutcomeStates.Exception,
            FinanceAutonomyExecutor.ClassifyWorkflowOutcomeForTest(
                FinanceAutonomyTriggers.Schedule, now, template, exception, now));
        Assert.Equal(FinanceAutonomyWorkflowOutcomeStates.Stale,
            FinanceAutonomyExecutor.ClassifyWorkflowOutcomeForTest(
                FinanceAutonomyTriggers.Schedule,
                now.AddMinutes(-template.Limits.EvidenceFreshnessMinutes - 1), template, healthy, now));
        Assert.Equal(FinanceAutonomyWorkflowOutcomeStates.Missing,
            FinanceAutonomyExecutor.ClassifyWorkflowOutcomeForTest(
                FinanceAutonomyTriggers.Schedule, now, template, null, now));
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void Every_event_enabled_template_routes_an_authoritative_event_to_review(
        FinanceAutonomyWorkflowTemplate template)
    {
        if (!template.Triggers.Contains(FinanceAutonomyTriggers.BusinessEvent)) return;
        var now = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);

        Assert.NotEmpty(template.EventTypes);
        Assert.Equal(FinanceAutonomyWorkflowOutcomeStates.Exception,
            FinanceAutonomyExecutor.ClassifyWorkflowOutcomeForTest(
                FinanceAutonomyTriggers.BusinessEvent, now, template,
                new Dictionary<string, JsonNode?> { ["isHealthy"] = true }, now));
        Assert.All(template.EventTypes, eventType =>
            Assert.Same(template, FinanceAutonomyWorkflowTemplateCatalogue.Resolve(
                template.CapabilityId, FinanceAutonomyTriggers.BusinessEvent, eventType)));
    }

    [Fact]
    public void Close_readiness_blocking_count_is_classified_as_an_exception()
    {
        var template = FinanceAutonomyWorkflowTemplateCatalogue.Find(
            FinanceAutonomyWorkflowTemplateCodes.CloseBlockers)!;
        var now = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);

        Assert.Equal(FinanceAutonomyWorkflowOutcomeStates.Exception,
            FinanceAutonomyExecutor.ClassifyWorkflowOutcomeForTest(
                FinanceAutonomyTriggers.Schedule, now, template,
                new Dictionary<string, JsonNode?> { ["blockingCount"] = 1 }, now));
    }

    [Fact]
    public void Sensitive_requested_effects_are_not_part_of_any_template_authority()
    {
        var sensitiveTools = new[]
        {
            "post_paid_supplier_bill_expense", "categorize_transaction", "approve_invoice",
            FinanceOperationalProposalAgentToolIds.AssignCloseTask,
            FinanceOperationalProposalAgentToolIds.RequestEvidence,
            FinanceOperationalProposalAgentToolIds.RequestAuditPackageGeneration
        };

        Assert.All(FinanceAutonomyWorkflowTemplateCatalogue.All, template =>
        {
            Assert.DoesNotContain(template.ToolName, sensitiveTools, StringComparer.OrdinalIgnoreCase);
            Assert.NotEqual("execute", template.ActionClass);
        });

        var rejected = FinanceAutonomyWorkflowTemplateService.FindUnsupportedRequestedEffects(
            ["read", "create_internal_task", "payment", "filing", "external_communication"]);
        Assert.Equal(["external_communication", "filing", "payment"], rejected);
    }

    [Fact]
    public void Api_exposes_safe_preview_and_manager_only_prospective_draft_without_activation_endpoint()
    {
        var controllerPolicy = Assert.Single(typeof(FinanceAutonomyWorkflowTemplatesController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal(CompanyPolicies.FinanceView, controllerPolicy.Policy);
        var draft = typeof(FinanceAutonomyWorkflowTemplatesController)
            .GetMethod(nameof(FinanceAutonomyWorkflowTemplatesController.CreateDraft))!;
        var draftPolicy = Assert.Single(draft
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal(CompanyPolicies.CompanyManager, draftPolicy.Policy);
        Assert.DoesNotContain(typeof(FinanceAutonomyWorkflowTemplatesController).GetMethods(),
            method => method.Name.Contains("Activate", StringComparison.OrdinalIgnoreCase));
    }
}
