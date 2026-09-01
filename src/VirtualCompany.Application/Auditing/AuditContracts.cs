using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Application.Auditing;

public sealed record AuditEventWriteRequest(
    Guid CompanyId,
    string ActorType,
    Guid? ActorId,
    string Action,
    string TargetType,
    string TargetId,
    string Outcome,
    string? RationaleSummary = null,
    IReadOnlyCollection<string>? DataSources = null,
    IReadOnlyDictionary<string, string?>? Metadata = null,
    string? CorrelationId = null,
    DateTime? OccurredUtc = null,
    IReadOnlyCollection<AuditDataSourceUsed>? DataSourcesUsed = null,
    string? PayloadDiffJson = null,
    string? AgentName = null,
    string? AgentRole = null,
    string? ResponsibilityDomain = null,
    string? PromptProfileVersion = null,
    string? BoundaryDecisionOutcome = null,
    string? IdentityReasonCode = null,
    string? BoundaryReasonCode = null);

// Business audit history is an explicit application concern.
// Technical diagnostics belong on ILogger and must not be inferred from audit records.
public interface IAuditEventWriter
{
    Task WriteAsync(AuditEventWriteRequest auditEvent, CancellationToken cancellationToken);
}

public sealed record AuditHistoryFilter(
    Guid? AgentId = null,
    Guid? TaskId = null,
    Guid? WorkflowInstanceId = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    int? Skip = null,
    int? Take = null);

public sealed record AuditHistoryListItem(
    Guid Id,
    Guid CompanyId,
    string ActorType,
    Guid? ActorId,
    string? ActorLabel,
    string Action,
    string TargetType,
    string TargetId,
    string? TargetLabel,
    string Outcome,
    string? RationaleSummary,
    DateTime OccurredAt,
    AuditSafeExplanationDto Explanation,
    string? CorrelationId,
    IReadOnlyList<AuditEntityReferenceDto> AffectedEntities,
    string? AgentName,
    string? AgentRole,
    string? ResponsibilityDomain,
    string? PromptProfileVersion,
    string? BoundaryDecisionOutcome,
    string? IdentityReasonCode,
    string? BoundaryReasonCode);

public sealed record AuditHistoryResult(
    IReadOnlyList<AuditHistoryListItem> Items,
    int TotalCount,
    int Skip,
    int Take);

public sealed record AuditDetailDto(
    Guid Id,
    Guid CompanyId,
    string ActorType,
    Guid? ActorId,
    string? ActorLabel,
    string Action,
    string TargetType,
    string TargetId,
    string? TargetLabel,
    string Outcome,
    string? RationaleSummary,
    IReadOnlyList<string> DataSources,
    AuditSafeExplanationDto Explanation,
    IReadOnlyList<AuditSourceReferenceDto> SourceReferences,
    DateTime OccurredAt,
    string? CorrelationId,
    IReadOnlyDictionary<string, string?> Metadata,
    IReadOnlyList<AuditApprovalReferenceDto> LinkedApprovals,
    IReadOnlyList<AuditToolExecutionReferenceDto> LinkedToolExecutions,
    IReadOnlyList<AuditEntityReferenceDto> AffectedEntities,
    string? AgentName,
    string? AgentRole,
    string? ResponsibilityDomain,
    string? PromptProfileVersion,
    string? BoundaryDecisionOutcome,
    string? IdentityReasonCode,
    string? BoundaryReasonCode);

public sealed record AuditSafeExplanationDto(
    string Summary,
    string WhyThisAction,
    string Outcome,
    IReadOnlyList<string> DataSources);

public sealed record AuditSourceReferenceDto(
    // Label is the concise user-facing text used by audit and explainability views.
    string Label,
    string? Reference,
    string? Type = null,
    string? SourceType = null,
    string? DisplayName = null,
    string? SecondaryText = null,
    string? EntityType = null,
    string? EntityId = null,
    string? Snippet = null);

public sealed record AuditApprovalReferenceDto(
    Guid Id,
    string ApprovalType,
    string Status,
    string TargetEntityType,
    Guid TargetEntityId,
    string? DecisionSummary,
    DateTime CreatedAt,
    DateTime? DecidedAt);

public sealed record AuditToolExecutionReferenceDto(
    Guid Id,
    Guid AgentId,
    string? AgentLabel,
    string ToolName,
    string ActionType,
    string Status,
    Guid? TaskId,
    Guid? WorkflowInstanceId,
    Guid? ApprovalRequestId,
    DateTime StartedAt,
    DateTime? CompletedAt);

public sealed record AuditEntityReferenceDto(
    string EntityType,
    string EntityId,
    string? Label);

