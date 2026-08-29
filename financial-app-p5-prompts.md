# Financial App P5 Implementation Prompts

Priority: P5 — Planning and Finance Intelligence  
Source roadmap: [financial-roadmap.md](financial-roadmap.md) Release 7  
Prompt order: execute Prompts 1–10 in order. Planning remains separate from posted accounting truth, while actuals and governed subledger commitments are its authoritative source evidence.

## Shared execution contract

Every prompt in this package is an implementation prompt, not an analysis or scaffolding task.

- Read and follow `production-implementation.md`, `docs/architecture-rules.md`, repository `AGENTS.md` files, and current implementation before editing.
- Preserve existing `Budget`/`Forecast` compatibility, variance queries, finance analytics, cash position/projections, revenue forecast snapshots, anomaly/insight records, tasks/approvals, Laura's governed tool boundary, shared AI orchestration, immutable ledger, and Releases 0–2 plus P2–P4 behavior.
- Actual accounting values come from authoritative read models/snapshots. Plans never post journals, change open items, move money, submit tax, close periods, or mutate source records.
- Every planning model, version, line, driver, assumption, scenario, comment, ownership assignment, approval, lock, forecast run, narrative, package, AI run, metric, audit event, and cache/object key is company-scoped. Add cross-company read/write tests.
- Planning calculations are deterministic application policies with explicit currency, period, dimension, driver/source, version, precision, and rounding. AI may explain or recommend but cannot become numeric or action authority.
- AI work must use the shared orchestration interfaces, bounded company-scoped sources, structured outputs, cited evidence, permissioned tools, safe failures, and retained rationale summaries—never hidden reasoning or direct feature-specific LLM calls.
- Scheduled/external management-pack delivery follows durable outbox/background execution with approval, stable idempotency, acknowledgement/reconciliation, bounded retry, audit, and telemetry.
- Database changes use additive SQL Server EF migrations, representative upgrades, and no-pending-model verification. UI work follows `ui-instructions.md` and the mandatory screenshot-first workflow in `docs/design.md`, with typed clients and complete English/Swedish localization/accessibility.
- Do not ship generated or simulated planning data as production truth. Demonstration/simulation mode stays explicit and isolated.
- Finish each prompt with production implementation, tests, documentation, measurable outcomes, and no deferred in-scope TODOs.

---

## Prompt 1 — Versioned planning model and workflow foundation

### 1. Title and outcome

Replace shallow planning rows with a versioned planning aggregate that supports ownership, workflow, locking, dimensions, provenance, and compatibility with existing budget/forecast reads.

### 2. Current context

- `Budget` and `Forecast` currently store company, account, month, version string, amount, currency, and optional cost center.
- Create/update budget endpoints, forecast reads, variance queries, baseline generation, analytics, and guided budget artifacts exist.
- There is no plan/version lifecycle, owner, status, comments, approval binding, lock, driver/source provenance, or optimistic version authority.

### 3. Dependencies

- Releases 0–2.
- P3 governed dimensions/currency should be used when present; unsupported capability remains explicit when P3 is not enabled.

### 4. Implementation requirements

- Add planning model, version, line, ownership, workflow status, comment, dimension assignment, source/driver reference, approval, lock, operation/idempotency, and immutable history records.
- Support plan types for budget and forecast; draft/edit/submit/approve/reject/lock/unlock-by-policy/archive/copy with expected version/hash and segregation where configured.
- Migrate existing rows into an explicit compatibility/baseline plan without inventing owner/approval facts; retain existing reads/routes while introducing new contracts.
- Implement bounded list/detail/version/comparison queries, typed reason codes, permissions, audit, telemetry, migration/indexes, and data lifecycle rules.
- Define calculation and source snapshot boundaries consumed by later prompts.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Locked versions are immutable; revision creates a successor version linked to its base.
- Planning account/dimension references must be company-scoped and effective for the planned period.

### 6. Acceptance criteria

- Given migrated budget rows, when read through old and new contracts, then amounts remain unchanged and compatibility provenance is explicit.
- Given a locked version, when edited, then the command conflicts and a new revision is required.
- Given replay/cross-company references, when processed, then one company-safe operation exists or access is rejected.

### 7. Verification

