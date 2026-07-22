# Finance Agent AI Implementation Prompts

## Purpose And Execution Order

This pack implements Laura's role-specific AI advice, analysis, and orchestration described in `agents-ai.md`. Execute the prompts in order. They extend the existing shared agent AI platform and authoritative Finance services; they do not create a Finance-only model stack.

1. Finance evidence and reasoning adapter
2. Cash and liquidity analysis
3. Payables and payment-run recommendations
4. Receivables and collections recommendations
5. Accounting-treatment recommendations
6. Variance, statement, and close analysis
7. Finance operating cadence and manager cockpit

## Instructions For Every Prompt

- Read and follow `AGENTS.md`, `production-implementation.md`, `agents-ai.md`, `architecture-inst.md` when present, and `/docs/architecture-rules.md`. For UI work also follow `ui-instructions.md` and `/docs/design.md`, including screenshot-first requirements.
- Use the shared capability catalog, `IAgentReasoningGateway`, persisted orchestration runs, grounded sources, typed handoffs, governed memory candidates, AI quality events, tasks, workflows, approvals, audit, and background execution already in the repository.
- Finance records and deterministic Finance policies remain authoritative. AI must not determine balances, payment state, tax liability, posting eligibility, approval eligibility, or provider success.
- External writes must retain existing policy checks, approval, idempotency, durable execution, retry, and reconciliation behavior.
- Every company-owned read and write must be authorized and company-scoped. Schema changes require an EF migration, model snapshot, migration discovery and pending-model verification, and compatible local/Docker SQL Server paths.
- Implement production behavior with no mock production data, parallel orchestration stack, silent AI fallback, or deferred in-scope TODOs.

---

## Prompt 1: Establish Finance AI Evidence And Shared Reasoning Contracts

### 1. Title And Outcome

Implement the Finance-owned evidence adapters and structured recommendation contracts that allow Laura to use the shared AI reasoning platform over authoritative Finance data. This creates one safe foundation for every later Finance analysis without duplicating calculations or provider access.

### 2. Current Context

- Shared AI contracts and persistence exist in `Application/Agents/SharedAgentAiContracts.cs`, `AgentCapabilityCatalog`, `SharedAgentReasoningGateway`, and `AgentOrchestrationRun`.
- Finance read contracts are split under `Application/Finance/Contracts`; `IFinanceReadService`, `CompanyFinanceReadService.*`, `FinanceAgentInsightRepository`, normalized insights, anomaly records, statements, planning, bills, invoices, and payments already expose deterministic facts.
- `FinanceIntelligenceHeuristics`, `FinancialChecks`, policy configuration, source policy, reconciliation scoring, and paid-expense eligibility already own deterministic decisions.
- Finance has narrative hints and agent-query DTOs, but no single versioned Finance AI evidence envelope or role-specific recommendation contract consistently linked to shared runs and quality events.

### 3. Dependencies

- Shared AI implementation from `shared-ai.md`.
- No external credentials are required for deterministic and fake-provider tests.

### 4. Implementation Requirements

- Add narrowly owned Finance application contracts for evidence snapshots and AI recommendations under `Application/Finance/Contracts/InsightsAndPolicyContracts.cs` or a more specific new file.
- Build Finance evidence adapters for cash, obligations, bills, invoices, payments, budgets, forecasts, anomalies, statement lines, reconciliations, policies, and linked documents by composing existing read services. Do not query EF from controllers or duplicate Finance formulas.
- Include source type/ID, as-of time, freshness, authoritative value, currency/period, policy outcome, contradiction markers, and links safe for the requesting agent.
- Register stable Finance capability IDs/versions in the shared catalog for later prompts, with action classes and explicit required scopes/tools.
- Add one Finance reasoning facade that invokes `IAgentReasoningGateway`, validates Finance-specific output schemas, and emits fact/inference/unknown claims, confidence, missing evidence, source IDs, allowed next actions, and review state.
- Reject unsupported source IDs, stale snapshots where freshness is material, cross-currency comparisons without authoritative conversion, and recommendations that contradict deterministic policy.
- Persist material recommendations through existing run/audit/quality infrastructure. Do not persist hidden prompts or copied provider payloads.
- Add authorized run/detail queries needed for Finance drill-down while preserving existing routes.

