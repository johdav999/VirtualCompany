# Finance Update P1 Implementation Prompts

Priority: P1 — Natural-language Finance tool planning and supervised execution  
Prompt order: execute Prompts 1–7 in order after `finance-update-p0-prompts.md` is complete.

## Shared execution contract

- Every prompt is a production implementation task. Read and follow `production-implementation.md`, `docs/architecture-rules.md`, repository `AGENTS.md` files, and current code. UI work also follows `docs/design.md` and `ui-instructions.md`.
- Reuse the shared agent orchestration, `IAgentReasoningGateway`, effective authority projection, trusted tool registry, policy guardrail, approval workflow, task/workflow infrastructure, audit, and typed Finance services. Do not create a Finance-only model stack or let Finance call an LLM provider directly.
- Model output may propose a plan but never grants authority, calculates accounting truth, changes policy, or confirms provider success. Every tool name, version, action, scope, argument, dependency, and result is deterministically validated.
- Read calls may run without confirmation only when current actor/agent authority permits them. Recommendations create reviewable output. Execute calls require the P0 policy decision and any confirmation or approval it specifies.
- All requests and plans are company-scoped, bounded, correlated, versioned, auditable, cancellable, and safe against prompt injection in user text, company records, retrieved documents, and tool results.
- Persist durable workflow state when a request spans approval, background work, external side effects, retries, or multiple turns. Do not treat chat as the system of record.
- Database changes use additive SQL Server EF migrations with upgrade and no-pending-model verification. Do not weaken existing tests or introduce fake production fallbacks.
- Finish every prompt with implementation, focused tests, documentation, observability, and no deferred in-scope TODOs.

---

## Prompt 1 — Versioned Finance tool-plan contract and bounded planner

### 1. Title and outcome

Implement a structured planning boundary that converts a user's Finance request into a bounded, reviewable tool plan using only currently permitted tool manifests.

### 2. Current context

- `SingleAgentOrchestrationService` currently executes explicit `toolInvocations` from the request or task payload.
- The shared reasoning gateway returns structured claims and next actions but does not produce an executable multi-tool Finance plan.
- The registry already exposes versioned input/output schemas and P0 supplies authoritative effective permissions.

### 3. Dependencies

- All P0 prompts.

### 4. Implementation requirements

- Add Application contracts for a tool-plan request, immutable plan/version, ordered steps, dependencies, expected action/effect, tool/version, normalized arguments, evidence requirements, confirmation/approval state, limits, and safe explanation.
- Add a shared orchestration planner using `IAgentReasoningGateway` with a strict versioned JSON result schema; validate and normalize the result before persisting or exposing it.
- Supply only permitted manifests and bounded relevant context. Do not put denied tools, secrets, hidden policy internals, unrestricted records, or provider payloads in the model input.
- Reject unknown tools, wrong versions/actions/scopes, schema-invalid arguments, cycles, excessive steps, unsupported dependencies, ungrounded target IDs, mixed companies, and actions beyond the user's request.
- Define configurable maximum steps, records, input/output size, model calls, tool calls, elapsed time, and estimated cost.
- Return explicit `ready`, `needs_clarification`, `confirmation_required`, `approval_required`, `unsupported`, and `failed` states with safe reason codes.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Planning is side-effect free except for its own plan/audit persistence.
- Never silently replace an unsupported request with a different action.

### 6. Acceptance criteria

- Given a request answerable by allowed tools, when planning completes, then every step references one current permitted manifest and schema-valid arguments.
- Given ambiguous targets or missing material inputs, then the plan requests clarification and executes nothing.
- Given injected instructions inside a Finance record or tool description, then they remain untrusted evidence and cannot add tools or authority.
- Given an unknown or denied tool from the model, then the entire invalid portion is rejected and auditable.

### 7. Verification

- Add planner schema, normalization, cycle, bounding, permission, target grounding, prompt-injection, malformed-provider, timeout, and cross-tenant tests.
- Use deterministic/fake reasoning providers; live-provider tests remain separately categorized.
- Run shared AI, agent orchestration, registry, P0 authorization, and audit suites.

### 8. Definition of done

Natural language can produce only a bounded, valid, authority-aware Finance tool plan and cannot directly execute anything.

---

## Prompt 2 — Safe Finance manifest projection and evidence grounding

### 1. Title and outcome

Give the planner the smallest accurate Finance tool and evidence context needed to select tools without exposing unrelated company data or internal implementation details.

### 2. Current context