public interface IAuditQueryService
{
    Task<AuditHistoryResult> ListAsync(Guid companyId, AuditHistoryFilter filter, CancellationToken cancellationToken);
    Task<AuditDetailDto> GetAsync(Guid companyId, Guid auditEventId, CancellationToken cancellationToken);
}

public static class AuditActorTypes
{
    public const string User = "user";
    public const string Human = User;
    public const string System = "system";
    public const string Agent = "agent";
}

public static class AuditTargetTypes
{
    public const string Company = "company";
    public const string CompanyInvitation = "company_invitation";
    public const string CompanyMembership = "company_membership";
    public const string Agent = "agent";
    public const string AgentToolExecution = "agent_tool_execution";
    public const string EscalationPolicy = "escalation_policy";
    public const string AgentGeneration = "agent_generation";
    public const string TriggerEvaluation = "trigger_evaluation";
    public const string TriggerExecutionAttempt = "trigger_execution_attempt";
    public const string CompanyDocument = "company_document";
    public const string ApprovalRequest = "approval_request";
    public const string CustomerInvoiceDraft = "customer_invoice_draft";
    public const string CustomerInvoiceCorrection = "customer_invoice_correction";
    public const string MemoryItem = "memory_item";
    public const string WorkflowInstance = "workflow_instance";
    public const string WorkTask = "work_task";
    public const string LinkedEntity = "linked_entity";
    public const string ConversationTaskLink = "conversation_task_link";
    public const string WorkflowException = "workflow_exception";
    public const string ExecutionException = "execution_exception";
    public const string CompanyNotification = "company_notification";
    public const string ProactiveMessage = "proactive_message";
    public const string AgentResponsibilityPolicy = "agent_responsibility_policy";
    public const string IntegrationConnection = "integration_connection";
    public const string FiscalPeriod = "fiscal_period";
    public const string FinanceAccount = "finance_account";
    public const string AccountingConfiguration = "accounting_configuration";
    public const string CompanyStatutoryProfile = "company_statutory_profile";
    public const string StatutoryDocumentSeries = "statutory_document_series";
    public const string StatutoryDocumentNumberAllocation = "statutory_document_number_allocation";
    public const string IssuedStatutoryDocument = "issued_statutory_document";
    public const string VatFilingPeriod = "vat_filing_period";
    public const string VatReturn = "vat_return";
    public const string AccountingJournal = "accounting_journal";
    public const string CustomerInvoiceAccounting = "customer_invoice_accounting";
    public const string SupplierBillAccounting = "supplier_bill_accounting";
    public const string AccountingExport = "accounting_export";
    public const string FinancialReportSnapshot = "financial_report_snapshot";
    public const string AccountingAuthority = "accounting_authority";
    public const string AccountingProviderSwitch = "accounting_provider_switch";
    public const string AccountingProviderSwitchAssessment = "accounting_provider_switch_assessment";
    public const string AccountingProviderSwitchStagedRecord = "accounting_provider_switch_staged_record";
    public const string AccountingProviderSwitchMappingDecision = "accounting_provider_switch_mapping_decision";
    public const string AccountingProviderSwitchCutoverPlan = "accounting_provider_switch_cutover_plan";
    public const string AccountingProviderSwitchCutover = "accounting_provider_switch_cutover";
    public const string AccountingProviderSwitchMonitoring = "accounting_provider_switch_monitoring";
    public const string AccountingProviderExport = "accounting_provider_export";
    public const string AccountingMigration = "accounting_migration";
    public const string AccountingRecovery = "accounting_recovery";
    public const string ManualJournalDraft = "manual_journal_draft";
    public const string TreasurySource = "treasury_source";
    public const string PaymentBatch = "payment_batch";
    public const string PaymentBatchExecution = "payment_batch_execution";
    public const string CurrencyRevaluationRun = "currency_revaluation_run";
    public const string FixedAsset = "fixed_asset";
    public const string FixedAssetClass = "fixed_asset_class";
    public const string FixedAssetDepreciationRun = "fixed_asset_depreciation_run";
    public const string AccountingDimension = "accounting_dimension";
    public const string AccountingDimensionMember = "accounting_dimension_member";
    public const string AccountingDimensionPolicy = "accounting_dimension_policy";
    public const string AccountingDimensionMapping = "accounting_dimension_mapping";
    public const string AccountingAllocationTemplate = "accounting_allocation_template";
    public const string AccountingAllocation = "accounting_allocation";
    public const string AccountingSchedule = "accounting_schedule";
    public const string AccountingScheduleOccurrence = "accounting_schedule_occurrence";
    public const string AccountingCloseTemplate = "accounting_close_template";
    public const string AccountingCloseInstance = "accounting_close_instance";
    public const string AccountingCloseTask = "accounting_close_task";
    public const string AccountingCloseReadiness = "accounting_close_readiness";
    public const string AccountingCloseWaiver = "accounting_close_waiver";
    public const string AccountingCloseReopenRequest = "accounting_close_reopen_request";
    public const string YearEndRun = "year_end_run";
    public const string YearEndSubsequentEvent = "year_end_subsequent_event";
}