### 5. Constraints And Preservation Rules

- Follow every instruction in the pack preamble.
- Finance module services are registered in `AddFinanceModule`; genuinely shared services remain in shared composition.
- AI output is read/recommend only in this prompt. It may not call Finance command services or provider adapters.
- Preserve all existing Finance API contracts, calculations, record-source rules, simulations, and Fortnox behavior.

### 6. Acceptance Criteria

- Given an authorized Finance agent, when evidence is assembled, then every value maps to an existing company-scoped Finance source and includes an as-of timestamp.
- Given a provider result citing an absent source, when validated, then the recommendation is rejected or marked for review and the citation is not exposed.
- Given a deterministic policy that blocks an action, when AI recommends it, then the action is removed and the conflict is recorded visibly.
- Given another company's record ID, when evidence is requested, then no record or metadata is disclosed.

### 7. Verification

- Unit-test evidence mapping, freshness, currency/period handling, contradiction detection, schema validation, and allowed-action filtering.
- Add fake-provider integration tests for valid, malformed, unsupported-citation, timeout, and policy-conflict results.
- Add authorization and cross-tenant API tests plus migration checks if persistence changes.
- Run affected Finance agent-query, insight, anomaly, shared-AI, audit, API, and Web builds.

### 8. Definition Of Done

Finance AI has one production evidence and reasoning boundary over authoritative Finance services. Later capabilities can reuse it without direct provider calls, duplicated calculations, unsupported claims, or unscoped data access.

---

## Prompt 2: Implement Cash, Liquidity, Runway, And Scenario Advice

### 1. Title And Outcome

Implement source-backed cash and short-term liquidity analysis that explains current cash, obligations, runway pressure, scenario changes, and decisions requiring attention while deterministic calculations remain authoritative.

### 2. Current Context

- `IFinanceReadService`, cash-position workflows, planning baselines, budgets, Finance scenarios, recurring outflows, obligation coverage, dashboard snapshots, burn-rate insights, and Finance policy thresholds already exist.
- Existing analytics calculate values and some narrative hints; they do not provide a versioned, claim-level AI explanation across baseline, upside, and downside scenarios.
- Shared briefing, tasks, quality events, and Finance evidence contracts from Prompt 1 are available.

### 3. Dependencies

- Prompt 1.
- Existing Finance entry state must be initialized; uninitialized Finance is an explicit unavailable state.

### 4. Implementation Requirements

- Add a Finance application query for a bounded cash-analysis horizon and scenario selection using existing deterministic scenario factories and planning services.
- Calculate cash, due obligations, inflow assumptions, burn, runway, covenant/policy thresholds, and confidence bands deterministically before invoking AI.
- Use AI only to explain drivers, material changes, uncertainty, concentration, assumptions, and recommended internal follow-ups.
- Return structured baseline/upside/downside results, driver contributions, confirmed facts, inferences, unknowns, sensitivity points, and source links.
- Detect missing/stale bank, invoice, bill, budget, or forecast evidence and lower confidence or require review rather than filling gaps.
- Permit only internal next actions such as review, task creation, scenario refresh, or approval navigation. Any payment or integration action stays in existing commands.
- Expose authorized API and typed Web client queries and integrate a concise analysis into the Finance overview/cash surface with drill-down to source records.
- Emit recommendation-produced, viewed, accepted/corrected feedback hooks and technical latency/failure metrics.

### 5. Constraints And Preservation Rules

- Follow every instruction in the pack preamble.
- Never let the model calculate ledger balances, exchange rates, runway, or threshold breaches.
- Clearly label simulation, forecast, and actual data. Simulation data must never appear as actual company performance.
- Preserve current dashboard and planning response contracts unless extending them compatibly.

### 6. Acceptance Criteria

- Given current cash and known obligations, when analysis runs, then totals match existing Finance deterministic services exactly.
- Given a downside scenario, when displayed, then assumptions and differences from baseline are explicit and source-linked.
- Given stale bank data, when analysis runs, then freshness is visible and no high-confidence recommendation is produced.
- Given insufficient evidence, then the result identifies missing sources and does not invent future inflows.

### 7. Verification