- Effective tool authority, Finance evidence adapters, grounded context, source references, schemas, and Finance query services exist.
- Generic orchestration currently lists available tool names with broad action metadata.
- Finance tools need user-facing intent, preconditions, data freshness, target types, and expected effects to support reliable selection.

### 3. Dependencies

Prompt 1.

### 4. Implementation requirements

- Extend the trusted manifest with safe purpose, action class, target entity types, side-effect summary, required evidence/freshness, confirmation/approval behavior, result semantics, and supported natural-language examples; keep authorization and schemas authoritative.
- Build a company/actor/agent-scoped manifest projection that includes only effective permitted tools and redacts fields the actor cannot see.
- Add bounded Finance entity resolution for human references such as invoice number, bill number, customer, supplier, period label, and migration, returning explicit ambiguity rather than guessing.
- Ground candidate targets through authoritative Finance read services and retain source IDs/versions used for resolution.
- Add deterministic tool-ranking hints based on intent and target type without granting authority or hardcoding full workflows in prompts.
- Version and hash the projected manifest/evidence bundle so execution detects stale registry, permission, or target changes.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Do not expose record existence across company or actor access boundaries.
- Examples are selection hints, never executable instructions or permission grants.

### 6. Acceptance criteria

- Given “invoice 1042,” when two accessible invoices match, then clarification is required and neither is selected.
- Given a tool the actor cannot use, then it is absent from the planner projection and still denied if fabricated.
- Given a target or manifest changes after planning, then the bundle hash becomes stale before execution.

### 7. Verification

- Add manifest redaction, entity resolution, ambiguity, freshness, version/hash, ranking, tenant, and authorization tests.
- Add tests with hostile record text and oversized evidence.
- Verify manifest projection and execution resolve the same effective authority.

### 8. Definition of done

The planner receives a minimal, source-backed, current view of Finance tools and targets with no hidden privilege or cross-tenant leakage.

---

## Prompt 3 — Conversational execution for read and recommendation plans

### 1. Title and outcome

Allow Laura to answer natural-language Finance requests by executing validated read and recommendation plans and returning one evidence-backed result.

### 2. Current context

- “Ask Laura” performs grounded question answering but does not automatically invoke Finance tools.
- Specialized Finance analysis endpoints support cash, payables, receivables, accounting treatment, close, and operating cadence.
- Tool execution and structured results already exist for explicit invocations.

### 3. Dependencies

Prompts 1–2.

### 4. Implementation requirements

- Add an orchestration path that plans, executes, and synthesizes read/recommend requests through the existing trusted executor.
- Execute steps in dependency order; validate every output schema and expose failures, missing evidence, truncation, freshness, and partial completion explicitly.
- Permit bounded re-planning only when a tool result changes target resolution or supplies a declared dependency; retain all plan revisions and cap attempts.
- Synthesize the final answer through shared reasoning using only validated tool results and retained sources; preserve facts, inferences, unknowns, dates, currencies, and source links.
- Support cancellation, timeout, duplicate request idempotency, correlation, and safe retry of read-only transient failures.
- Integrate the six existing Finance analysis capabilities as callable planner capabilities or trusted adapters rather than duplicating their calculations.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- This prompt does not execute Finance mutations.
- A partial failure cannot be summarized as full success, and unsupported questions cannot be answered from general model knowledge.

### 6. Acceptance criteria

- Given “What should I pay this week while retaining 100,000 SEK?”, when evidence is current, then Laura calls the bounded payables/cash path and cites the exact source records and assumptions.
- Given stale or missing evidence, then the response identifies it and lowers confidence or requires review.
- Given one dependent read fails, then completed evidence remains visible but the request is not represented as complete.
- Given duplicate delivery, then one logical conversational run and one set of read attempts remain.

### 7. Verification

- Add fixed-input end-to-end tests for cash, overdue customers, P&L, categorization recommendation, accounting treatment, and close blockers.
- Add partial failure, invalid output, timeout, retry, cancellation, duplicate, unsupported-query, and cross-tenant tests.
- Measure latency and tool/model-call counts against documented development budgets.

### 8. Definition of done

Laura can answer supported Finance questions through real tools instead of generic chat, with explicit evidence and failure boundaries.

---

## Prompt 4 — Mutation preview, confirmation, and approval handoff

### 1. Title and outcome

Extend conversational planning to Finance execute requests through an exact effect preview and P0 authorization/approval path without allowing chat to bypass review.

### 2. Current context

- P0 enforces actor permissions, tool risk, approval, exact-action binding, and stale-state revalidation.
- Existing execute tools include transaction categorization, invoice status changes, paid-bill expense posting, and accounting-migration commands.
- The Agents workspace can commit reviewed payment recommendations through a specialized endpoint.

