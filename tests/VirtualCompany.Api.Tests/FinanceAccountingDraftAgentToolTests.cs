using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Shared;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceAccountingDraftAgentToolTests
{
    [Fact]
    public void Manifest_registers_four_draft_tools_and_one_separate_guarded_submission_without_posting_tool()
    {
        var registry = new StaticCompanyToolRegistry();
        var definitions = registry.ListToolDefinitions();
        var operations = FinanceAgentCoverageCatalogue.Manifests.SelectMany(x => x.Operations).ToArray();

        Assert.Equal(4, FinanceAccountingDraftAgentToolIds.RecommendationTools.Count);
        foreach (var tool in FinanceAccountingDraftAgentToolIds.RecommendationTools)
        {
            var definition = Assert.Single(definitions, x => x.ToolName == tool);
            Assert.Equal(ToolActionType.Recommend, definition.ActionType);
            Assert.False(definition.SensitiveAction);
            Assert.Contains("draft", definition.SelectionMetadata!.SafePurpose, StringComparison.OrdinalIgnoreCase);
            var operation = Assert.Single(operations, x => x.ToolName == tool &&
                x.SupportState == FinanceAgentCoverageSupportStates.ImplementedRecommendDraft);
            Assert.Equal(FinancePermissions.AccountingAdmin, operation.RequiredPermission);
            Assert.Contains(FinancePermissions.AccountingAdmin,
                FinanceAgentAuthorizationService.ResolveRequirements(tool, ToolActionType.Recommend).Permissions);
        }

        var journalSchema = definitions.Single(x => x.ToolName ==
            FinanceAccountingDraftAgentToolIds.CreateManualJournalDraft).InputSchema.ToJsonString();
        Assert.Contains("taxFacts", journalSchema, StringComparison.Ordinal);
        Assert.Contains("dimensionFacts", journalSchema, StringComparison.Ordinal);
        Assert.Contains("sourceVersion", journalSchema, StringComparison.Ordinal);
        var reconciliationSchema = definitions.Single(x => x.ToolName ==
            FinanceAccountingDraftAgentToolIds.CreateReconciliationDecisionDraft).InputSchema.ToJsonString();
        Assert.Contains("rationale", reconciliationSchema, StringComparison.Ordinal);
        Assert.Contains("sourceRecords", reconciliationSchema, StringComparison.Ordinal);

        var submit = Assert.Single(definitions, x => x.ToolName == FinanceAccountingDraftAgentToolIds.SubmitForApproval);
        Assert.Equal(ToolActionType.Execute, submit.ActionType);
        Assert.True(submit.SensitiveAction);
        var risk = FinanceToolRiskPolicyCatalog.GetRequired(submit.ToolName);
        Assert.Equal(FinanceToolExternalSideEffects.ApprovalStateChange, risk.ExternalSideEffectClassification);
        Assert.Equal(FinancePermissions.AccountingAdmin, risk.RequiredActorPermission);
        Assert.Contains(FinancePermissions.AccountingAdmin,
            FinanceAgentAuthorizationService.ResolveRequirements(submit.ToolName, ToolActionType.Execute).Permissions);
        Assert.DoesNotContain(definitions, x => FinanceAccountingDraftAgentToolIds.Contains(x.ToolName) &&
                                                x.ToolName.Contains("post", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(definitions, x => FinanceAccountingDraftAgentToolIds.Contains(x.ToolName) &&
                                                x.ToolName.Contains("apply", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Unbalanced_manual_journal_is_retained_as_reviewable_draft_and_submission_is_blocked()
    {
        var companyId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var input = Input(sourceId, "source-v7", debit: 100m, credit: 90m);
        var draft = Draft(companyId, input, difference: 10m);
        var preview = Preview(draft, valid: false, "unbalanced_entry");
        var created = 0;
        var service = Service(
            manual: Proxy<IManualJournalService>((method, _) => method.Name switch
            {
                nameof(IManualJournalService.CreateAsync) => CountCreate(),
                nameof(IManualJournalService.PreviewAsync) => Task.FromResult(preview),
                _ => Unexpected(method)
            }),
            journals: JournalReader(companyId, sourceId, "source-v7"));

        var response = await service.ExecuteAsync(Request(FinanceAccountingDraftAgentToolIds.CreateManualJournalDraft,
            ToolActionType.Recommend, companyId, actorId,
            ("draft", JsonSerializer.SerializeToNode(input, JsonOptions)),
            ("idempotencyKey", JsonValue.Create("journal-draft-business-key")),
            ("rationale", JsonValue.Create("Accrual supported by the attached source.")),
            ("modelProposedFields", new JsonArray("description"))), default);

        Assert.True(response.Success);
        Assert.Equal(1, created);
        var result = response.Data["accountingDraft"]!;
        Assert.True(result["submissionBlocked"]!.GetValue<bool>());
        Assert.Equal(10m, result["draft"]!["difference"]!.GetValue<decimal>());
        Assert.False(response.Metadata["posted"]!.GetValue<bool>());
        Assert.Contains("description", result["modelProposedFields"]!.AsArray().Select(x => x!.GetValue<string>()));

        Task<ManualJournalDraftDto> CountCreate() { created++; return Task.FromResult(draft); }
    }

    [Fact]
    public async Task Stale_source_version_rejects_draft_before_any_authoritative_create()
    {
        var companyId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var createCalls = 0;
        var input = Input(sourceId, "source-v6", 100m, 100m);
        var service = Service(
            manual: Proxy<IManualJournalService>((method, _) =>
            {
                if (method.Name == nameof(IManualJournalService.CreateAsync)) createCalls++;
                return Unexpected(method);
            }),
            journals: JournalReader(companyId, sourceId, "source-v7"));

        var response = await service.ExecuteAsync(Request(FinanceAccountingDraftAgentToolIds.CreateManualJournalDraft,
            ToolActionType.Recommend, companyId, Guid.NewGuid(),
            ("draft", JsonSerializer.SerializeToNode(input, JsonOptions)),
            ("idempotencyKey", JsonValue.Create("stale-source-draft-key")),
            ("rationale", JsonValue.Create("Prepare an accrual."))), default);

        Assert.False(response.Success);
        Assert.Equal("accounting_draft_source_stale", response.ErrorCode);
        Assert.Equal(0, createCalls);
    }

    [Fact]
    public async Task Correction_requires_the_current_original_journal_in_its_source_evidence()
    {
        var originalId = Guid.NewGuid();
        var input = Input(Guid.NewGuid(), "source-v7", 100m, 100m) with
        {
            OriginalLedgerEntryId = originalId,
            CorrectionReason = "Reverse the incorrect classification."
        };

        var response = await Service().ExecuteAsync(Request(
            FinanceAccountingDraftAgentToolIds.CreateCorrectionDraft, ToolActionType.Recommend,
            Guid.NewGuid(), Guid.NewGuid(),
            ("draft", JsonSerializer.SerializeToNode(input, JsonOptions)),
            ("idempotencyKey", JsonValue.Create("correction-source-link-key")),
            ("rationale", JsonValue.Create("Correct the original governed posting."))), default);

        Assert.False(response.Success);
        Assert.Equal("correction_source_required", response.ErrorCode);
    }

    [Fact]
    public async Task Changed_draft_hash_is_rejected_before_submission()
    {
        var companyId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var input = Input(sourceId, "source-v7", 100m, 100m);
        var draft = Draft(companyId, input, 0m);
        var submits = 0;
        var service = Service(manual: Proxy<IManualJournalService>((method, _) => method.Name switch
        {
            nameof(IManualJournalService.GetAsync) => Task.FromResult(draft),
            nameof(IManualJournalService.SubmitAsync) => CountSubmit(),
            _ => Unexpected(method)
        }), journals: JournalReader(companyId, sourceId, "source-v7"));

        var response = await service.ExecuteAsync(Request(FinanceAccountingDraftAgentToolIds.SubmitForApproval,
            ToolActionType.Execute, companyId, Guid.NewGuid(),
            ("draftId", JsonValue.Create(draft.Id)), ("expectedVersion", JsonValue.Create(draft.Version)),
            ("expectedPayloadHash", JsonValue.Create(new string('b', 64))),
            ("idempotencyKey", JsonValue.Create("submit-reviewed-draft-key")),
            ("reviewed", JsonValue.Create(true))), default);

        Assert.False(response.Success);
        Assert.Equal("accounting_draft_stale", response.ErrorCode);
        Assert.Equal(0, submits);
        Task<ManualJournalSubmissionResult> CountSubmit() { submits++; throw new InvalidOperationException(); }
    }

    [Fact]
    public async Task Current_reviewed_draft_enters_approval_once_and_is_never_posted()
    {
        var companyId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var input = Input(sourceId, "source-v7", 100m, 100m);
        var draft = Draft(companyId, input, 0m);
        var preview = Preview(draft, true);
        var approvalId = Guid.NewGuid();
        var submitted = draft with { Status = "awaiting_approval", ApprovalRequestId = approvalId };
        var calls = 0;
        var service = Service(manual: Proxy<IManualJournalService>((method, _) => method.Name switch
        {
            nameof(IManualJournalService.GetAsync) => Task.FromResult(draft),
            nameof(IManualJournalService.PreviewAsync) => Task.FromResult(preview),
            nameof(IManualJournalService.SubmitAsync) => Submit(),
            nameof(IManualJournalService.PostAsync) => throw new InvalidOperationException("Posting must not be called."),
            _ => Unexpected(method)
        }), journals: JournalReader(companyId, sourceId, "source-v7"));

        var response = await service.ExecuteAsync(Request(FinanceAccountingDraftAgentToolIds.SubmitForApproval,
            ToolActionType.Execute, companyId, Guid.NewGuid(),
            ("draftId", JsonValue.Create(draft.Id)), ("expectedVersion", JsonValue.Create(draft.Version)),
            ("expectedPayloadHash", JsonValue.Create(draft.PayloadHash)),
            ("idempotencyKey", JsonValue.Create("submit-reviewed-draft-key")),
            ("reviewed", JsonValue.Create(true))), default);

        Assert.True(response.Success);
        Assert.Equal(1, calls);
        Assert.Equal("awaiting_approval", response.Metadata["state"]!.GetValue<string>());
        Assert.Equal("conversation-plan-42", response.Metadata["correlationId"]!.GetValue<string>());
        Assert.False(response.Data["accountingDraftSubmission"]!["posted"]!.GetValue<bool>());
        Assert.Equal(approvalId, response.Data["accountingDraftSubmission"]!["submission"]!["approvalRequestId"]!.GetValue<Guid>());
        Task<ManualJournalSubmissionResult> Submit() { calls++; return Task.FromResult(new ManualJournalSubmissionResult(submitted, approvalId, false)); }
    }

    [Fact]
    public async Task Action_class_mismatch_and_missing_actor_are_rejected()
    {
        var service = Service();
        var wrongAction = await service.ExecuteAsync(Request(FinanceAccountingDraftAgentToolIds.SubmitForApproval,
            ToolActionType.Recommend, Guid.NewGuid(), Guid.NewGuid()), default);
        var missingActor = await service.ExecuteAsync(new InternalToolExecutionRequest(
            FinanceAccountingDraftAgentToolIds.CreateManualJournalDraft,
            new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ToolActionType.Recommend, "finance"),
            new Dictionary<string, JsonNode?>()), default);

        Assert.Equal("unsupported_action_type", wrongAction.ErrorCode);
        Assert.Equal("accounting_draft_actor_required", missingActor.ErrorCode);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static ManualJournalDraftInput Input(Guid sourceId, string sourceVersion, decimal debit, decimal credit) => new(
        Guid.NewGuid(), "A", new DateOnly(2026, 8, 31), new DateOnly(2026, 8, 31), "Accrual proposal", "SEK",
        [new(Guid.NewGuid(), debit, 0m, "Model-proposed description"), new(Guid.NewGuid(), 0m, credit, "Counter-line")],
        [], SourceRecords: [new(FinanceAccountingDraftSourceTypes.LedgerJournal, sourceId, sourceVersion)]);

    private static ManualJournalDraftDto Draft(Guid companyId, ManualJournalDraftInput input, decimal difference) => new(
        Guid.NewGuid(), companyId, input.FiscalPeriodId, input.VoucherSeriesCode, input.DocumentDate, input.PostingDate,
        input.Explanation, input.Currency, "draft", 1, new string('a', 64), Guid.NewGuid(), Guid.NewGuid(), null, null,
        input.OriginalLedgerEntryId, input.CorrectionReason, DateTime.UtcNow, DateTime.UtcNow, null,
        100m, 100m - difference, difference,
        input.Lines.Select((x, i) => new ManualJournalLineDto(Guid.NewGuid(), i + 1, x.FinanceAccountId,
            (3000 + i).ToString(), "Account", x.DebitAmount, x.CreditAmount, input.Currency, x.Description,
            x.CostCenterId, x.TaxFacts ?? new Dictionary<string, string>(), x.DimensionFacts ?? new Dictionary<string, string>(),
            x.DimensionMemberIds)).ToArray(), [], null,
        input.SourceRecords!.Select(x => new ManualJournalSourceReferenceDto(x.SourceType, x.RecordId, x.SourceVersion)).ToArray());

    private static ManualJournalPreviewDto Preview(ManualJournalDraftDto draft, bool valid, string reason = "")
    {
        var issues = valid ? Array.Empty<AccountingPostingIssue>() : [new AccountingPostingIssue(reason, "Draft is unbalanced.")];
        return new(draft, new(valid, draft.DebitTotal, draft.CreditTotal, draft.Difference, draft.Currency, 2, issues),
            new(valid, true, 0m, draft.Currency, issues, [new(ManualJournalReasonCodes.ApprovalRequired, "Human approval required.")]));
    }

    private static IAccountingJournalReadService JournalReader(Guid companyId, Guid sourceId, string sourceVersion) =>
        Proxy<IAccountingJournalReadService>((method, args) =>
        {
            Assert.Equal(nameof(IAccountingJournalReadService.GetAsync), method.Name);
            var query = Assert.IsType<GetAccountingJournalQuery>(args![0]);
            Assert.Equal(companyId, query.CompanyId);
            Assert.Equal(sourceId, query.LedgerEntryId);
            return Task.FromResult(new AccountingJournalDto(sourceId, companyId, Guid.NewGuid(), "A1", "posted", "A",
                1, 2026, new(2026, 8, 31), new(2026, 8, 31), "SEK", "manual", "Source", "manual", "1",
                sourceVersion, "pack", "1", Guid.NewGuid(), null, null, null, DateTime.UtcNow, 100m, 100m, []));
        });

    private static FinanceAccountingDraftAgentService Service(
        IManualJournalService? manual = null,
        IAccountingJournalReadService? journals = null) => new(
        manual ?? Proxy<IManualJournalService>((m, _) => Unexpected(m)),
        Proxy<IAdvancedReconciliationCommandService>((m, _) => Unexpected(m)),
        Proxy<IFinanceAgentDecisionService>((m, _) => Unexpected(m)),
        journals ?? Proxy<IAccountingJournalReadService>((m, _) => Unexpected(m)),
        Proxy<IBankTransactionReadService>((m, _) => Unexpected(m)),
        Proxy<IFinancePaymentReadService>((m, _) => Unexpected(m)),
        Proxy<IFinanceReadService>((m, _) => Unexpected(m)));

    private static InternalToolExecutionRequest Request(string tool, ToolActionType action, Guid companyId, Guid actorId,
        params (string Key, JsonNode? Value)[] payload) => new(tool,
        new(companyId, Guid.NewGuid(), Guid.NewGuid(), action, "finance", CorrelationId: "conversation-plan-42", ActorUserId: actorId),
        payload.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase));

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, HandlerProxy>();
        ((HandlerProxy)(object)proxy).Handler = handler;
        return proxy;
    }
    private static object Unexpected(MethodInfo method) => throw new InvalidOperationException("Unexpected call to " + method.Name);
    public class HandlerProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
    }
}