- Unit-test scenario arithmetic delegation, freshness thresholds, confidence rules, and source coverage.
- Add integration tests for healthy cash, low cash, obligation pressure, missing planning data, mixed currencies, and tenant isolation.
- Add Web tests for scenario selection, facts versus assumptions, source drill-down, empty/error states, and responsive layout.
- Run cash-position, planning, variance, dashboard, shared reasoning, API, and Web suites.

### 8. Definition Of Done

Laura can explain cash and liquidity scenarios with exact authoritative values, bounded uncertainty, and actionable internal recommendations. No model-derived balances, hidden assumptions, or direct money movement exists.

---

## Prompt 3: Implement Payables And Payment-Run Recommendations

### 1. Title And Outcome

Implement AI-supported payables prioritization that recommends which supplier bills to pay, defer, dispute, or review and explains cash impact, due-date risk, supplier concentration, duplicates, and policy constraints.

### 2. Current Context

- Bills, bill detail, payable-pressure insights, due-soon recommendations, payment proposals, allocations, duplicate checks, reconciliation, supplier enrichment, expense posting, approvals, exports, and Fortnox execution already exist.
- `PaidSupplierBillExpensePostingEligibility` and Finance payment/approval policies are authoritative.
- Existing recommendations are primarily deterministic and record-level; there is no governed portfolio payment-run analysis tied to cash scenarios and shared AI evidence.

### 3. Dependencies

- Prompts 1-2.
- Provider execution requires the existing configured Finance integration and approval path, but recommendation tests do not.

### 4. Implementation Requirements

- Add a company-scoped payment-run recommendation query accepting cutoff date, cash reserve policy, optional maximum outflow, and included currencies.
- Build the candidate set from existing bill/payment/reconciliation services and exclude paid, cancelled, credited, duplicated, blocked, or otherwise ineligible records deterministically.
- Calculate urgency, late-payment exposure, available discounts when authoritative, supplier concentration, cash impact, duplicate risk, approval state, and deterministic action eligibility.
- Ask AI to explain tradeoffs and sequencing only after the deterministic ranking and constraints are fixed. AI may not alter amounts, dates, status, or eligibility.
- Return recommended groups (`pay`, `defer`, `dispute_or_review`, `not_eligible`), reasons, cash-before/after values, evidence, confidence, missing data, and required approvals.
- Allow explicit reviewed conversion of selected eligible recommendations into existing payment proposals/tasks. Recheck current state, policy, and idempotency before command execution.
- Keep export/provider execution separate and approval-backed with existing retry/reconciliation behavior.
- Add API, typed client, and Finance payment-workbench presentation with review, selection, conflict refresh, and safe failure states.

### 5. Constraints And Preservation Rules

- Follow every instruction in the pack preamble.
- AI never creates a payment, changes a bill, selects a bank account, or overrides approval.
- Do not duplicate payment proposal, expense posting, duplicate detection, or Fortnox command logic.
- A stale recommendation cannot be committed without recalculation and version/state checks.

### 6. Acceptance Criteria

- Given paid or cancelled bills, when a payment run is analyzed, then they cannot enter an executable recommendation.
- Given a reserve threshold, when proposed payments would breach it, then deterministic policy blocks or reduces the executable selection.
- Given a duplicate-risk bill, then it is routed to review and never silently included.
- Given repeated commit delivery, then one set of payment proposals/tasks is created.

### 7. Verification

- Unit-test candidate eligibility, deterministic ranking inputs, cash-reserve enforcement, stale-state checks, and idempotency.
- Add SQL-backed integration tests for bill/payment lifecycle, proposal creation, approval preservation, concurrency, and cross-tenant access.
- Add fake-provider tests proving AI cannot modify amounts or eligibility.
- Add Web tests for grouped recommendations, selection, cash impact, review conflicts, and approval links.

### 8. Definition Of Done

Laura produces a reviewable, source-backed payment-run recommendation and can convert eligible reviewed items through existing workflows. Money movement and provider writes remain governed and recoverable.

---

## Prompt 4: Implement Receivables, Collection Risk, And Reminder Strategy

### 1. Title And Outcome

Implement AI-supported receivables analysis that prioritizes overdue customers, estimates collection risk from authoritative history, recommends approved reminder strategies, and coordinates strategic-account follow-up with Sales.

### 2. Current Context

