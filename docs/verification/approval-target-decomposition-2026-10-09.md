# Approval target decomposition verification

Approval target processing now uses the Application `IApprovalTargetHandler`
contract and scoped keyed registrations by `ApprovalTargetEntityType`. Each
owning module registers its own implementations:

| Module | Handlers |
| --- | --- |
| Operations | Task, Workflow, Action, OperatingPlan, OperatingDecision, AnnualPlanVersion |
| Sales | SalesMeetingInvitation, SalesMeetingChangeRequest, SalesMeetingChangeProposal, MarketingChannelAction |
| Finance | FinanceIntegrationWrite, AccountingProviderSwitchMappingDecision, AccountingProviderSwitchCutoverPlan, AccountingProviderSwitchActivation, AccountingProviderSwitchClosure, VatReturn, TreasurySource |

The 17 handlers preserve the targets supported by generic approval creation.
The other 12 enum targets continue to use their module workflows and are not
registered for generic creation. Some handlers intentionally provide only target
existence checks: their decisions already belong to the owning workflow. The
`fortnox_write` input alias continues to resolve to FinanceIntegrationWrite.

`CompanyApprovalRequestService` retains chain ordering, membership and reviewer
authorization, independent Finance review, locking, serializable transactions,
retry strategy, common audit/notifications, and cockpit invalidation. Its main
file fell from 2,606 to 1,354 lines. Target validation/binding, review material and
details, and outcome mutation moved to the handlers. Summary projections and
common review visibility remain in the coordinator.

Handlers resolve lazily to preserve approval-backed service composition and
optional dependencies. They share the coordinator's scoped database context;
no sibling Infrastructure project reference was added. Finance write retry and
Support refund refresh remain after decision persistence in the same position.
The existing standing-grant flow retains its original separate behavior.

Action processing is split into its target handler and focused partials for
Finance continuation validation, reviewed task-policy delivery, and policy/result
payloads. Reviewed internal actions retain their original durable topic and
idempotency keys, exact queued-task receipt, current authority checks, and
execution-time material revalidation. Support refund task callbacks continue
through the Application outcome interface.

## Verification

Evidence is under `artifacts/approval-target-refactor`.

- A syntax-token comparison against the captured original source passed for all
  17 existence checks, 11 outcome branches, and 8 review-material branches (36
  comparisons). Formatting and the transition record's type rename are excluded.
  Original review envelope names and canonical SHA-256 semantics are preserved.
- API approval regressions: **191 passed**, including decision chains, stale
  reviews, expiration, authorization, replay/concurrent reviewers, controlled
  actions, reviewed task execution, Finance approvals/autonomy, outbound write
  approvals, annual/quarterly planning, Sales scheduling/change commands,
  Marketing operating journeys, and accounting provider switching.
- Additional API target paths: **25 passed**. Four handler tests overlap the
  preceding run, giving **212 distinct API tests** in total. Additional coverage
  includes browser meeting scheduling, customer minutes delivery, operational
  Finance proposals, and close/compliance tools.
- Owning Finance VAT and treasury service tests: **15 passed**.
- Total distinct passing tests: **227**, with no failures or skips in the final
  selected runs. SQL Server categorized tests were excluded from API filters.
- Four new composed handler tests cover unique module ownership, scoped DI,
  unsupported targets, tenant-scoped missing lookups, a retained legacy review
  digest, nested-object ordering, array-order changes, business-material changes,
  foreign-company review/mutation rejection even with the target tracked, pending
  decisions, and the coordinator's responsibility for the final persistence.
- Final API and Web builds succeeded with zero errors. Incremental final builds
  reported zero warnings; earlier compilation emitted existing project warnings.
- Read-only EF `has-pending-model-changes` completed successfully: no model changes
  since the last migration. Existing model-validation warnings remain.

The initial new test fixtures omitted tenant context and failed; their contexts
were corrected without changing production query filters, then all four passed
in both selected API runs. No migrations, database data updates, live provider
execution, browser acceptance, or deployment were performed. The complete API
suite and external SQL Server concurrency tests were not run. Changes remain
uncommitted alongside the previous refactors in this checkout.
