# Financial App P4 Implementation Prompts

Priority: P4 — Close, Compliance, and Accountant Workspace  
Source roadmap: [financial-roadmap.md](financial-roadmap.md) Release 6  
Prompt order: execute Prompts 1–10 in order. The package turns existing reports and period locking into controlled month-end, year-end, compliance-review, and external-accountant workflows.

## Shared execution contract

Every prompt in this package is an implementation prompt, not an analysis or scaffolding task.

- Read and follow `production-implementation.md`, `docs/architecture-rules.md`, repository `AGENTS.md` files, and current implementation before editing.
- Preserve immutable journals, source/evidence links, reporting locks, statement/trial-balance snapshots, tax review, VAT returns, SIE/statutory archives, control reconciliations, approvals, tasks, audit, recovery verification, Releases 0–2, P2 banking, and P3 subledgers.
- Posted journals and finalized/snapshotted reports are immutable. Corrections require reopen policy, linked reversals, replacement snapshots, future-period correction, or subsequent-event treatment with retained history.
- Close and compliance readiness, materiality, allowed actions, segregation of duties, sign-off, and reopening are authoritative backend policies. Chat/UI/AI cannot override blockers or approvals.
- Every template, close instance/task, dependency, evidence request, note, sign-off, report definition/version, snapshot, package artifact, collaboration record, role grant, audit event, and object key is company-scoped. Add cross-company access tests.
- External submissions or package deliveries are external side effects and must follow the approval/outbox/idempotency/acknowledgement/reconciliation rules in `docs/architecture-rules.md`.
- Database changes use additive SQL Server EF migrations with representative upgrade and no-pending-model verification; object artifacts use checksums and coordinated recovery rules.
- UI work follows `ui-instructions.md` and the mandatory screenshot-first workflow in `docs/design.md`, with typed Web clients, English/Swedish localization, responsive/accessibility verification, and plain language.
- Swedish statutory features remain an unvalidated engineering candidate until exact-version qualified evidence is checked in. Do not turn a close workflow or technical export into a compliance claim.
- Finish each prompt with production implementation, tests, documentation, operations evidence, and no deferred in-scope TODOs.

---

## Prompt 1 — Versioned close templates, instances, and accountable tasks

### 1. Title and outcome

Implement reusable close templates and period-specific close instances so every month/year-end activity has an owner, due date, dependency, evidence requirement, and retained history.

### 2. Current context

- Fiscal periods, close validation/lock/reopen history, company tasks, approvals, documents, reconciliations, VAT, exports, insights, and worker operations exist.
- Close is currently a set of validations and screens rather than a durable orchestration aggregate with templates, task dependencies, sign-offs, and materiality.

### 3. Dependencies

- Releases 0–2.
- P2/P3 readiness signals should be consumed when those packages are implemented; template contracts must tolerate explicitly unavailable capabilities.

### 4. Implementation requirements

- Add versioned close templates, sections, task definitions, dependency graph, default owners/roles, offsets/due dates, evidence requirements, sign-off rules, materiality settings, and activation history.
- Add close instances bound to company/fiscal period/template version with generated tasks, status history, owners, due dates, evidence links, notes, blockers, approvals, and optimistic concurrency.
- Support create/preview/activate/copy/version/retire templates and start/assign/reassign/complete/reopen/cancel close tasks with stable idempotency.
- Integrate existing task/approval/document systems by reference rather than duplicating them; retain exact target and evidence access checks.
- Add commands/queries/APIs, audit, telemetry, migration, indexes, and template/close operations documentation.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Template edits never mutate an active/historical close instance; create a new version.
- Dependency cycles, cross-company owners/evidence, and unauthorized completion/sign-off are rejected.

### 6. Acceptance criteria

- Given an activated template, when a close starts, then one task graph is created with retained template version and no duplicate tasks on replay.
- Given an incomplete predecessor, when a dependent task is completed, then backend policy blocks it with a stable reason.
- Given another company's user or document, when assigned/referenced, then no existence or content is exposed.

### 7. Verification

- Template version, graph/cycle, generation, ownership, dependency, evidence, concurrency, idempotency, and history tests.
- API authorization/tenant tests, EF migration/upgrade/no-pending checks, and task/approval/document integration regressions.

