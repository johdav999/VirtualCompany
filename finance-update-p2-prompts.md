# Finance Update P2 Implementation Prompts

Priority: P2 — Broad Finance application coverage for Laura  
Prompt order: execute Prompts 1–8 in order after `finance-update-p0-prompts.md` and `finance-update-p1-prompts.md` are complete.

## Shared execution contract

- Every prompt implements production behavior. Read and follow `production-implementation.md`, `docs/architecture-rules.md`, repository `AGENTS.md` files, and current code. UI work also follows `docs/design.md` and `ui-instructions.md`.
- Build domain-level Finance tools around authoritative Application contracts and services; do not create one tool per page, query EF from controllers, duplicate calculations, or expose provider schemas.
- Extend the existing shared registry, effective authority, natural-language planner, guardrails, approvals, audit, workflows, background execution, typed clients, and Finance evidence model.
- Tool classes are deliberate: reads inspect current facts, recommendations/drafts create reviewable internal proposals, and execute tools invoke existing authoritative commands only after P0/P1 checks.
- Posted journals, closed periods, approved snapshots, finalized reports, statutory archives, and audit evidence remain immutable. Corrections use existing governed reversal/reopen/replacement paths.
- External writes, object generation, and long-running work use existing outbox/background/idempotency/reconciliation boundaries. Never claim queued work succeeded.
- Company/actor/agent/record scope, source versions, allowed actions, freshness, and explicit unsupported cases are required for every tool.
- Database changes use additive SQL Server EF migrations and full upgrade/no-pending verification. Swedish accounting remains technically reviewable, not statutory approval.
- Finish every prompt with production implementation, tests, documentation, observability, and no deferred in-scope TODOs.

---

## Prompt 1 — Effective Finance agent coverage catalogue

### 1. Title and outcome

Implement a maintained capability catalogue that maps the Finance product's domain workflows to available read, recommend/draft, execute, and permanently human-only agent operations.

### 2. Current context

- The Web project contains 46 Finance pages spanning daily finance, accounting, close, compliance, reporting, audit, integrations, and administration.
- Laura currently has 42 registered Finance tools, concentrated in core transaction/invoice queries and accounting-provider migration.
- Six role-analysis capabilities cover cash, payables, receivables, accounting treatment, close analysis, and operating cadence.
- There is no authoritative product-to-agent coverage contract or test that detects unclassified Finance workflows.

### 3. Dependencies

- All P0 and P1 prompts.

### 4. Implementation requirements

- Add a versioned Application-level Finance agent capability catalogue organized by domain workflow, not route, with stable capability ID, purpose, supported operations, required permission/scope, risk tier, approval behavior, integrations, source types, and availability reason.
- Classify current Finance workflows and tools as implemented read, implemented recommend/draft, implemented execute, configuration-dependent, unsupported, or human-only.
- Bind every registered Finance tool to exactly one owning capability and make duplicate/unowned tools a test failure.
- Expose an authorized effective-coverage API and typed client projection for Laura and administrators, including counts and safe gap explanations.
- Add explicit human-only declarations for payment initiation, credentials, final statutory filing/sign-off, final close/year-end authority, self-approval, and ambiguous provider resolution.
- Document how future Finance features must register and test agent coverage without automatically granting it.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Classification must not infer capability from a Razor route or controller action alone.
- A coverage entry does not grant permission; P0 effective authority remains authoritative.

### 6. Acceptance criteria

- Given any registered Finance tool, when the catalogue is validated, then it has one owner, action class, permission, risk, and support state.
- Given a new Finance tool without catalogue metadata, then tests fail and Laura does not receive it.
- Given a human-only operation, then the planner explains the boundary and offers safe navigation or preparation rather than fabricating execution.

### 7. Verification

- Add completeness, uniqueness, permission/risk consistency, support-state, and effective-projection tests.
- Add contract tests comparing catalogue, registry, planner projection, and execution.
- Capture the initial classified coverage baseline in documentation generated from code metadata.

### 8. Definition of done

Finance agent coverage is explicit, test-enforced, user-visible, and safe to extend without hidden privilege expansion.

---

## Prompt 2 — Ledger, period, and financial-report read tools