- Finance invoices, historical receivable payments, open receivables, overdue recommendations, allocations, customer counterparties, and cash analytics already exist.
- Sales owns contacts, accounts, deals, relationship activity, and communication automation; shared typed handoffs exist.
- Current Finance data can identify overdue items but does not provide one evidence-backed portfolio collections plan with relationship-aware escalation.

### 3. Dependencies

- Prompts 1-2.
- Shared handoff implementation from `shared-ai.md`.

### 4. Implementation Requirements

- Add deterministic collection-risk features: days overdue, amount, payment history, disputes/credits, concentration, promises or reminders when authoritative, and current allocation state.
- Define a transparent risk band and priority score in Finance policy code. AI explains context and suggests a strategy but cannot set invoice status or assert payment probability as fact.
- Return a collections plan with customer/invoice grouping, confirmed facts, risk factors, recommended contact timing/channel, approved message intent, next review date, and source evidence.
- Retrieve only permitted Sales relationship context through an application contract or typed handoff; Finance must not query Sales tables directly.
- For strategic or relationship-sensitive accounts, create an idempotent `customer_payment_risk` handoff to Alex rather than initiating communication.
- Draft reminder content only from approved company communication and collections policies. Sending remains a separate consent/policy/approval/outbox operation.
- Record outcomes such as paid, promise kept/broken, disputed, corrected, or escalated for quality measurement without claiming causal attribution.
- Add authorized API, typed Web client, and collections-plan UI integrated with receivables.

### 5. Constraints And Preservation Rules

- Follow every instruction in the pack preamble.
- Do not infer legal rights, fees, customer promises, or dispute resolution from model output.
- Avoid exposing unrestricted Sales or Support records to Finance.
- Preserve existing invoice, allocation, communication, and handoff systems of record.

### 6. Acceptance Criteria

- Given overdue invoices, when analyzed, then priority factors reconcile to authoritative invoice/payment history.
- Given an active dispute or credit, then the invoice is not recommended for a routine reminder.
- Given a strategic account, then one scoped Sales handoff is created and no unrestricted relationship history is copied.
- Given unsupported customer context, then the recommendation labels it unknown rather than inventing an explanation.

### 7. Verification

- Unit-test risk features, policy bands, dispute suppression, strategy eligibility, and handoff minimization.
- Add integration tests for payment histories, partial allocations, disputes, strategic-account handoffs, duplicate delivery, and tenant isolation.
- Add communication-policy and outbox tests if reminder execution is included.
- Add Web tests for customer grouping, evidence, handoff status, and safe missing-context behavior.

### 8. Definition Of Done

Laura provides a governed collections plan grounded in Finance history and coordinates sensitive accounts with Alex. No unsupported promises, unapproved messages, or cross-module data leakage remains.

---

## Prompt 5: Implement Governed Accounting-Treatment Recommendations

### 1. Title And Outcome

Implement explainable recommendations for ledger account, VAT treatment, dimensions, accruals, prepayments, and corrections while deterministic accounting policy and human approval retain control of postings.

### 2. Current Context

- Supplier invoice extraction, enrichment, account mapping, reconciliation, draft actions, expense posting, Finance accounts, statement mappings, and Fortnox adapters already exist.
- Historical mapping can currently produce poor suggestions when supplier mappings are weak, as seen in prior account `2000` behavior.
- `FinanceAccountCodePolicy`, record-source policy, paid-expense eligibility, and provider draft/bookkeeping services own authoritative constraints.

### 3. Dependencies

- Prompt 1.
- Processed source documents and valid chart-of-accounts data.

### 4. Implementation Requirements

- Build an accounting recommendation service over extracted document facts, supplier history, chart of accounts, tax configuration, policy documents, statement mappings, and reviewed prior outcomes.
- Separate candidate generation from deterministic validation. Filter control, liability, receivable, bank, unsupported VAT, closed-period, and otherwise invalid accounts before ranking.
- Return ranked candidates with account/VAT/dimension treatment, evidence, historical support, confidence, contradictions, missing documents, and review requirement.
- Implement accrual/prepayment/correction recommendations only when deterministic period, amount, and source-document prerequisites are satisfied.
- Ensure learned supplier mappings enter through governed memory or reviewed mapping commands; never activate raw model output automatically.
- Allow explicit reviewed application to existing supplier-invoice draft/enrichment commands. Recheck bill editability, approval, account validity, tax rules, and concurrency.
- Capture accepted, corrected, and rejected recommendations with bounded reason codes to improve quality metrics.
- Integrate the recommendation into bill/invoice review surfaces using plain-English explanations and source links.