- Lifecycle, version, lock, copy, approval, compatibility migration, idempotency, concurrency, dimension/effective-date, and tenant tests.
- API authorization, SQL migration/upgrade/no-pending, existing variance/analytics regression, and typed-client tests.

### 8. Definition of done

- Budget and forecast data has a governed version/workflow authority while current integrations remain compatible.

---

## Prompt 2 — Annual budgeting, monthly phasing, and business drivers

### 1. Title and outcome

Implement a complete annual budget workflow with monthly phasing, drivers, assumptions, dimensions, ownership, review, and roll-forward.

### 2. Current context

- Prompt 1 supplies planning versions and lines; actuals, accounts, dimensions, customer/supplier subscriptions, revenue forecast snapshots, and historical baselines exist.
- Current budget entry is one account/month amount and lacks driver formulas, annual control totals, contributor workflow, or allocation/phasing tools.

### 3. Dependencies

- P5 Prompt 1.
- P3 dimensions/currency for dimensioned or multi-currency budgets.

### 4. Implementation requirements

- Add driver definitions/versions, assumptions, units, formulas from a safe bounded grammar, source mappings, phasing profiles, annual controls, contributor assignments, comments, and validation results.
- Support direct monthly entry, annual amount phasing, prior-year actual/plan copy, percentage growth, headcount/unit-price style drivers, and deterministic rounding residual placement.
- Add contributor submit/review/return/approve workflows, completeness checks, variance/materiality thresholds, and lock readiness.
- Build import/export with preview and version binding; reject formula injection, invalid accounts/dimensions/currencies, and stale target versions.
- Add commands/queries/APIs, audit, telemetry, migration, and budgeting operating guidance.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Driver formulas cannot query arbitrary tables, call external services, or mutate actuals.
- Copy/roll-forward retains source plan/actual snapshot/version and never silently refreshes historical inputs.

### 6. Acceptance criteria

- Given an annual target and phasing profile, when calculated, then monthly values reproduce and sum exactly to the annual control total.
- Given contributors and ownership scopes, when submitting, then completeness and access are enforced per assigned lines/dimensions.
- Given stale imports or invalid formulas, when committed, then no partial plan update survives.

### 7. Verification

- Driver grammar/calculation, phasing/rounding, copy, ownership, completeness, submission, import atomicity, and concurrency tests.
- Tenant/authorization, migration, performance for small/medium plans, export safety, and client/component tests.

### 8. Definition of done

- Finance users can build, review, approve, and lock a fully phased annual budget with reproducible drivers and ownership.

---

## Prompt 3 — Rolling forecasts, scenarios, assumptions, and sensitivities

### 1. Title and outcome

Add rolling forecast and scenario management so finance teams can revise future expectations without changing actuals or losing model/version evidence.

### 2. Current context

- Stored forecasts, revenue snapshots, variance queries, cash projections, deterministic finance scenarios, and Prompt 1 planning versions exist.
- There is no complete forecast horizon roll, scenario tree, assumption set, sensitivity run, forecast cut-off, or actual-replacement policy.

### 3. Dependencies

- P5 Prompts 1–2.

### 4. Implementation requirements

- Add forecast horizon/cut-off, scenario, assumption set/version, scenario lineage, driver override, sensitivity definition/run/result, source snapshot, and quality metadata.
- Implement roll-forward that freezes actual periods, extends horizon, copies eligible future assumptions, and retains base forecast/budget/source versions.
- Support base/upside/downside and company-defined scenarios, controlled overrides, side-by-side comparison, sensitivity ranges, and deterministic result hashes.
- Add submit/approve/lock/revise workflows, comments/owners, APIs, audit, telemetry, migration/indexes, and bounded calculation execution.
- Integrate revenue forecast snapshots, AR/AP commitments, subscriptions, and actual ledger sources without making unreviewed source updates silently change a locked forecast.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Actual periods are authoritative and cannot be overwritten by forecasts.
- Scenario/sensitivity results are planning outputs, not accounting entries or hidden AI-generated numbers.

### 6. Acceptance criteria

- Given a new actual month, when a forecast rolls, then actual periods freeze, horizon extends, and lineage/source versions remain traceable.
- Given the same assumptions/sources, when recalculated, then result hashes and values match.
- Given a locked forecast or stale source binding, when edited/recalculated under unsafe policy, then the operation is blocked or creates an explicit revision.