### 8. Definition of done

- Period closes have durable, versioned, accountable work plans with no spreadsheet-only or UI-only task authority.

---

## Prompt 2 — Evidence-backed close readiness, sign-off, and lock orchestration

### 1. Title and outcome

Make period close a governed workflow that aggregates authoritative blockers, enforces materiality and segregation of duties, captures sign-off, and locks only a current reconciled period.

### 2. Current context

- `CompanyReportingPeriodCloseService`, accounting readiness, period history, reporting locks, reconciliations, approvals, audits, and source snapshots exist.
- Current close validation does not orchestrate every close task/subledger signal or retain prepared-by/reviewed-by evidence in one close decision.

### 3. Dependencies

- P4 Prompt 1.
- P2/P3 close checks where their capabilities are enabled.

### 4. Implementation requirements

- Add a close-readiness policy and snapshot covering bank, AR, AP, VAT/tax, suspense, open approvals, provider/delivery backlog, document gaps, exports, schedules, accruals, revaluation, assets, dimensions, and task/evidence completion.
- Add company materiality thresholds and explicit exception/waiver proposals requiring reason, amount/evidence, approval, expiry, and retained reviewer identity.
- Implement prepare, refresh, submit for review, approve/reject, lock, cancel, and controlled reopen requests with expected versions/hashes and segregation of duties.
- Recheck every authoritative signal and approval immediately before lock/reopen; atomically persist lock/history/snapshot/audit and enqueue post-close work only afterward.
- Add operator-visible stale/failure states, telemetry, APIs, and runbook procedures.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- A completed checklist does not override an accounting blocker; a waiver applies only to explicitly waivable checks and exact evidence/version.
- Period lock/reopen remains a sensitive backend action and never occurs from a read request or AI recommendation.

### 6. Acceptance criteria

- Given all required tasks and reconciliations current, when an independent reviewer approves and lock executes, then one immutable close snapshot and lock history commit.
- Given a stale reconciliation, changed journal, expired waiver, or self-review violation, when lock is attempted, then nothing locks.
- Given reopen, when approved, then reason, scope, actor, prior snapshot, and later correction path remain traceable.

### 7. Verification

- Readiness aggregation, materiality/waiver, stale hash, segregation, lock atomicity, reopen, concurrency, and idempotency tests.
- SQL Server transaction tests, authorization/tenant tests, audit/recovery checks, and all subledger close-blocker regressions.

### 8. Definition of done

- Close and reopen are reproducible policy decisions backed by current evidence, responsible owners, and immutable sign-off history.

---

## Prompt 3 — Complete statutory and management statement suite

### 1. Title and outcome

Complete the core report suite with cash flow, equity changes, comparatives, rolling periods, aged ledgers, registers, tax detail, and dimension reporting tied to immutable journals.

### 2. Current context

- General ledger, trial balance, P&L, balance sheet, tax summary, control reconciliation, snapshots, drill-down, and exports exist.
- Cash-flow and equity statements, comparative/rolling views, aged AP, journal/fixed-asset registers, full tax detail, and dimension reports are incomplete.

### 3. Dependencies

- P3 advanced ledger/subledgers for final FX, dimension, schedule, and asset reporting.
- Qualified review is required before labeling jurisdiction-specific layouts statutory-compliant.

### 4. Implementation requirements

- Add deterministic cash-flow statement using governed direct/indirect mappings as selected, statement of changes in equity, comparative periods, rolling twelve months, and retained calculation/mapping versions.
- Add aged AR/AP, journal register, fixed-asset register, tax-detail, currency, and dimension reports with pagination/export and control totals.
- Extend financial-statement mappings with lifecycle/effective-date rules and explicit unmapped/conflicting account blockers; never mutate historical classifications.
- Add immutable report snapshots/checksums, evidence/provenance, drill-down to journal/source/subledger, typed APIs/clients, background exports, telemetry, and migration where needed.
- Define reproducibility and performance budgets for small/medium supported volumes.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Reports read authoritative journal/subledger projections and do not repair accounting state.
- Currency and dimension totals must reconcile to the same journal lines, not separate denormalized truth.