### 5. Constraints And Preservation Rules

- Follow every instruction in the pack preamble.
- AI cannot post an entry, invent a tax rate, reopen a period, or treat a control account as an expense.
- Preserve `PaidSupplierBillExpensePostingEligibility` as the paid-bill authority.
- Do not train or fine-tune on tenant data; use scoped retrieval and reviewed memory/mappings.

### 6. Acceptance Criteria

- Given supplier history mapping to a non-expense control account, when recommending an expense account, then the control account is excluded with a stable reason.
- Given insufficient VAT evidence, then the result requires review and does not invent treatment.
- Given an accepted recommendation, when applied, then existing draft/enrichment policy revalidates the current bill.
- Given a correction by a reviewer, then quality and governed mapping evidence are recorded without auto-changing unrelated suppliers.

### 7. Verification

- Unit-test account-class exclusions, VAT prerequisites, candidate ranking inputs, period rules, and confidence/review thresholds.
- Add integration tests for supplier history, new suppliers, conflicting mappings, accrual/prepayment, paid bills, closed periods, and tenant isolation.
- Extend expense-posting, enrichment, draft-action, mapping, migration, and Fortnox boundary tests.
- Add Web tests for ranked candidates, excluded reasons, correction feedback, and apply conflicts.

### 8. Definition Of Done

Laura provides useful accounting-treatment recommendations that cannot bypass chart, VAT, period, posting, or approval policy. Reviewed outcomes are measurable and safely reusable.

---

## Prompt 6: Implement Variance, Statement Narrative, And Close Readiness

### 1. Title And Outcome

Implement grounded financial narrative and month-end close analysis covering actual-versus-budget/forecast variance, P&L and balance-sheet movements, reconciliation completeness, missing evidence, and close sequencing.

### 2. Current Context

- Budgets, planning baselines, variance queries, reporting periods, trial balances, financial statement mappings/snapshots, reconciliations, anomalies, normalized insights, and period-close services already exist.
- Existing narrative hints are deterministic but do not provide source-linked materiality explanations, hypotheses, or a bounded close plan.
- Shared planning creates reviewable tasks; Finance workflows and approvals own close operations.

### 3. Dependencies

- Prompts 1 and 5.
- Complete deterministic statement mappings for periods being analyzed.

### 4. Implementation Requirements

- Define deterministic materiality, comparison-period, freshness, mapping-completeness, reconciliation, and close-readiness policies.
- Build variance contributions and account movements from existing read models before AI interpretation.
- Produce a structured narrative with material facts, likely drivers labeled as hypotheses, contradictory evidence, missing explanations, source links, and management questions.
- Create a close-readiness result with checklist items, authoritative completion/block state, owner, due date, dependency, supporting evidence, and recommended sequence.
- Reuse shared bounded planning to prepare tasks only after explicit review; do not let AI close periods, post journals, or mark reconciliations complete.
- Preserve regenerated statement snapshots and closed-period immutability. Recommendations based on superseded snapshots must become stale.
- Add daily/monthly briefing contributions and CEO decision-brief output without merging conflicts into false certainty.
- Add authorized API/client and Finance reporting/close UI extensions with source drill-down.

### 5. Constraints And Preservation Rules

- Follow every instruction in the pack preamble.
- AI must not calculate statement totals or decide that a control is complete.
- Do not mutate closed-period data or create journal entries from narrative output.
- Reuse reporting, mapping, reconciliation, workflow, and task services.

### 6. Acceptance Criteria

- Given a material variance, when analyzed, then the amount matches the deterministic variance result and every claimed driver is fact, inference, or unknown.
- Given incomplete statement mappings, then readiness is blocked and the narrative cannot claim complete financial statements.
- Given unresolved reconciliation items, then close completion remains unavailable regardless of AI confidence.
- Given a refreshed snapshot, then prior analysis is marked stale or versioned against its original snapshot.

### 7. Verification