### 7. Verification

- Horizon/cut-off, roll, lineage, scenario, override, sensitivity, hash, approval/lock, concurrency, and source-binding tests.
- Tenant/authorization, migration, calculation performance, variance/report regressions, and UI/client tests.

### 8. Definition of done

- Rolling forecasts and scenarios are reproducible, versioned, reviewable, and cleanly separated from actual accounting.

---

## Prompt 4 — Evidence-based 13-week cash forecasting

### 1. Title and outcome

Implement a 13-week cash forecast that combines bank evidence, receivables, payables, subscriptions, payment batches, and imported commitments with source confidence and scenarios.

### 2. Current context

- Cash position/projections, AR collections, AP bills/subscriptions, payments, revenue forecasts, balances, and Laura cash analysis exist.
- There is no versioned weekly cash model with opening-balance evidence, timing assumptions, confidence, manual commitments, reconciliation to actual outcomes, or scenario workflow.

### 3. Dependencies

- P5 Prompt 3.
- P2 connected banking/treasury and P1 native payables for complete source coverage; absent sources must be disclosed, not simulated.

### 4. Implementation requirements

- Add cash forecast/run/version, weekly bucket, source item, timing rule, manual/imported commitment, confidence, scenario override, opening balance snapshot, and outcome comparison records.
- Project supported customer invoices/promises, supplier bills/subscriptions, approved/queued payments, payroll/other imported commitments, transfers, fees, and configured recurring flows.
- Implement deterministic timing/default/late-payment rules, currency conversion, double-count prevention, source freshness, confidence calculation, and liquidity threshold breach detection.
- Support prepare/recalculate/review/approve/lock/revise and compare forecast to realized bank cash with error/coverage metrics.
- Add APIs, tasks/alerts, audit, telemetry, migration, import preview, and operations guidance.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Missing bank/payroll/commitment evidence lowers confidence and is visible; it must not be filled with mock values.
- One source obligation may contribute once per scenario/version unless an explicit split/timing rule explains otherwise.

### 6. Acceptance criteria

- Given current sources, when the forecast calculates, then every weekly movement links to a source/rule and opening-to-closing cash rolls correctly.
- Given missing/stale feeds or commitments, when reviewed, then confidence and missing evidence are explicit.
- Given realized bank outcomes, when compared, then forecast error and timing variance are measured without rewriting the locked forecast.

### 7. Verification

- Source inclusion/deduplication, timing, weekly roll, currency, confidence, scenarios, threshold, lock, outcome, and performance tests.
- Tenant/authorization, migration, feed/source-failure, import, task/alert, and client/component tests.

### 8. Definition of done

- The 13-week forecast is source-linked, reproducible, confidence-aware, and useful for controlled liquidity decisions.

---

## Prompt 5 — Variance workflow, explanations, and corrective actions

### 1. Title and outcome

Turn budget/forecast/actual variance from a read-only query into a governed review workflow with ownership, materiality, explanations, evidence, and follow-up actions.

### 2. Current context

- Actual-versus-budget/forecast queries, financial drill-down, anomalies, insights, tasks, approvals, and Laura analysis exist.
- Variance rows do not have durable review cases, assigned owners, explanation versions, evidence, thresholds, resolution, or outcome measurement.

### 3. Dependencies

- P5 Prompts 1–4.

### 4. Implementation requirements

- Add variance policy/version, review case, materiality evaluation, owner, explanation, evidence link, source snapshot, action/task link, resolution, and history.
- Evaluate amount/percentage/trend thresholds by account/group/dimension/period with deterministic severity and suppression/exception rules.
- Provide drill-down from variance to actual journal/source and plan driver/assumption; detect stale explanations when underlying versions change.
- Integrate Laura to draft evidence-cited explanations/recommended review actions through shared AI orchestration; require human confirmation and retain uncertainty/missing evidence.
- Add commands/queries/APIs, notifications, audit, telemetry, migration/indexes, and metrics for overdue/unresolved/material variances.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- AI cannot change amounts, thresholds, status, tasks, or approvals; it provides structured draft text/recommendations only.
- Resolving a variance does not alter actuals or the locked plan.

### 6. Acceptance criteria