### 1. Title and outcome

Give Laura source-backed read access to the general ledger, journals, fiscal periods, balances, trial balance, statements, registers, and report snapshots needed to answer accounting questions.

### 2. Current context

- Authoritative ledger, journal, period, account, balance, trial-balance, statement, report-definition, snapshot, drill-down, and export services exist from prior Finance releases.
- Laura currently has cash, transaction, P&L, and bounded agent-query reads but not broad accounting report inspection tools.

### 3. Dependencies

Prompt 1.

### 4. Implementation requirements

- Add versioned read tools for chart/account lookup, fiscal-period status, journal/register search, general-ledger detail, trial balance, supported statements, report definitions/versions, report snapshots, and source drill-down.
- Reuse authoritative report/read services and preserve pagination, period, currency, dimension, and as-of semantics.
- Return control totals, mapping/version, snapshot/checksum, freshness, truncation, source IDs, and current allowed actions.
- Support bounded natural-language references to accounts, voucher series, journals, periods, report names, and lines with explicit ambiguity handling.
- Reject uninitialized accounting, unsupported report variants, stale snapshots, cross-company IDs, and unbounded exports with actionable states.
- Register tools in the P0/P2 catalogues with `FinanceView` and read-only classification.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Read tools never post, repair, remap, refresh a finalized snapshot, or bypass reporting locks.
- Reports must reconcile to the same immutable journal truth used by the UI.

### 6. Acceptance criteria

- Given “Why did account 6540 change in August?”, then Laura returns the bounded ledger/journal evidence and source links without recalculating balances.
- Given a closed report snapshot, when read repeatedly, then definition version and checksum remain identical.
- Given unsupported or unmapped data, then the blocker is explicit rather than silently classified.

### 7. Verification

- Add golden ledger/report fixtures, pagination, drill-down, checksum, period/currency/dimension, ambiguity, authorization, and tenant tests.
- Add schema/manifest and P1 natural-language selection tests.
- Run existing ledger, report, snapshot, export, and performance regressions.

### 8. Definition of done

Laura can explain supported accounting balances and reports using the same reproducible evidence available to Finance users.

---

## Prompt 3 — Close, compliance, audit-package, and year-end read tools

### 1. Title and outcome

Give Laura complete read and explanation coverage for close readiness, compliance obligations, audit packages, accountant collaboration, and year-end state without granting final authority.

### 2. Current context

- Versioned close templates/instances, readiness hashes, tasks, sign-offs, locks, compliance calendar, submission evidence, audit packages, accountant access, and year-end workflows exist.
- Existing Finance close analysis surfaces a subset of readiness evidence.
- Direct authority filing and professional statutory approval remain outside the product's technical authority.

### 3. Dependencies

Prompts 1–2.

### 4. Implementation requirements

- Add read tools for close instance/task graph, readiness snapshot/blockers, waivers/sign-offs, period lock history, compliance obligations/submission evidence, audit-package definitions/runs/artifacts, accountant access/activity, and year-end readiness/rollover history.
- Preserve exact versions, hashes, evidence links, owners, due dates, approval state, materiality, and allowed actions.
- Add explanation/recommendation tools for prioritizing close blockers, missing evidence, compliance preparation, audit-package completeness, and year-end prerequisites.
- Clearly distinguish technical readiness, manual submission evidence, provider acknowledgement, human approval, and statutory sign-off.
- Enforce source/object access and one-time download rules; tools return authorized metadata or links rather than embedding protected artifacts.
- Register all capabilities with explicit human-only final lock, rollover, filing, and professional approval boundaries.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Recommendations cannot waive blockers, sign off, lock/reopen periods, approve packages, or claim compliance.
- Package content and accountant access remain company- and grant-scoped.

### 6. Acceptance criteria

- Given “What blocks August close?”, then Laura returns the current readiness hash, blockers, owners, evidence age, and safe next actions.
- Given manual filing evidence without acknowledgement, then Laura does not describe the obligation as submitted or accepted.
- Given protected or expired package access, then no object content or renewed link is exposed without the owning authorization path.
- Given human approval is pending, then technical verification remains distinct from statutory approval.