### 3. Dependencies

Prompts 1–3 and all P0 prompts.

### 4. Implementation requirements

- Add deterministic execution previews showing target, current version/state, proposed change, reversible/irreversible classification, risk tier, policy outcome, required permission, approval path, and evidence age.
- Require an explicit user confirmation token for policy-permitted confirmation actions; bind it to plan/step, actor, normalized payload hash, authority/policy/target versions, and short expiry.
- Route approval-required steps into the existing approval workflow and resume only through P0 continuation logic.
- Distinguish “create proposal/draft” from “execute external or accounting action” in language, UI, contracts, and audit.
- After execution, re-read authoritative state and report the actual result; never infer success from queueing or provider acknowledgement alone.
- Provide safe conflict refresh when state changes between preview and confirmation.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- The model cannot generate confirmation or approval tokens, choose an approver, approve its own request, or alter risk classification.
- Payment initiation, final period lock/year-end, statutory filing, credentials, and qualified approval remain outside conversational direct execution.

### 6. Acceptance criteria

- Given a valid low-risk preview, when the same authorized user confirms before expiry, then the exact step may execute once.
- Given changed target/policy/authority state, when confirmation or approval resumes, then execution is blocked as stale.
- Given approval is required, when the user confirms, then an approval request is created but no Finance mutation occurs.
- Given an external action is queued, then the response says queued/pending and later reconciles the real outcome.

### 7. Verification

- Add preview/hash/token, expiry, replay, actor mismatch, stale state, self-approval, approval continuation, outbox, and reconciliation tests.
- Add language tests ensuring proposal, approval, queued, executed, failed, and ambiguous states are not conflated.
- Run P0 adversarial authorization proof after implementation.

### 8. Definition of done

Natural-language execute requests enter an exact supervised workflow and cannot acquire more authority than the equivalent direct Finance action.

---

## Prompt 5 — Durable multi-step Finance conversation runs

### 1. Title and outcome

Persist multi-step conversational Finance work so it survives approval waits, worker restarts, retries, and later user follow-up without losing evidence or repeating effects.

### 2. Current context

- Agent runs, tool attempts, tasks, workflows, approvals, audit, and background/outbox infrastructure already exist.
- Single-agent orchestration can process explicit tool lists but does not provide a complete durable conversational plan/step lifecycle.

### 3. Dependencies

Prompts 1–4.

### 4. Implementation requirements

- Persist a bounded conversational run and step lifecycle: planned, awaiting clarification, ready, executing, awaiting confirmation, awaiting approval, queued, reconciling, completed, partially completed, cancelled, stale, and failed.
- Retain plan revisions, tool versions, normalized argument/result summaries, evidence references, attempts, leases, approvals, confirmations, policy decisions, and final outcome.
- Implement leased background continuation for steps that wait or run long; use bounded retries and stable business idempotency.
- Resume only dependent steps whose prerequisites completed successfully and current authority/evidence remain valid.
- Support user cancellation and safe supersession; cancellation cannot claim to undo an already completed external effect.
- Add retention/redaction rules and operational queries without storing hidden reasoning, secrets, or unrestricted payloads.

### 5. Constraints and preservation rules

- Follow the Shared execution contract and database rules in `docs/architecture-rules.md`.
- Chat transcript is presentation history, not workflow authority.
- No unbounded agent loop or recursive self-scheduling is allowed.

### 6. Acceptance criteria

- Given a run awaits approval, when the host restarts, then it resumes from durable state without duplicate execution.
- Given one branch fails, then unrelated completed steps remain traceable and dependent steps stay blocked.
- Given cancellation before an execute step, then no later worker may execute it.
- Given retention/redaction, then audit linkage remains useful without retaining sensitive raw content.

### 7. Verification

- Add migration, lifecycle, lease, restart, duplicate worker, cancellation, supersession, partial completion, retention, and tenant tests.
- Add SQL Server concurrency and rollback tests.
- Verify object/outbox recovery where a plan produces stored artifacts or external work.

### 8. Definition of done

Conversational Finance work is a recoverable governed workflow rather than ephemeral chat or best-effort in-process execution.

---

## Prompt 6 — Finance agent conversation and supervision workspace

### 1. Title and outcome

Create a professional Finance agent interaction surface where users can ask, clarify, preview, approve, monitor, cancel, and inspect natural-language tool runs.

### 2. Current context