- Given a material variance, when generated, then owner, threshold/version, actual/plan sources, and due action are retained.
- Given an AI explanation, when evidence is missing or citations are invalid, then it remains needs-review and cannot be accepted as fact.
- Given changed actual/plan versions, when an old explanation is opened, then staleness is explicit.

### 7. Verification

- Threshold/materiality, case idempotency, ownership, stale source, resolution, escalation, and drill-down tests.
- AI structured-output/citation/permission/failure tests, authorization/tenant tests, migration, performance, and UI/client tests.

### 8. Definition of done

- Material variances become assigned, evidence-backed management work with measured resolution and no AI/accounting authority confusion.

---

## Prompt 6 — Versioned management reporting packs and delivery

### 1. Title and outcome

Generate approved management packs combining financial statements, KPIs, forecasts, variances, risks, narrative, and drill-down with immutable versions and durable delivery.

### 2. Current context

- Financial reports/snapshots/exports, planning versions, variances, insights, audit packages, documents, mailbox delivery, tasks, approvals, and background jobs exist.
- There is no reusable management-pack definition, scheduled generation, narrative review, audience/access control, or delivery acknowledgement.

### 3. Dependencies

- P5 Prompt 5.
- P4 report definitions and immutable report snapshots.

### 4. Implementation requirements

- Add pack definition/version, section, KPI/threshold, report/plan source binding, generation job, artifact/manifest/checksum, narrative draft/version, approval, audience, schedule, delivery attempt, acknowledgement, and history.
- Generate deterministic quantitative sections first; allow Laura to draft bounded evidence-cited narrative through shared AI orchestration with uncertainty and missing-evidence disclosure.
- Support preview/review/edit narrative/submit/approve/finalize/schedule/download/deliver with exact source versions and idempotency.
- Deliver externally only through durable outbox with current approval/audience authorization, bounded retry, provider acknowledgement, bounce/failure/reconciliation state, audit, and telemetry.
- Add APIs, typed clients, retention/security controls, migration, and runbook.

### 5. Constraints and preservation rules

- Follow the Shared execution contract and external-side-effect rules.
- AI-generated narrative never changes numerical content; confirmed facts cite included source IDs.
- A generated/finalized pack is not delivered until provider acknowledgement; later source changes create a new version.

### 6. Acceptance criteria

- Given fixed source versions, when a pack regenerates, then quantitative values and manifest checksum reproduce.
- Given an AI narrative with unsupported claims, when validated, then finalization is blocked or claims are marked unknown/removed.
- Given delivery timeout/replay, when processed, then one business delivery exists with honest acknowledgement/reconciliation state.

### 7. Verification

- Definition/version, KPI, source binding, checksum, narrative validation, approval, schedule, delivery idempotency/ambiguity, access, and retention tests.
- SQL/object recovery, provider contract, tenant/authorization, performance, export/download security, and UI/client tests.

### 8. Definition of done

- Management packs are reproducible, reviewed, securely delivered, and numerically authoritative independent of AI narrative.

---

## Prompt 7 — Laura finance preparation and grounded decision support

### 1. Title and outcome

Expand Laura from bounded analysis into a governed finance preparation assistant for reconciliations, coding, collections, payments, close evidence, variances, and management questions.

### 2. Current context

- Laura is a guided Finance Manager with shared model-backed analysis, evidence sources, read/recommend/limited execute tools, approval guardrails, audit, quality events, and deterministic Finance policies.
- Her current tool set is narrow and does not prepare the complete recurring finance operating cadence or cross-link P2–P5 workflows.

### 3. Dependencies

- P5 Prompts 1–6 and the applicable P2–P4 capabilities.

### 4. Implementation requirements

- Add permissioned read/recommend/prepare tools for reconciliation candidates, coding proposals, collection/payment priorities, close task/evidence preparation, variance explanations, cash scenarios, and management-pack narrative drafts.
- Add recurring daily/weekly/monthly finance analyses using existing workflow/scheduler/task systems with stable occurrence idempotency, bounded sources, owner escalation, and safe missing-evidence states.
- Add typed cross-agent handoffs for sales billing/terms, support refunds/credits, marketing spend/variance, and CEO decisions without using chat as system of record.
- Require deterministic backend eligibility and current approval for any later execute action; default new capabilities to read/recommend/prepare.
- Persist source citations, structured result/version, confidence, uncertainty, accepted/overridden outcome, audit, telemetry, and safe failure without storing hidden reasoning.