### 7. Verification

- Add close/compliance/package/year-end fixture, state, hash, object access, grant, authorization, tenant, and natural-language tests.
- Add negative-boundary tests for filing, sign-off, lock, rollover, and protected downloads.
- Run P4 close/compliance proof suites and Swedish accounting technical verification.

### 8. Definition of done

Laura can explain and coordinate close/compliance evidence while every final accounting and statutory authority remains human-controlled.

---

## Prompt 4 — Advanced accounting and subledger read tools

### 1. Title and outcome

Extend Laura's read coverage to reconciliation, statement imports, receivables/payables detail, multi-currency, dimensions, schedules, and fixed assets.

### 2. Current context

- Prior Finance packages provide bank/statement operations, reconciliation, AR/AP, payments, exchange rates/revaluation, dimensions, accounting schedules, and fixed assets.
- The agent currently analyzes receivables/payables and basic accounting candidates but lacks explicit tools over most advanced accounting state.

### 3. Dependencies

Prompts 1–3.

### 4. Implementation requirements

- Add bounded read tools for statement imports/lines, reconciliation sessions/matches/exceptions, receivable/payable allocations and settlement state, payment proposals/batches/executions, exchange-rate sources/sets and revaluation evidence, dimension structures/usage, schedule state/calculations, and fixed-asset registers/depreciation/disposal history.
- Reuse owning Finance services and expose deterministic eligibility, confidence, warnings, evidence, current versions, and allowed actions.
- Add recommendation tools for reconciliation review, stale rate/evidence remediation, schedule/asset review, and subledger exception prioritization without changing authoritative state.
- Represent explicitly unsupported inventory quantity, valuation, and COGS accounting rather than answering from adjacent commerce data.
- Bound all list/search tools and retain currency, period, source, and object/version provenance.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- AI cannot invent exchange rates, matches, allocations, depreciation, tax treatment, or asset values.
- Cross-capability data is accessed through Application contracts, not sibling Infrastructure references.

### 6. Acceptance criteria

- Given a reconciliation exception, then Laura can explain candidate evidence and deterministic confidence without applying a match.
- Given stale or unapproved exchange rates, then revaluation advice is review-required and no amount is fabricated.
- Given an asset or schedule, then calculations match the owning deterministic service exactly.
- Given an inventory-accounting question, then the unsupported boundary is stated clearly.

### 7. Verification

- Add reconciliation, subledger, FX, dimension, schedule, asset, unsupported-boundary, manifest, authorization, tenant, and natural-language tests.
- Run existing P2/P3 Finance suites and SQL-specific concurrency/calculation tests.
- Verify bounded performance at supported small volume.

### 8. Definition of done

Laura can inspect and explain the supported advanced accounting estate without duplicating calculations or crossing explicit product boundaries.

---

## Prompt 5 — Journal, reconciliation, and accounting draft tools

### 1. Title and outcome

Allow Laura to create safe, incomplete, reviewable accounting drafts for journals, reconciliation decisions, and corrections without posting or applying them.

### 2. Current context

- Manual journal drafts/previews/submission, proposed entries, reconciliation decisions, accounting treatment recommendations, reversals/corrections, approvals, and source links exist.
- P1 supports preview, confirmation, approval, and durable multi-step runs.

### 3. Dependencies

Prompts 1–4.

### 4. Implementation requirements

- Add recommend/draft tools for manual journal drafts, correction/reversal proposals, reconciliation decision drafts, and accounting-treatment proposals using existing owning services.
- Require source record IDs/versions, period, currency, balanced lines, account/dimension eligibility, rationale, evidence, and idempotency.
- Mark model-derived descriptions or selections as proposed; amounts, tax, FX, balancing, period eligibility, and posting rules remain deterministic.
- Validate drafts immediately and return blockers, warnings, missing evidence, approval requirements, and safe editable fields.
- Permit explicit submission of a current reviewed draft into the existing approval/workflow path as a separate execute tool; do not post in the same action.
- Preserve draft/review/submission history and link it to the conversational plan and audit chain.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- No draft tool may post a journal, apply a reconciliation, reopen a period, or overwrite an existing reviewed draft.
- Corrections retain links to original accounting and use governed reversal/replacement paths.