### 6. Acceptance criteria

- Given a closed period, when a report is regenerated from its retained definition/version, then totals and checksum match the approved snapshot.
- Given a report amount, when drilled down, then journal lines, source documents, dimensions/rates, and subledger items explain it.
- Given unmapped or inconsistent facts, when a close/statutory report is requested, then the blocker is explicit rather than silently classified.

### 7. Verification

- Golden statement/register fixtures, mapping/version, currency/dimension, aging, comparative, drill-down, checksum, and close-blocker tests.
- SQL performance/plan tests, tenant/authorization tests, export safety, migration, and existing-report regressions.

### 8. Definition of done

- The complete supported statement/register suite is reproducible, explainable, performant, and reconciled to immutable accounting truth.

---

## Prompt 4 — Versioned report definitions and custom layouts

### 1. Title and outcome

Allow finance teams and accountants to define controlled report layouts without changing historical account classification or report snapshots.

### 2. Current context

- Financial-statement mappings and snapshots exist, but layouts and groupings are largely implementation-defined.
- There is no complete version lifecycle for custom sections, formulas, presentation order, comparisons, validation, approval, and effective dates.

### 3. Dependencies

- P4 Prompt 3.

### 4. Implementation requirements

- Add report definition, version, section/line, account-group mapping, formula/reference, display rule, comparison, validation result, approval, effective date, and retirement records.
- Support copy from system template, draft/edit/validate/preview/submit/approve/activate/retire with optimistic concurrency and stable idempotency.
- Detect cycles, duplicate account coverage, missing mappings, sign errors, invalid formulas, and incompatible currency/dimension use.
- Bind generated snapshots/exports to exact definition versions; later edits are prospective and cannot mutate closed output.
- Add authorized APIs, administration UI, audit, telemetry, migration, and documentation for safe layout governance.

### 5. Constraints and preservation rules

- Follow the Shared execution contract and screenshot-first workflow for the report designer.
- No arbitrary executable expressions, database queries, or tenant-crossing references are allowed in formulas.
- System/statutory templates are immutable; customization creates company-owned versions.

### 6. Acceptance criteria

- Given a valid approved definition, when activated, then reports use it only from its effective period and retain its version.
- Given a cyclic/imbalanced/overlapping mapping, when validated, then activation is blocked with precise issues.
- Given a later layout edit, when an older closed snapshot is opened, then it remains unchanged and reproducible.

### 7. Verification

- Formula/parser, graph, mapping coverage, effective date, approval, version, snapshot binding, concurrency, and tenant tests.
- API/UI/browser, migration, report performance, export, and historical-regression tests.

### 8. Definition of done

- Custom reporting is safe, versioned, reviewable, and never rewrites historical accounting or closed statements.

---

## Prompt 5 — Compliance calendar and statutory submission evidence

### 1. Title and outcome

Create a compliance calendar and governed submission evidence lifecycle so statutory obligations, reviews, exports, acknowledgements, and corrections are visible even when authority submission remains external.

### 2. Current context

- Swedish profile, VAT periods/returns, statutory documents, SIE/archive exports, tasks, approvals, outbox, and audit exist.
- There is no unified obligation calendar, filing ownership, submission-provider contract, acknowledgement history, or explicit manual-submission evidence workflow.

### 3. Dependencies

- P4 Prompts 1–4.
- Qualified Swedish review for production statutory claims.
- A selected authority/submission provider is required only if direct electronic submission is included; otherwise implement an honest export/manual-evidence lifecycle.

### 4. Implementation requirements

- Add compliance obligation definitions/instances, jurisdiction/pack/version, due dates, owner, status, required reports/returns/evidence, approval, submission mode, acknowledgement, correction link, and history.
- Generate obligations from company profile/policy-pack facts and integrate them into close tasks without duplicating VAT or statutory-return authorities.
- Implement prepare/review/approve/export/mark-manually-submitted-with-evidence/acknowledge/reject/correct workflows with strict permissions and source hashes.
- If direct submission is selected, use a provider adapter plus durable outbox, stable idempotency, signed acknowledgement/reconciliation, bounded retry, and operator recovery.
- Add calendar/list/detail APIs and UI, reminders/escalations, audit, telemetry, migration, retention, and runbook.