public static class AuditEventOutcomes
{
    public const string Succeeded = "succeeded";
    public const string Started = "started";
    public const string Blocked = "blocked";
    public const string Denied = "denied";
    public const string Pending = "pending";
    public const string Failed = "failed";
    public const string Requested = "requested";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
}

public static class AuditEventActions
{
    public const string CompanyCreated = "company.created";
    public const string CompanySelected = "company.selected";
    public const string CompanyOnboardingCompleted = "company.onboarding.completed";
    public const string AccidentalCompaniesRemoved = "company.accidental_duplicates.removed";
    public const string CompanyInvitationCreated = "company.invitation.created";
    public const string CompanyInvitationResent = "company.invitation.resent";
    public const string CompanyInvitationRevoked = "company.invitation.revoked";
    public const string CompanyInvitationAccepted = "company.invitation.accepted";
    public const string CompanyMembershipRoleChanged = "company.membership.role_changed";
    public const string AgentHired = "agent.hired";
    public const string AgentOperatingProfileUpdated = "agent.operating_profile.updated";
    public const string AgentStatusUpdated = "agent.status.updated";
    public const string AgentToolExecutionDenied = "agent.tool_execution.denied";
    public const string AgentToolExecutionExecuted = "agent.tool_execution.executed";
    public const string FinanceAgentToolAuthorizationEvaluated = "finance.agent_tool.authorization_evaluated";
    public const string FinanceMutationPreviewCreated = "finance.mutation.preview_created";
    public const string FinanceMutationConfirmationSubmitted = "finance.mutation.confirmation_submitted";
    public const string FinanceMutationReconciled = "finance.mutation.reconciled";
    public const string FinanceConversationRunCreated = "finance.conversation_run.created";
    public const string FinanceConversationRunTransitioned = "finance.conversation_run.transitioned";
    public const string FinanceConversationRunCancelled = "finance.conversation_run.cancelled";
    public const string FinanceConversationRunRedacted = "finance.conversation_run.redacted";
    public const string CompanyDocumentUploaded = "company.document.uploaded";
    public const string CompanyDocumentUploadFailed = "company.document.upload_failed";
    public const string CompanyDocumentProcessed = "company.document.processed";
    public const string CompanyDocumentFailed = "company.document.failed";
    public const string MemoryItemExpired = "memory.item.expired";
    public const string MemoryItemDeleted = "memory.item.deleted";
    public const string ApprovalCreated = "approval.created";
    public const string ApprovalStepApproved = "approval.step.approved";
    public const string ApprovalStepRejected = "approval.step.rejected";
    public const string ApprovalChainAdvanced = "approval.chain.advanced";
    public const string ApprovalCompleted = "approval.completed";
    public const string ApprovalRejected = "approval.rejected";
    public const string ApprovalLinkedEntityStateUpdated = "approval.linked_entity.state_updated";
    public const string SupplierApprovalAutomationGranted = "supplier.approval_automation.granted";
    public const string SupplierApprovalAutomationRevoked = "supplier.approval_automation.revoked";
    public const string AgentToolExecutionApprovalRequested = "agent.tool_execution.approval_requested";
    public const string WorkflowInstanceStarted = "workflow.instance.started";
    public const string EscalationPolicyEvaluationStarted = "escalation.policy_evaluation.started";
    public const string EscalationPolicyEvaluationCompleted = "escalation.policy_evaluation.completed";
    public const string EscalationPolicyEvaluationResult = "escalation.policy_evaluation.result";
    public const string EscalationCreated = "escalation.created";
    public const string EscalationDuplicateSkipped = "escalation.duplicate_skipped";
    public const string TriggerEvaluationStarted = "trigger.evaluation.started";
    public const string TriggerEvaluationSkipped = "trigger.evaluation.skipped";
    public const string TriggerExecutionAttemptStarted = "trigger.execution_attempt.started";
    public const string TriggerExecutionAttemptRetried = "trigger.execution_attempt.retried";
    public const string TriggerExecutionAttemptRetryDeferred = "trigger.execution_attempt.retry_deferred";
    public const string TriggerExecutionAttemptDuplicateSkipped = "trigger.execution_attempt.duplicate_skipped";
    public const string TriggerExecutionAttemptBlocked = "trigger.execution_attempt.blocked";
    public const string TriggerOrchestrationStartRequested = "trigger.orchestration.start_requested";
    public const string TriggerExecutionAttemptDispatched = "trigger.execution_attempt.dispatched";
    public const string TriggerExecutionAttemptRetryScheduled = "trigger.execution_attempt.retry_scheduled";
    public const string TriggerExecutionAttemptDeadLettered = "trigger.execution_attempt.dead_lettered";
    public const string TriggerExecutionAttemptFailed = "trigger.execution_attempt.failed";
    public const string AgentInitiatedTaskCreated = "agent.initiated_task.created";
    public const string WorkflowExceptionCreated = "workflow.exception.created";
    public const string WorkflowExceptionReviewed = "workflow.exception.reviewed";
    public const string ExecutionExceptionCreated = "execution.exception.created";
    public const string DirectChatTaskCreated = "direct_chat.task.created";
    public const string DirectChatTaskLinked = "direct_chat.task.linked";
    public const string SingleAgentTaskOrchestrationExecuted = "single_agent_task.orchestration.executed";
    public const string AgentGeneration = "agent_generation";
    public const string BoundaryEnforcement = "boundary_enforcement";
    public const string MultiAgentCollaborationStarted = "multi_agent.collaboration.started";
    public const string MultiAgentCollaborationPlanCreated = "multi_agent.collaboration.plan_created";
    public const string MultiAgentWorkerSubtaskCreated = "multi_agent.worker_subtask.created";
    public const string MultiAgentCollaborationGuardrailDenied = "multi_agent.collaboration.guardrail_denied";
    public const string MultiAgentWorkerCompleted = "multi_agent.worker.completed";
    public const string MultiAgentWorkerFailed = "multi_agent.worker.failed";
    public const string MultiAgentCollaborationConsolidated = "multi_agent.collaboration.consolidated";
    public const string CompanyNotificationActioned = "company.notification.actioned";
    public const string ProactiveMessageDelivered = "proactive_message.delivered";
    public const string ProactiveMessageBlocked = "proactive_message.blocked";
    public const string AgentResponsibilityOutOfScopeHandled = "agent.responsibility.out_of_scope_handled";
    public const string ReportingPeriodCloseValidationExecuted = "reporting_period.close_validation.executed";
    public const string ReportingPeriodLockApplied = "reporting_period.lock.applied";
    public const string ReportingPeriodLockRemoved = "reporting_period.lock.removed";
    public const string ReportingPeriodRegenerationBlocked = "reporting_period.regeneration.blocked";
    public const string ReportingPeriodClosedAndLocked = "reporting_period.closed_and_locked";
    public const string ReportingPeriodReopened = "reporting_period.reopened";
    public const string AccountingTaxSummaryReviewed = "accounting.tax_summary.reviewed";
    public const string AccountingExportRequested = "accounting.export.requested";
    public const string AccountingExportContentExpired = "accounting.export.content_expired";
    public const string FinancialReportSnapshotCaptured = "accounting.financial_report_snapshot.captured";
    public const string IntegrationConnectionDisconnected = "integration.connection.disconnected";
    public const string AccountingConfigurationCreated = "accounting.configuration.created";
    public const string AccountingTaxDecisionBlocked = "accounting.tax_decision.blocked";
    public const string CompanyStatutoryProfileCreated = "accounting.statutory_profile.created";
    public const string CompanyStatutoryProfileUpdated = "accounting.statutory_profile.updated";
    public const string StatutoryDocumentSeriesCreated = "statutory_document.series.created";
    public const string StatutoryDocumentSeriesUpdated = "statutory_document.series.updated";
    public const string StatutoryDocumentNumberGapRecorded = "statutory_document.number_gap.recorded";
    public const string StatutoryDocumentIssued = "statutory_document.issued";
    public const string StatutoryDocumentImportedRegistered = "statutory_document.imported_registered";
    public const string StatutoryDocumentEvidenceAttached = "statutory_document.evidence_attached";
    public const string VatFilingPeriodCreated = "accounting.vat_filing_period.created";
    public const string VatReturnCalculated = "accounting.vat_return.calculated";
    public const string VatReturnApprovalRequested = "accounting.vat_return.approval_requested";
    public const string VatReturnFinalized = "accounting.vat_return.finalized";
    public const string VatReturnCorrectionCreated = "accounting.vat_return.correction_created";
    public const string AccountingPolicyPackSelected = "accounting.policy_pack.selected";
    public const string AccountingPolicyPackUpgraded = "accounting.policy_pack.upgraded";
    public const string AccountingJournalPosted = "accounting.journal.posted";
    public const string AccountingJournalReversed = "accounting.journal.reversed";
    public const string AccountingManualJournalDraftCreated = "accounting.manual_journal.created";
    public const string AccountingManualJournalDraftUpdated = "accounting.manual_journal.updated";
    public const string AccountingManualJournalDraftDiscarded = "accounting.manual_journal.discarded";
    public const string AccountingManualJournalApprovalRequested = "accounting.manual_journal.approval_requested";
    public const string AccountingCurrencyRevaluationPreviewed = "accounting.currency_revaluation.previewed";
    public const string AccountingCurrencyRevaluationReviewed = "accounting.currency_revaluation.reviewed";
    public const string AccountingCurrencyRevaluationApprovalRequested = "accounting.currency_revaluation.approval_requested";
    public const string AccountingCurrencyRevaluationPosted = "accounting.currency_revaluation.posted";
    public const string AccountingCurrencyRevaluationReversed = "accounting.currency_revaluation.reversed";
    public const string AccountingCurrencyRevaluationConfigured = "accounting.currency_revaluation.configured";
    public const string AccountingDimensionConfigured = "accounting.dimension.configured";
    public const string AccountingDimensionMemberConfigured = "accounting.dimension_member.configured";
    public const string AccountingDimensionPolicyConfigured = "accounting.dimension_policy.configured";
    public const string AccountingDimensionMappingConfigured = "accounting.dimension_mapping.configured";
    public const string AccountingAllocationTemplateVersioned = "accounting.allocation_template.versioned";
    public const string AccountingAllocationApplied = "accounting.allocation.applied";
    public const string AccountingScheduleCreated = "accounting.schedule.created";
    public const string FixedAssetClassConfigured = "accounting.fixed_asset.class_configured";
    public const string FixedAssetRegistered = "accounting.fixed_asset.registered";
    public const string FixedAssetLifecyclePosted = "accounting.fixed_asset.lifecycle_posted";
    public const string FixedAssetTransferred = "accounting.fixed_asset.transferred";
    public const string FixedAssetEventReversed = "accounting.fixed_asset.event_reversed";
    public const string FixedAssetDepreciationRunPosted = "accounting.fixed_asset.depreciation_run_posted";
    public const string FixedAssetMigrationConflictDetected = "accounting.fixed_asset.migration_conflict_detected";
    public const string AccountingScheduleVersioned = "accounting.schedule.versioned";
    public const string AccountingScheduleApprovalRequested = "accounting.schedule.approval_requested";
    public const string AccountingScheduleApprovalDecided = "accounting.schedule.approval_decided";
    public const string AccountingScheduleStateChanged = "accounting.schedule.state_changed";
    public const string AccountingScheduleOccurrencePosted = "accounting.schedule.occurrence_posted";
    public const string AccountingScheduleOccurrenceReversed = "accounting.schedule.occurrence_reversed";
    public const string AccountingScheduleOccurrenceBlocked = "accounting.schedule.occurrence_blocked";
    public const string AccountingScheduleOccurrenceRegenerated = "accounting.schedule.occurrence_regenerated";
    public const string AccountingCloseTemplateCreated = "accounting.close_template.created";
    public const string AccountingCloseTemplateVersioned = "accounting.close_template.versioned";
    public const string AccountingCloseTemplateActivated = "accounting.close_template.activated";
    public const string AccountingCloseTemplateRetired = "accounting.close_template.retired";
    public const string AccountingCloseStarted = "accounting.close.started";
    public const string AccountingCloseTaskAssigned = "accounting.close_task.assigned";
    public const string AccountingCloseTaskSignOffRequested = "accounting.close_task.sign_off_requested";
    public const string AccountingCloseTaskCompleted = "accounting.close_task.completed";
    public const string AccountingCloseTaskReopened = "accounting.close_task.reopened";
    public const string AccountingCloseTaskCancelled = "accounting.close_task.cancelled";
    public const string AccountingCloseTaskBlocked = "accounting.close_task.blocked";
    public const string AccountingCloseTaskBlockerResolved = "accounting.close_task.blocker_resolved";
    public const string AccountingCloseCancelled = "accounting.close.cancelled";
    public const string AccountingCloseReadinessPrepared = "accounting.close.readiness_prepared";
    public const string AccountingCloseReadinessSubmitted = "accounting.close.readiness_submitted";
    public const string AccountingCloseReadinessApproved = "accounting.close.readiness_approved";
    public const string AccountingCloseReadinessRejected = "accounting.close.readiness_rejected";
    public const string AccountingCloseReadinessCancelled = "accounting.close.readiness_cancelled";
    public const string AccountingCloseLocked = "accounting.close.locked";
    public const string AccountingCloseWaiverProposed = "accounting.close.waiver_proposed";
    public const string AccountingCloseWaiverReviewed = "accounting.close.waiver_reviewed";
    public const string AccountingCloseReopenRequested = "accounting.close.reopen_requested";
    public const string AccountingCloseReopenReviewed = "accounting.close.reopen_reviewed";
    public const string AccountingCloseReopened = "accounting.close.reopened";
    public const string AccountingYearEndPrepared = "accounting.year_end.prepared";
    public const string AccountingYearEndSubmitted = "accounting.year_end.submitted";
    public const string AccountingYearEndReviewed = "accounting.year_end.reviewed";
    public const string AccountingYearEndExecuted = "accounting.year_end.executed";
    public const string AccountingYearEndReconciled = "accounting.year_end.reconciled";
    public const string AccountingYearEndFinalized = "accounting.year_end.finalized";
    public const string AccountingYearEndFailed = "accounting.year_end.failed";
    public const string AccountingYearEndSubsequentEventRecorded = "accounting.year_end.subsequent_event.recorded";
    public const string AccountingYearEndSubsequentEventReviewed = "accounting.year_end.subsequent_event.reviewed";
    public const string AccountingYearEndCorrectionLinked = "accounting.year_end.correction.linked";
    public const string AccountingCustomerInvoiceDraftCreated = "accounting.customer_invoice_draft.created";
    public const string AccountingCustomerInvoiceDraftUpdated = "accounting.customer_invoice_draft.updated";
    public const string AccountingCustomerInvoiceDraftDiscarded = "accounting.customer_invoice_draft.discarded";
    public const string AccountingCustomerInvoiceDraftApprovalRequested = "accounting.customer_invoice_draft.approval_requested";
    public const string AccountingCustomerInvoiceDraftIssued = "accounting.customer_invoice_draft.issued";
    public const string AccountingCustomerInvoiceApprovalRequested = "accounting.customer_invoice.approval_requested";
    public const string AccountingCustomerCreditNoteCreated = "accounting.customer_credit_note.created";
    public const string AccountingCustomerInvoiceCorrectionProposed = "accounting.customer_invoice_correction.proposed";
    public const string AccountingCustomerInvoiceCorrectionExecuted = "accounting.customer_invoice_correction.executed";
    public const string AccountingCustomerInvoiceRefundQueued = "accounting.customer_invoice_refund.queued";
    public const string AccountingCustomerInvoiceRefundExecutionUpdated = "accounting.customer_invoice_refund.execution_updated";
    public const string AccountingCustomerInvoiceRefundReconciled = "accounting.customer_invoice_refund.reconciled";
    public const string AccountingSupplierBillApprovalRequested = "accounting.supplier_bill.approval_requested";
    public const string AccountingSupplierCreditNoteCreated = "accounting.supplier_credit_note.created";
    public const string AccountingSetupCompleted = "accounting.setup.completed";
    public const string AccountingAccountCreated = "accounting.account.created";
    public const string AccountingAccountRenamed = "accounting.account.renamed";
    public const string AccountingAccountDeactivated = "accounting.account.deactivated";
    public const string AccountingFiscalYearCreated = "accounting.fiscal_year.created";
    public const string AccountingBankReconciliationReviewed = "accounting.bank_reconciliation.reviewed";
    public const string AccountingBankStatementImported = "accounting.bank_statement.imported";
    public const string AccountingBankSuspenseReclassified = "accounting.bank_suspense.reclassified";
    public const string AccountingTreasurySourceCreated = "accounting.treasury_source.created";
    public const string AccountingTreasuryBankEvidenceLinked = "accounting.treasury_source.bank_evidence_linked";
    public const string AccountingTreasuryApprovalBound = "accounting.treasury_source.approval_bound";
    public const string AccountingTreasurySourcePosted = "accounting.treasury_source.posted";
    public const string AccountingTreasurySourceReversed = "accounting.treasury_source.reversed";
    public const string PaymentBatchCreated = "finance.payment_batch.created";
    public const string PaymentBatchChanged = "finance.payment_batch.changed";
    public const string PaymentBatchValidated = "finance.payment_batch.validated";
    public const string PaymentBatchApprovalRequested = "finance.payment_batch.approval_requested";
    public const string PaymentBatchApproved = "finance.payment_batch.approved";
    public const string PaymentBatchRejected = "finance.payment_batch.rejected";
    public const string PaymentBatchCancelled = "finance.payment_batch.cancelled";
    public const string PaymentBatchExecutionQueued = "finance.payment_batch_execution.queued";
    public const string PaymentBatchSubmittedToProvider = "finance.payment_batch_execution.submitted";
    public const string PaymentBatchAcknowledged = "finance.payment_batch_execution.acknowledged";
    public const string PaymentBatchExecutionReconciliationRequired = "finance.payment_batch_execution.reconciliation_required";
    public const string PaymentBatchExecutionRejected = "finance.payment_batch_execution.rejected";
    public const string PaymentBatchExecutionCancelled = "finance.payment_batch_execution.cancelled";
    public const string PaymentBatchExecutionSettled = "finance.payment_batch_execution.settled";
    public const string PaymentRemittanceAccepted = "finance.payment_remittance.accepted";
    public const string AccountingAuthorityChangeStarted = "accounting.authority.change_started";
    public const string AccountingAuthorityCutoverValidated = "accounting.authority.cutover_validated";
    public const string AccountingAuthorityCutoverCompleted = "accounting.authority.cutover_completed";
    public const string AccountingProviderSwitchCreated = "accounting.provider_switch.created";
    public const string AccountingProviderSwitchPlanUpdated = "accounting.provider_switch.plan_updated";
    public const string AccountingProviderSwitchStatusChanged = "accounting.provider_switch.status_changed";
    public const string AccountingProviderSwitchBlocked = "accounting.provider_switch.blocked";
    public const string AccountingProviderSwitchCancelled = "accounting.provider_switch.cancelled";
    public const string AccountingProviderSwitchMutationRejected = "accounting.provider_switch.mutation_rejected";
    public const string AccountingProviderSwitchAssessmentRequested = "accounting.provider_switch.assessment_requested";
    public const string AccountingProviderSwitchAssessmentCompleted = "accounting.provider_switch.assessment_completed";
    public const string AccountingProviderSwitchAssessmentFailed = "accounting.provider_switch.assessment_failed";
    public const string AccountingProviderSwitchMaterialGapsChanged = "accounting.provider_switch.material_gaps_changed";
    public const string AccountingProviderSwitchStagedRecordCreated = "accounting.provider_switch.staged_record_created";
    public const string AccountingProviderSwitchStagedRecordReplayed = "accounting.provider_switch.staged_record_replayed";
    public const string AccountingProviderSwitchStagedRecordChanged = "accounting.provider_switch.staged_record_changed";
    public const string AccountingProviderSwitchMappingSuggested = "accounting.provider_switch.mapping_suggested";
    public const string AccountingProviderSwitchMappingApprovalRequested = "accounting.provider_switch.mapping_approval_requested";
    public const string AccountingProviderSwitchMappingApproved = "accounting.provider_switch.mapping_approved";
    public const string AccountingProviderSwitchMappingRejected = "accounting.provider_switch.mapping_rejected";
    public const string AccountingProviderSwitchStaleDecisionRejected = "accounting.provider_switch.stale_decision_rejected";
    public const string AccountingProviderSwitchDispositionResolved = "accounting.provider_switch.disposition_resolved";
    public const string AccountingProviderSwitchSourceExcluded = "accounting.provider_switch.source_excluded";
    public const string AccountingProviderSwitchSourceTransformed = "accounting.provider_switch.source_transformed";
    public const string AccountingProviderSwitchDuplicateMatched = "accounting.provider_switch.duplicate_matched";
    public const string AccountingProviderSwitchRehearsalRequested = "accounting.provider_switch.rehearsal_requested";
    public const string AccountingProviderSwitchRehearsalCompleted = "accounting.provider_switch.rehearsal_completed";
    public const string AccountingProviderSwitchRehearsalFailed = "accounting.provider_switch.rehearsal_failed";
    public const string AccountingProviderSwitchReconciliationCalculated = "accounting.provider_switch.reconciliation_calculated";
    public const string AccountingProviderSwitchManualEvidenceRecorded = "accounting.provider_switch.manual_evidence_recorded";
    public const string AccountingProviderSwitchCutoverPlanGenerated = "accounting.provider_switch.cutover_plan_generated";
    public const string AccountingProviderSwitchPlanApprovalRequested = "accounting.provider_switch.plan_approval_requested";
    public const string AccountingProviderSwitchPlanStaleRejected = "accounting.provider_switch.plan_stale_rejected";
    public const string AccountingProviderSwitchPreparationRequested = "accounting.provider_switch.preparation_requested";
    public const string AccountingProviderSwitchPreparationCompleted = "accounting.provider_switch.preparation_completed";
    public const string AccountingProviderSwitchPreparationFailed = "accounting.provider_switch.preparation_failed";
    public const string AccountingProviderSwitchReadinessEvaluated = "accounting.provider_switch.readiness_evaluated";
    public const string AccountingProviderSwitchNativeCandidateCreated = "accounting.provider_switch.native_candidate_created";
    public const string AccountingProviderSwitchNativeCandidateRejected = "accounting.provider_switch.native_candidate_rejected";
    public const string AccountingProviderSwitchNativeCandidateReplayed = "accounting.provider_switch.native_candidate_replayed";
    public const string AccountingProviderSwitchExistingReferenceMatched = "accounting.provider_switch.existing_reference_matched";
    public const string AccountingProviderSwitchArchiveDependencyRecorded = "accounting.provider_switch.archive_dependency_recorded";
    public const string AccountingProviderSwitchTargetTransferRequested = "accounting.provider_switch.target_transfer_requested";
    public const string AccountingProviderSwitchTargetTransferPrepared = "accounting.provider_switch.target_transfer_prepared";
    public const string AccountingProviderSwitchTargetTransferReplayed = "accounting.provider_switch.target_transfer_replayed";
    public const string AccountingProviderSwitchTargetTransferFailed = "accounting.provider_switch.target_transfer_failed";
    public const string AccountingProviderSwitchTargetTransferReconciled = "accounting.provider_switch.target_transfer_reconciled";
    public const string AccountingProviderSwitchCutoverScheduled = "accounting.provider_switch.cutover_scheduled";
    public const string AccountingProviderSwitchSourceFrozen = "accounting.provider_switch.source_frozen";
    public const string AccountingProviderSwitchFinalSnapshotCaptured = "accounting.provider_switch.final_snapshot_captured";
    public const string AccountingProviderSwitchFinalTransferCompleted = "accounting.provider_switch.final_transfer_completed";
    public const string AccountingProviderSwitchFinalReconciliationCompleted = "accounting.provider_switch.final_reconciliation_completed";
    public const string AccountingProviderSwitchActivationApprovalRequested = "accounting.provider_switch.activation_approval_requested";
    public const string AccountingProviderSwitchActivated = "accounting.provider_switch.activated";
    public const string AccountingProviderSwitchCutoverBlocked = "accounting.provider_switch.cutover_blocked";
    public const string AccountingProviderSwitchCutoverRecovered = "accounting.provider_switch.cutover_recovered";
    public const string AccountingProviderSwitchCorrectiveCutoverRequired = "accounting.provider_switch.corrective_cutover_required";
    public const string AccountingProviderSwitchMonitoringStarted = "accounting.provider_switch.monitoring_started";
    public const string AccountingProviderSwitchMonitoringChecked = "accounting.provider_switch.monitoring_checked";
    public const string AccountingProviderSwitchMonitoringRetryRequested = "accounting.provider_switch.monitoring_retry_requested";
    public const string AccountingProviderSwitchMonitoringExceptionAccepted = "accounting.provider_switch.monitoring_exception_accepted";
    public const string AccountingProviderSwitchMonitoringClosureRequested = "accounting.provider_switch.monitoring_closure_requested";
    public const string AccountingProviderSwitchMonitoringClosed = "accounting.provider_switch.monitoring_closed";
    public const string AccountingProviderSwitchCorrectiveCutoverCreated = "accounting.provider_switch.corrective_cutover_created";
    public const string AccountingFormerAuthorityPostingBlocked = "accounting.former_authority.posting_blocked";
    public const string AccountingProviderExportQueued = "accounting.provider_export.queued";
    public const string AccountingProviderExportReconciled = "accounting.provider_export.reconciled";
    public const string AccountingMigrationRequested = "accounting.migration.requested";
    public const string AccountingJournalMigrated = "accounting.journal.migrated";
    public const string AccountingMigrationConflictResolved = "accounting.migration_conflict.resolved";
    public const string AccountingMigrationCompleted = "accounting.migration.completed";
    public const string AccountingRecoveryVerified = "accounting.recovery.verified";
    public const string FinanceWorkerRetryRequested = "finance.worker.retry_requested";
    public const string FinanceWorkerStopped = "finance.worker.stopped";
    public const string FinanceWorkerFailureAcknowledged = "finance.worker.failure_acknowledged";
    public const string SalesLeadQualified = "sales.lead.qualified";
    public const string SalesLeadRejected = "sales.lead.rejected";
    public const string SalesLeadConverted = "sales.lead.converted";
    public const string SalesDealStageChanged = "sales.deal.stage_changed";
    public const string SalesDealWon = "sales.deal.won";
    public const string SalesDealLost = "sales.deal.lost";
    public const string SalesEmailProcessed = "sales.email.processed";
}

public static class AuditBoundaryDecisionOutcomes
{
    public const string InScope = "in_scope";
    public const string DelegatedOutOfScope = "delegated_out_of_scope";
    public const string EscalatedOutOfScope = "escalated_out_of_scope";
    public const string DeniedByPolicy = "denied_by_policy";
}

public static class AuditReasonCodes
{
    public const string IdentityFallbackMissingConfig = "identity_fallback_missing_config";
    public const string IdentityFallbackIncompleteProfile = "identity_fallback_incomplete_profile";
    public const string BoundaryDelegateOutOfScope = "boundary_delegate_out_of_scope";
    public const string BoundaryDelegatePolicyRestriction = "boundary_delegate_policy_restriction";
    public const string BoundaryEscalateOutOfScope = "boundary_escalate_out_of_scope";
    public const string BoundaryDeniedByPolicy = "boundary_denied_by_policy";
}