- Unit-test materiality, readiness rules, snapshot versioning, source coverage, and plan validation.
- Add integration tests for mapped/unmapped statements, budget/forecast variance, unresolved reconciliations, close blockers, closed periods, and cross-tenant access.
- Extend reporting-period, statement snapshot, planning, briefing, audit, and shared-plan tests.
- Add Web tests for narratives, hypothesis labels, checklist dependencies, stale state, and responsive presentation.

### 8. Definition Of Done

Laura explains financial performance and prepares a trustworthy close checklist from authoritative statements and controls. Analysis is versioned, source-linked, and incapable of closing or posting by itself.

---

## Prompt 7: Implement Laura's Governed Operating Cadence And Finance Cockpit

### 1. Title And Outcome

Implement Laura's daily, weekly, and monthly operating cadence and consolidate Finance AI recommendations into an actionable manager cockpit with priorities, approvals, exceptions, handoffs, and measured outcomes.

### 2. Current Context

- Shared briefing scheduling/update jobs, work prioritization, plans, handoffs, memory candidates, and AI quality metrics exist.
- Finance background services already refresh insights, analytics, integrations, approval tasks, reporting, simulations, and workflow triggers.
- Finance and executive cockpit surfaces already expose KPIs, insights, issues, approvals, and activity, but role-specific AI outputs are not coordinated as one idempotent cadence.

### 3. Dependencies

- Prompts 1-6.
- Shared AI prompts 4, 5, 8, 9, and 10.

### 4. Implementation Requirements

- Define versioned daily, weekly, and monthly Finance cadence manifests with deterministic prerequisites, bounded company batches, idempotency windows, and explicit outputs.
- Daily: cash/obligation changes, due bills/invoices, anomalies, failed integrations, approvals, and urgent collection/payment tasks.
- Weekly: payment-run proposal, collections plan, liquidity scenarios, supplier/customer concentration, and forecast changes.
- Monthly: reconciliation and close readiness, variance/statement narrative, control exceptions, and CEO Finance decision brief.
- Orchestrate existing services and Prompts 2-6 through background execution. Do not create duplicate insight, task, approval, or briefing records on retries.
- Use typed handoffs for won-deal readiness, strategic payment risk, and refund/dispute outcomes with minimum evidence and legal transitions.
- Surface capability/integration unavailable states, stale data, provider failures, approval waits, and reconciliation ambiguity as operator-visible work.
- Build or extend the Finance cockpit using existing read projections: priorities, recommendations, source evidence, approvals, exceptions, handoffs, integration health, recent outcomes, and quality sample size.
- Include explicit feedback actions and autonomy-review recommendations, but never raise autonomy automatically.
- Audit material cadence outcomes and retain technical metrics without sensitive prompts or provider payloads.

### 5. Constraints And Preservation Rules

- Follow every instruction in the pack preamble.
- Scheduled AI work cannot perform money movement, posting, unusual credits/refunds, write-offs, or provider writes without existing policy and approval.
- Reuse existing hosted/background services and execution scope; register the cadence once in `AddFinanceModule`.
- If the cockpit is a significant redesign, perform and save the required reference screenshot before implementation.

### 6. Acceptance Criteria

- Given duplicate scheduler delivery, when a cadence runs, then one logical set of recommendations/tasks/briefing contributions exists.
- Given missing provider credentials, then deterministic Finance analysis still runs where possible and provider-dependent capabilities show actionable failure.
- Given an approval-gated recommendation, then the cockpit links to the authoritative approval and cannot execute it directly.
- Given a small quality sample, then reliability components and insufficient-evidence state are visible and autonomy remains unchanged.

### 7. Verification

- Unit-test cadence selection, idempotency identities, prerequisite evaluation, bounded batching, and briefing contribution mapping.
- Add background retry/concurrency tests, handoff tests, approval preservation, audit assertions, and cross-tenant tests.
- Add cockpit API projection and Web component tests for loading, empty, stale, failed, approval, and source-drilldown states.
- Perform responsive browser/screenshot verification if the UI change is substantial; run Finance, shared-AI, briefing, workflow, approval, API, and Web suites.

### 8. Definition Of Done

Laura operates a durable Finance cadence and presents a coherent, evidence-backed action cockpit. Retries are idempotent, failures are visible, sensitive actions remain governed, and measured outcomes can support future human-approved autonomy changes.