### 5. Constraints and preservation rules

- Follow the Shared execution contract and external-side-effect rules.
- Export generation, technical validation, send acceptance, authority receipt, and authority approval are distinct states.
- Never claim legal filing or compliance merely because an artifact was generated or uploaded by a user without evidence.

### 6. Acceptance criteria

- Given a configured company, when obligations are generated, then exact jurisdiction/pack/version and due-date rules are retained without duplicates.
- Given manual submission evidence, when recorded, then actor, file/reference/hash, time, and review state are retained without asserting authority approval.
- Given timeout or ambiguous direct submission, when processed, then the obligation enters reconciliation and is not blindly resent.

### 7. Verification

- Calendar/rule, generation/idempotency, review, evidence, correction, notification, and state-separation tests.
- Provider contract tests if applicable, authorization/tenant tests, migration, browser UAT, and qualified fixture review evidence.

### 8. Definition of done

- Every supported compliance obligation has a truthful, auditable lifecycle from preparation through evidence/acknowledgement, with unsupported submission paths explicit.

---

## Prompt 6 — Immutable audit and accounting evidence packages

### 1. Title and outcome

Generate downloadable, checksum-verifiable audit packages that let an external reviewer trace reported balances to journals, sources, approvals, reconciliations, policies, and documents without database access.

### 2. Current context

- General ledger, trial balance, statements, tax evidence, reconciliations, approvals, audit events, source documents, policy provenance, exports, object storage, and recovery checksums exist.
- There is no complete period audit-package manifest, bounded artifact assembly, reviewer index, or cross-artifact integrity verification.

### 3. Dependencies

- P4 Prompts 2–5.

### 4. Implementation requirements

- Add audit-package request/job, scope/version, approval, artifact manifest, item checksum, source reference, generation attempt, retention, download authorization, and verification result.
- Assemble selected trial balance, GL, statements, tax/returns, reconciliations, significant journals, approvals/sign-offs, close history, provider exceptions, policy-pack evidence, and accessible source documents.
- Generate a human-readable index and machine-readable manifest with deterministic ordering, per-item SHA-256, package checksum, missing/inaccessible evidence report, and exact snapshot/definition versions.
- Execute assembly in bounded background work with idempotency, retry, cancellation-before-finalization, object-storage recovery, audit, telemetry, and expiry/access controls.
- Add request/status/download/verify APIs and close/accountant workspace integration.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Package creation never broadens document access or copies secrets/raw provider credentials.
- A package with missing required evidence is incomplete and cannot be labeled final.

### 6. Acceptance criteria

- Given a closed period, when a final package is generated twice for the same scope/version, then one logical result and identical manifest checksum are returned.
- Given inaccessible/missing/corrupt evidence, when assembly runs, then the package is blocked/incomplete with bounded details.
- Given restore, when package verification runs, then database/object references and hashes still match.

### 7. Verification

- Manifest/checksum, deterministic ordering, authorization, missing/corrupt object, worker restart, idempotency, retention, and redaction tests.
- SQL/object coordinated recovery, large-package performance, download security, and accountant access tests.

### 8. Definition of done

- Audit packages are complete, access-controlled, reproducible, and independently integrity-verifiable.

---

## Prompt 7 — External accountant access and collaboration

### 1. Title and outcome

Add a least-privilege accountant role and collaboration workflow for review notes, evidence requests, sign-offs, and client responses across authorized companies.

### 2. Current context

- Company membership, finance permissions, tasks, approvals, messages/notifications, documents, comments-like histories, audit, and company selector exist.
- There is no purpose-built accountant role, client portfolio grant, prepared/reviewed separation, immutable review note, or evidence-request lifecycle.

### 3. Dependencies

- P4 Prompts 1–6.

### 4. Implementation requirements