- The Agents page contains grounded questions, Finance analyses, plans, governance, and capability state.
- Finance pages expose insight panels, “Message Laura” entry points, approvals, audit, and tool-execution transparency.
- Natural-language runs need a coherent user flow rather than additional disconnected controls.

### 3. Dependencies

Prompts 1–5.

### 4. Implementation requirements

- Apply the mandatory screenshot-first workflow in `docs/design.md` and retain the prompt/reference image before implementing the material workspace change.
- Add a conversational Finance workbench with request input, supported example intents, clarification prompts, plan preview, step progress, confirmation/approval cards, evidence/source drill-down, cancellation, conflict refresh, and final outcome.
- Deep-link from relevant Finance records while preserving company context and preselecting only authorized visible target references.
- Show facts, assumptions, unknowns, stale evidence, requested versus actual effects, tool/action mode, and human checkpoints in plain language.
- Provide EN/SV localization, responsive/narrow layout, keyboard flow, screen-reader announcements for asynchronous state, accessible tables/cards, and local money/date/time formatting.
- Keep administrative manifest/debug payloads in restricted transparency surfaces; daily users see safe summaries and actionable remediation.

### 5. Constraints and preservation rules

- Follow the Shared execution contract and `docs/design.md`.
- UI never builds tool arguments, grants authority, or marks work successful independently.
- Do not expose hidden prompts, chain-of-thought, credentials, secret references, or raw provider payloads.

### 6. Acceptance criteria

- Given a supported read request, when completed, then the user can trace the answer to source records and inspect partial/missing evidence.
- Given clarification is required, then no tool runs until the user resolves the ambiguity.
- Given confirmation or approval is required, then the exact proposed effect and current state are visible before proceeding.
- Given narrow viewport or keyboard-only use, then the complete supervision flow remains operable.

### 7. Verification

- Add component, typed-client, contract, localization, state-transition, and accessibility tests.
- Perform authenticated EN/SV desktop/narrow browser UAT across read, clarification, confirmation, approval, stale, partial-failure, cancellation, and recovery flows.
- Compare against the retained reference and capture evidence.

### 8. Definition of done

Users can supervise natural-language Finance work from request through evidence-backed outcome without navigating hidden technical surfaces.

---

## Prompt 7 — Natural-language safety evaluation and P1 release gate

### 1. Title and outcome

Complete P1 with reproducible quality, safety, cost, latency, authorization, and recovery evidence for supported natural-language Finance workflows.

### 2. Current context

- P1 adds nondeterministic planning/synthesis over deterministic manifests and execution.
- The repository has AI quality events, hermetic tests, fake-provider patterns, Finance capacity evidence, and release-manifest conventions.

### 3. Dependencies

Prompts 1–6.

### 4. Implementation requirements

- Create a versioned fixed-input evaluation pack covering supported intents, ambiguity, unsupported requests, prompt injection, conflicting evidence, stale data, mixed currency/periods, large result sets, mutation requests, and malicious tool outputs.
- Assert invariants rather than exact prose: permitted tools only, grounded targets, valid schemas, correct action class, no mutation before checkpoints, accurate completion state, complete source linkage, and bounded calls/cost/time.
- Add quality recording for plan validity, tool selection, correction/clarification, user acceptance/rejection, policy interception, failure class, latency, model/tool calls, and estimated cost.
- Add safe degradation for unavailable/rate-limited/malformed AI providers; deterministic Finance services remain usable and no fake answer is substituted.
- Document supported language intents and explicit non-capabilities.
- Produce a revision-bound P1 release manifest with test commands, model/config version, checksums, counts, UAT evidence, and unresolved blockers.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Do not include production customer data in evaluation fixtures or logs.
- A model-quality score cannot override a failed authorization, approval, tenant, or accounting invariant.

### 6. Acceptance criteria

- Given the fixed evaluation pack, when run repeatedly against deterministic/fake providers, then all safety invariants pass.
- Given provider failure, then the user receives an actionable unavailable/partial state and no unsupported Finance conclusion.
- Given a mutation phrased deceptively as a read, then the action is still classified and gated as execute.
- Given P1 release verification, then all P0 gates remain green.

### 7. Verification

- Run focused planner/orchestration/UI/evaluation suites, full Release build, hermetic matrix, and applicable SQL lanes.
- Run bounded opt-in live-model evaluation only when credentials/network are explicitly available; never make it the sole safety proof.
- Capture authenticated EN/SV browser evidence and recovery/restart evidence.

### 8. Definition of done

P1 is complete only when supported natural-language requests are safe, grounded, bounded, recoverable, usable, and independently reproducible. `finance-update-p2-prompts.md` may then begin.