### 6. Acceptance criteria

- Given incomplete evidence or an unbalanced proposal, when a draft is requested, then validation issues are retained and submission is blocked.
- Given an approved account/dimension selection, then the draft is reviewable but not posted.
- Given a stale source or changed draft, when submission is attempted, then it is rejected and must be refreshed.
- Given replay, then one draft/submission exists for the same business idempotency key.

### 7. Verification

- Add balance, period, account, dimension, tax/FX delegation, source-version, correction, idempotency, approval, tenant, and audit tests.
- Add P1 conversational preview/confirmation tests and Web/API contract tests.
- Run manual journal, posting, reconciliation, and close-lock regressions.

### 8. Definition of done

Laura can accelerate accounting preparation while reviewed submission and posting remain distinct authoritative steps.

---

## Prompt 6 — Close, compliance, audit, schedule, FX, and asset proposal tools

### 1. Title and outcome

Allow Laura to prepare reviewable operational proposals across close, compliance evidence, audit packages, schedules, revaluation, and assets without acquiring final authority.

### 2. Current context

- Existing workflows support close tasks/evidence, compliance obligations, audit-package definitions, schedules, FX revaluation, fixed assets, tasks, approvals, and background generation.
- P2 Prompts 3–4 provide complete read context, and Prompt 5 establishes safe accounting draft patterns.

### 3. Dependencies

Prompts 1–5.

### 4. Implementation requirements

- Add recommendation/draft tools for close-task assignments and evidence requests, compliance evidence checklists, audit-package definition/run previews, schedule proposals, revaluation previews, and asset addition/disposal/depreciation proposals.
- Reuse exact owning validation/calculation services and retain target versions, source evidence, proposed changes, blockers, approvals, and expected downstream effects.
- Add separate guarded execute tools only for submitting current proposals, assigning eligible close tasks, requesting evidence, or starting approved internal generation/calculation workflows.
- Generated files and long-running calculations use object storage/background execution with checksums, idempotency, failure visibility, and coordinated recovery.
- Keep final close/lock/reopen, year-end rollover, statutory filing/sign-off, provider credential changes, external delivery, and final accounting posting outside direct agent execution.
- Create typed handoffs/tasks when another responsible human or agent owns the next step.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Proposal generation cannot mark evidence complete, approve its own work, or convert a technical result into a statutory claim.
- Existing segregation-of-duties and materiality policies remain authoritative.

### 6. Acceptance criteria

- Given a close blocker owned by another role, then Laura can propose or create an authorized task but cannot sign it off.
- Given an audit-package preview, then no artifact is generated until the owning guarded command is confirmed/approved.
- Given an FX/asset/schedule proposal, then all figures match deterministic services and remain unposted.
- Given object-generation failure, then the run is visibly failed/retryable as classified and never shown as downloadable success.

### 7. Verification

- Add proposal, validation, task/handoff, approval, object/background, checksum, recovery, segregation, tenant, and natural-language tests.
- Run close/compliance, audit package, schedule, FX, fixed asset, outbox, and recovery regressions.
- Verify protected object access and retention.

### 8. Definition of done

Laura can prepare and coordinate advanced Finance work while sensitive conclusions and final actions remain governed by owning workflows.

---

## Prompt 7 — Guarded internal Finance command tools

### 1. Title and outcome

Expose a minimal allowlist of reversible or approval-backed Finance commands so Laura can complete supervised internal work without broad controller or database access.

### 2. Current context

- Existing tools can categorize transactions, change invoice approval status, post eligible paid-bill expenses, and operate approved migration workflows.
- Prompts 5–6 introduce new draft/submission and internal workflow commands.
- P0/P1 provide actor authorization, risk policy, preview, approval, continuation, idempotency, and audit.

### 3. Dependencies

Prompts 1–6.

### 4. Implementation requirements