- Add accountant role/permission mapping and explicit company engagement grants with scope, effective dates, inviter/approver, revocation, last access, and audit.
- Add review engagements, notes/findings, evidence requests/responses, assignments, due dates, resolution, sign-off, and immutable history linked to close/report/package/source targets.
- Enforce least privilege, context switching, segregation of duties, inaccessible-document handling, revoked-access behavior, and no implicit group-wide access.
- Add portfolio summary queries for deadlines, close status, VAT/compliance, unreconciled items, failed integrations, approvals, and evidence requests without cross-company data leakage.
- Add authorized APIs, notifications, UI, telemetry, migration, and operator/security documentation.

### 5. Constraints and preservation rules

- Follow the Shared execution contract and screenshot-first workflow for new accountant/portfolio screens.
- Portfolio views aggregate only companies with explicit active grants and preserve company context for every drill-down/action.
- Revocation blocks future access but retains attributable notes/sign-offs/history.

### 6. Acceptance criteria

- Given an accountant granted one company, when viewing the portfolio or guessing another ID, then only the granted company is visible.
- Given a preparer, when attempting their own required independent sign-off, then backend policy blocks it.
- Given revoked access, when the accountant follows an old deep link, then access is denied without exposing current data while prior audit remains.

### 7. Verification

- Permission matrix, grant/revoke/effective date, portfolio isolation, context-switch, segregation, evidence access, notification, and audit tests.
- API penetration-style cross-company tests, migration, UI/client/browser accessibility, and performance tests across many authorized companies.

### 8. Definition of done

- External accountants can collaborate and sign off within explicit company scopes without database access or weakened tenant isolation.

---

## Prompt 8 — Formal year-end rollover and subsequent-event control

### 1. Title and outcome

Implement controlled fiscal-year finalization, retained-earnings transfer, verified opening balances, reopening, and subsequent-event workflows without mutating the closed prior year.

### 2. Current context

- Fiscal years/periods, close/lock history, journals, voucher series, statements/snapshots, approvals, accounting authority, and reversals exist.
- There is no complete year-end run, retained-earnings mapping, opening-balance generation/reconciliation, subsequent-event note, or forward-correction lifecycle.

### 3. Dependencies

- P4 Prompts 1–7.
- P3 advanced-ledger subledgers and their year-end close checks.

### 4. Implementation requirements

- Add year-end run, readiness snapshot, retained-earnings proposal, opening-balance candidate, approval/sign-off, execution, reconciliation, subsequent-event, reopen/correction, and history records.
- Validate all periods closed, subledgers/control accounts reconciled, reports/tax/compliance current, no conflicting authority/provider work, and required year-end tasks/sign-offs complete.
- Generate retained-earnings and opening-balance journals through `IAccountingPostingService` with fiscal-year/series/source idempotency and atomic linkage.
- Verify next-year opening balances by account/currency/dimension against final prior-year closing balances; block activation on any difference.
- Add APIs, worker where bounded, UI integration, audit, telemetry, migration, recovery, and runbook.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Year-end never rewrites prior journals or snapshots. Reopen requires policy/approval; otherwise corrections post forward with linked evidence.
- Replay or concurrent execution cannot duplicate retained-earnings/opening journals.

### 6. Acceptance criteria

- Given a ready fiscal year, when rollover executes, then one retained-earnings/opening chain commits and balances reconcile exactly.
- Given a blocker or forced posting failure, when execution runs, then no partial year activation or orphan journal remains.
- Given a subsequent event after final close, when recorded, then disclosure/correction follows explicit reopen or forward policy with prior snapshots intact.

### 7. Verification

- Readiness, retained earnings, opening balance, currency/dimension, concurrency, rollback, idempotency, reopen, subsequent-event, and recovery tests.
- SQL Server atomicity/migration, tenant/authorization, report/close regressions, and end-to-end fiscal-year golden scenario.

### 8. Definition of done

- Year-end rollover is atomic, reconciled, recoverable, and historically immutable with controlled later-event handling.

---

## Prompt 9 — Unified close and accountant workspace

### 1. Title and outcome

Create a single close workspace where finance staff and authorized accountants can see priorities, complete evidence-backed tasks, review reports, sign off, and handle exceptions.

### 2. Current context

- Accounting reports/close, periods, journals, reconciliation, VAT, work center, documents, audit, and settings screens exist.
- Prompts 1–8 add close instances, report definitions, compliance, packages, accountant collaboration, and year-end workflows that need coherent daily navigation.