### 5. Constraints and preservation rules

- Follow the Shared execution contract and Agent/AI rules in `docs/architecture-rules.md`.
- Laura does not post, pay, submit tax, close/reopen, activate providers, change policy, or perform material write-offs through a recommendation/preparation tool.
- Tool outputs and handoffs remain company-scoped, schema-validated, and permissioned.

### 6. Acceptance criteria

- Given a scheduled finance cadence, when it runs twice for one occurrence, then one analysis/task set exists with cited current sources.
- Given missing/stale evidence, when Laura responds, then uncertainty is explicit and no unsupported action is offered.
- Given a cross-agent handoff, when accepted, then the typed target workflow/evidence exists and chat is only a presentation channel.

### 7. Verification

- Tool manifest/schema/permission, source grounding, tenant, cadence idempotency, handoff, approval boundary, prompt-injection, provider failure, and audit tests.
- End-to-end agent/task/workflow tests and UI presentation tests for citations, uncertainty, and allowed actions.

### 8. Definition of done

- Laura prepares high-value finance work from authoritative evidence while every sensitive decision remains governed outside the model.

---

## Prompt 8 — Finance intelligence quality, outcomes, and control monitoring

### 1. Title and outcome

Measure forecast and recommendation quality, overrides, time saved, unresolved exceptions, drift, and control breaches so finance automation can be governed by evidence.

### 2. Current context

- Agent orchestration runs, AI quality events, finance insights, anomaly decisions, accepted/overridden recommendations, telemetry, tasks, and audit exist in parts.
- There is no complete finance-intelligence metric model tying recommendations/forecasts to later outcomes and safe automation thresholds.

### 3. Dependencies

- P5 Prompt 7.

### 4. Implementation requirements

- Add versioned metric definitions and bounded outcome records for forecast error/coverage, recommendation acceptance, override reasons, false positives/negatives where observable, cycle time, unresolved exceptions, estimated time saved, and control breaches.
- Link each measurement to capability/version, model/provider where applicable, source/result IDs, company, cohort/time window, and later authoritative outcome without retaining chain-of-thought.
- Add drift/threshold policies, minimum sample rules, review alerts/tasks, disable/pause recommendations or automation scopes, and retained policy-change audit.
- Provide aggregate privacy-safe company dashboards and operator diagnostics; prevent sensitive payloads or cross-company benchmarking without an explicit anonymized product policy.
- Add APIs, telemetry, migration/retention, export, and governance documentation.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Do not infer false-negative ground truth where the later outcome is unavailable; label incomplete samples honestly.
- Metrics cannot automatically broaden autonomy or approval thresholds.

### 6. Acceptance criteria

- Given an accepted/overridden recommendation and later outcome, when measured, then the metric links to exact versions without hidden reasoning.
- Given insufficient or biased samples, when displayed, then confidence/coverage limitations are explicit.
- Given a drift/control threshold breach, when detected, then the affected capability can be paused and an owner task/audit record is created.

### 7. Verification

- Metric attribution, outcome timing, sample/coverage, override, drift, pause, retention, privacy, and idempotency tests.
- Tenant/authorization, migration, performance, dashboard/client, audit, and failure-path tests.

### 8. Definition of done

- Finance intelligence has measurable quality and safety feedback with explicit limits and operator control.

---

## Prompt 9 — Unified planning and finance-intelligence workspace

### 1. Title and outcome

Build a first-class planning workspace for budgets, forecasts, cash scenarios, variances, management packs, and Laura recommendations.

### 2. Current context

- Finance overview/monthly summary/cash/report pages and basic budget/variance APIs exist, but planning is not a primary complete product surface.
- Prompts 1–8 add complex version, workflow, driver, scenario, review, pack, and quality states.

### 3. Dependencies

- P5 Prompts 1–8.

### 4. Implementation requirements