- Define an explicit readiness contract for each execute tool covering authorization, risk, reversibility, approval, target/version, idempotency, transactional behavior, external effects, retries, reconciliation, audit, and rollback/recovery.
- Expose only commands whose owning service already enforces authoritative eligibility; remove or disable any path that bypasses the owning application policy.
- Support bounded batch size and financial/materiality exposure per request; partially eligible batches must produce per-item decisions and must not silently skip failures.
- Recheck all P0/P1 and Finance-domain decisions immediately before mutation.
- Provide safe after-state reads and exact requested/actual effect summaries.
- Keep the permanent human-only operations declared in Prompt 1 unavailable even at elevated agent autonomy.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Do not add generic CRUD, arbitrary endpoint, SQL, expression, file, browser, or provider-call tools.
- An agent tool must never be more permissive than the equivalent human API command.

### 6. Acceptance criteria

- Given any enabled execute tool, then its readiness contract and owning regression test prove no authorization or policy bypass.
- Given a mixed batch, then each item has an explicit outcome and no ineligible item mutates.
- Given stale or duplicate execution, then no repeated effect occurs.
- Given a human-only request, then it remains unsupported and auditable rather than routed to a generic command.

### 7. Verification

- Add parameterized readiness, role, tenant, risk, approval, stale-state, batch, idempotency, rollback, audit, and recovery tests for every execute tool.
- Add SQL Server transaction/concurrency tests and provider/outbox reconciliation tests where applicable.
- Rerun the complete P0 adversarial matrix.

### 8. Definition of done

Laura has a narrow, reviewable set of production-safe Finance commands with no generic escape hatch into the application.

---

## Prompt 8 — Unified Finance agent coverage UX and P2 release proof

### 1. Title and outcome

Integrate Laura's expanded coverage into the Finance and Agents workspaces and complete P2 with current, revision-bound functional and safety evidence.

### 2. Current context

- Finance uses a consolidated daily information architecture plus detailed accounting, close, compliance, and administration routes.
- Agent insights, “Message Laura” links, analysis workbench, approvals, audit, and transparency surfaces already exist.
- Prompts 1–7 add broad read, proposal, and guarded-command coverage.

### 3. Dependencies

Prompts 1–7.

### 4. Implementation requirements

- Apply the mandatory screenshot-first workflow in `docs/design.md` for any materially new or redesigned agent coverage/workbench screen.
- Integrate context-aware Laura entry points into relevant Finance workflows without adding retired pages to primary navigation or duplicating the global conversation workbench.
- Show supported actions, human-only boundaries, evidence, drafts, approvals, run progress, requested/actual effects, and source/audit links in plain language.
- Add a coverage view for authorized users showing implemented read/recommend/execute/human-only capabilities by workflow, configuration gaps, and last verification version.
- Complete English/Swedish localization, responsive behavior, keyboard/screen-reader support, safe loading/empty/error/stale states, and local date/money formatting.
- Generate a P2 release manifest covering catalog completeness, tools, tests, SQL/object recovery, capacity, browser UAT, and unresolved external/human approvals.

### 5. Constraints and preservation rules

- Follow the Shared execution contract and `docs/design.md`.
- Avoid presenting tool count as percentage product completion; failed safety checkpoints remain release blockers.
- UI links and capability labels do not grant authority.

### 6. Acceptance criteria

- Given a supported Finance record/workflow, then the user can ask Laura from context and receive only capabilities valid for that actor and state.
- Given a human-only operation, then the boundary and correct human workflow are visible.
- Given P2 catalogue verification, then every enabled tool is owned, tested, localized where user-facing, and represented accurately.
- Given P2 release verification, then P0/P1 gates remain green and no earlier Finance behavior regresses.

### 7. Verification

- Run focused Finance agent catalog/tool/orchestration/UI tests, full Release build, hermetic matrix, and SQL Server lanes.
- Run accounting capacity at supported small volume and production-shaped audit-package/report generation evidence; retain unsupported medium results honestly.
- Perform authenticated EN/SV desktop/narrow/accessibility UAT for representative ledger, close, compliance, advanced accounting, draft, approval, and recovery flows.
- Run Swedish accounting technical verification and keep qualified human approval separate.

### 8. Definition of done

P2 is complete when Laura has broad, truthful, secure coverage of supported Finance workflows and the evidence proves what she can read, prepare, execute, and must leave to humans. `finance-update-p3-prompts.md` may then begin.