### 3. Dependencies

- P4 Prompts 1–8.

### 4. Implementation requirements

- Build a close cockpit with period selector, readiness summary, task/dependency timeline, owners/due dates, evidence, reconciliations, reports, compliance obligations, sign-offs, exceptions, package status, and year-end actions.
- Build accountant portfolio/engagement views with explicit company context and safe deep links into the same close evidence.
- Use backend allowed-action decisions for task completion, waiver, sign-off, lock/reopen, package, and rollover actions.
- Add notifications/action-center links, source/journal/report drill-down, stale/loading/error/empty states, localization, accessibility, responsive behavior, and telemetry.

### 5. Constraints and preservation rules

- Follow the Shared execution contract. The mandatory screenshot-first workflow in `docs/design.md` applies.
- Do not duplicate transactional reads/calculations in Razor components or merge system administration into close work.
- A green visual state must be backed by the current readiness snapshot and timestamp.

### 6. Acceptance criteria

- Given a close owner, when the cockpit loads, then every blocker has evidence, owner, status, and safe next action.
- Given an accountant with one engagement, when navigating portfolio and detail, then company context remains explicit and isolated.
- Given stale readiness after a new journal/provider event, when lock is attempted, then the UI shows the backend rejection and refreshes evidence.

### 7. Verification

- Read-model, action-policy, tenant/role, stale-state, notification, and typed-client/component tests.
- Authenticated English/Swedish desktop/narrow browser UAT, keyboard/screen-reader checks, and supported-volume performance evidence.

### 8. Definition of done

- Month/year-end and accountant collaboration are operable from one evidence-led workspace without hidden spreadsheets or UI-owned authority.

---

## Prompt 10 — Close/compliance production proof and operations

### 1. Title and outcome

Prove that close, reports, evidence packages, accountant access, compliance states, and year-end remain correct, secure, reproducible, and recoverable in production-shaped conditions.

### 2. Current context

- The repository has accounting integrity, production test-matrix, recovery, capacity, browser UAT, readiness, and release-evidence conventions.
- Prompts 1–9 create high-impact locking, signing, reporting, evidence, external access, and rollover behavior.

### 3. Dependencies

- P4 Prompts 1–9.
- Qualified review for statutory claims and approved non-production credentials for any direct submission provider.

### 4. Implementation requirements

- Add release readiness checks for overdue/blocked close tasks, unresolved reconciliations, stale reports, missing sign-offs/evidence, incomplete packages, compliance ambiguity, access anomalies, and failed rollover.
- Run a deterministic month/year-end scenario covering every subledger, reports, tax/compliance, close, package, accountant review, lock, restore verification, rollover, subsequent event, and approved correction.
- Run role/tenant penetration tests, fresh/upgrade migration, SQL concurrency/rollback, worker restart, object corruption/missing evidence, large reports/packages, and coordinated recovery.
- Complete authenticated English/Swedish browser UAT for finance and accountant roles and validate accessibility, narrow layout, time zones, and period/date boundaries.
- Publish supported-volume/SLO evidence, access-review procedure, close calendar operations, incident/recovery/forward-fix runbooks, and an evidence-backed go/no-go decision.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Missing professional, provider, browser, SQL, or recovery evidence is a release stop.
- Rollback preserves locks, snapshots, sign-offs, reports, packages, submissions, acknowledgements, year-end journals, audits, and hashes.

### 6. Acceptance criteria

- Given a closed period restored with object storage, when verified, then every report/package checksum and journal/source link matches.
- Given unauthorized cross-company/accountant access attempts, when executed across all surfaces, then they fail without existence disclosure.
- Given incomplete evidence, when release review runs, then the decision is no-go with explicit remediation.

### 7. Verification

- Full solution build and hermetic, SQL Server, Docker migration/restore, capacity/performance, browser, security, and approved provider lanes.
- Qualified accounting review of golden close/year-end/statutory fixtures and retained evidence.

### 8. Definition of done

- Close, Compliance, and Accountant Workspace has auditable release proof with no unresolved critical/high correctness, isolation, recovery, or statutory-claim gap.