- Add Finance Planning navigation with overview, budgets, forecasts/scenarios, 13-week cash, variances, management packs, and intelligence-quality views while preserving existing routes/contracts.
- Provide model/version selectors, workflow status, owners, comments, grid/month/dimension editing, driver/assumption panels, comparison/drill-down, review queues, source freshness, confidence, and allowed actions.
- Integrate Laura recommendations with citations/uncertainty and explicit accept/edit/reject/convert-to-task flows; never present generated narrative as accounting truth.
- Implement typed clients/presenters, bounded pagination/virtualization, accessible data-grid behavior, responsive/narrow states, localization, error/loading/empty/stale states, and usage telemetry.

### 5. Constraints and preservation rules

- Follow the Shared execution contract. The mandatory screenshot-first workflow in `docs/design.md` applies to every new/materially redesigned planning screen.
- UI never owns calculations, workflow transitions, approvals, locks, source selection, confidence, or AI permissions.
- Keep Simulation Lab and administrative AI/provider settings separate from production planning work.

### 6. Acceptance criteria

- Given a finance user, when creating through approving a budget/forecast, then all version/owner/source/lock states are visible and backend enforced.
- Given a variance or cash movement, when drilled down, then the user reaches plan drivers and authoritative actual/source evidence.
- Given Swedish locale, narrow viewport, or keyboard-only use, when completing the workflow, then content/actions remain accessible and localized.

### 7. Verification

- Typed client/presenter/component, authorization, route compatibility, stale/concurrent edit, large-grid, localization, and accessibility tests.
- Authenticated English/Swedish desktop/narrow browser UAT against generated references and supported-volume performance measurements.

### 8. Definition of done

- Planning and finance intelligence are coherent daily workflows rather than API-only data or disconnected dashboard cards.

---

## Prompt 10 — Planning and intelligence production proof

### 1. Title and outcome

Prove planning correctness, source lineage, workflow safety, AI grounding, delivery reliability, performance, and recovery and publish an evidence-backed release decision.

### 2. Current context

- The repository has production test-matrix, SQL/Docker recovery, capacity, authenticated UAT, AI orchestration/audit, readiness, and release-evidence patterns.
- Prompts 1–9 add governed plans, calculations, cash forecasts, variance cases, management packs, agent preparation, metrics, and UI.

### 3. Dependencies

- P5 Prompts 1–9.
- Dedicated SQL Server/Docker, coordinated object storage, configured shared AI non-production credentials, approved delivery credentials, and owned browser environment.

### 4. Implementation requirements

- Add readiness checks for incomplete/stale plan versions, missing owners/sources, failed calculations, cash-source gaps, unresolved material variances, stale packs, delivery ambiguity, AI grounding/schema failures, drift/control breaches, and worker backlog.
- Run one deterministic annual budget → rolling forecast → cash forecast → actual update → variance review → Laura explanation → management pack → approval/delivery → outcome measurement scenario.
- Prove calculation hashes, lineage, locked-version immutability, concurrency/idempotency, tenant isolation, AI citation/tool boundaries, delivery ambiguity, worker restart, and SQL/object restore.
- Measure small/medium plan grids, calculations, comparisons, cash forecasts, pack generation, and dashboards against documented SLOs.
- Complete authenticated English/Swedish browser UAT and publish deployment, feature controls, AI/provider outage, retention, recovery, rollback/forward-fix, and go/no-go evidence.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Missing AI/provider/browser/SQL/recovery evidence is a release stop for that capability, not a skipped pass; deterministic fallback may explain unavailability but may not fake a result.
- Rollback preserves plan versions, approvals/locks, calculation/source hashes, narratives, packages, deliveries, AI runs, quality events, audits, and objects.

### 6. Acceptance criteria

- Given fixed inputs, when calculations rerun after restore, then plan/forecast/cash/pack values and hashes reproduce.
- Given malicious/unsupported AI output or unavailable provider, when processed, then no plan/accounting/action state changes and safe failure evidence remains.
- Given incomplete release prerequisites, when assessed, then the result is no-go with explicit remediation.

### 7. Verification

- Full solution build and hermetic, SQL Server, Docker migration/restore, performance, browser, AI contract/security, and approved delivery-provider lanes.
- End-to-end tenant/authorization, prompt-injection, tool-permission, accessibility, localization, and recovery evidence review.

### 8. Definition of done

- Planning and Finance Intelligence has reproducible release evidence, governed AI behavior, measurable quality, and no unresolved critical/high correctness, isolation, delivery, or recovery gap.
