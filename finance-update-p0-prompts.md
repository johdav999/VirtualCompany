# Finance Update P0 Implementation Prompts

Priority: P0 — Finance agent permission consistency and approval enforcement  
Prompt order: execute Prompts 1–6 in order. This package must be complete before `finance-update-p1-prompts.md` begins.

## Shared execution contract

Every prompt is a production implementation task, not an analysis or scaffolding task.

- Read and follow `production-implementation.md`, `docs/architecture-rules.md`, repository `AGENTS.md` files, and the current implementation before editing. UI work also follows `docs/design.md` and `ui-instructions.md`.
- Preserve the modular monolith, shared agent orchestration, authoritative Finance policies, immutable accounting records, tenant isolation, typed API clients, audit evidence, and current routes unless a prompt explicitly changes a contract.
- Treat the actor, company, agent, tool, action class, data scope, target record, policy version, and approval as independent authorization inputs. A company ID, agent identity, UI-hidden control, model request, or generic membership is never sufficient authority for a Finance mutation.
- Agent tools remain versioned, schema-validated, company-scoped, and classified as `read`, `recommend`, or `execute`. Finance calculations, eligibility, posting, locking, approval, and provider outcomes remain deterministic backend decisions.
- Database changes, if required, use additive SQL Server EF migrations and include migration discovery, representative upgrade, rollback/recovery documentation, and `dotnet ef migrations has-pending-model-changes` verification.
- Sensitive or external actions follow the Workflow and Approval and External Side Effects and Outbox sections of `docs/architecture-rules.md`; approval is rechecked immediately before execution.
- Do not weaken, delete, skip, or rename valid tests to obtain a green result. Record unrelated baseline failures explicitly and leave the affected package with focused tests and the full hermetic matrix passing.
- Finish every prompt with production behavior, tests, documentation, observability, and no deferred in-scope TODOs.

---

## Prompt 1 — Authoritative actor-to-finance-tool authorization

### 1. Title and outcome

Implement one backend authorization policy for Finance agent tools so the same human or delegated actor receives the same decision through the UI, agent API, orchestration worker, and approved continuation path.

### 2. Current context

- `AgentsController` accepts tool execution under the broad `CompanyMember` policy.
- `FinanceAgentAnalysisController` uses `FinanceView`, and payment-run commitment additionally uses `FinanceEdit`.
- `CompanyAgentToolExecutionService`, `PolicyGuardrailEngine`, `StaticCompanyToolRegistry`, company membership resolution, Finance access resolution, and company authorization policies already exist.
- The current execution decision primarily evaluates agent policy; it does not establish an equivalent actor Finance permission for every tool action.

### 3. Dependencies

None.

### 4. Implementation requirements

- Define an Application-level Finance agent authorization decision containing actor identity/type, membership state, tool/action/scope, required company policy or Finance permission, outcome, stable reason code, explanation, and evidence.
- Map `read` and `recommend` Finance tools to at least `FinanceView`; map `execute` tools to `FinanceEdit` plus the narrow owning permission such as approval, accounting posting, close administration, or integration administration.
- Enforce the decision inside the tool-execution application/service boundary before guardrail evaluation or provider dispatch. Controller attributes remain defense in depth, not the sole boundary.
- Support trusted background execution only through a persisted delegation/authority context bound to company, agent, capability, action classes, expiry, issuer, and originating workflow. Never impersonate a user or infer authority from agent autonomy.
- Return safe, structured denials without disclosing target existence across tenant or permission boundaries.
- Persist audit evidence for actor authorization decisions and correlate it with the tool execution attempt.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Do not duplicate Finance access rules in controllers, Blazor, prompts, or individual tool providers.
- Reads must remain reads; authorization evaluation must not create approval requests or mutate Finance state.

### 6. Acceptance criteria

- Given an ordinary company member without `FinanceView`, when any Finance tool is requested, then it is denied before Finance data is read.
- Given `FinanceView` without `FinanceEdit`, when a read or recommendation is requested, then it may proceed, but an execute request is denied.
- Given an authorized background workflow with an expired or mismatched delegation, when execution starts, then it is denied without falling back to agent identity.
- Given another company's target ID, when authorization fails, then the response does not reveal whether the target exists.

### 7. Verification

- Add unit tests for the permission mapping and structured decision.
- Add API integration tests for owner/admin/finance approver/finance editor/ordinary member, every action class, background delegation, missing actor, expired delegation, and cross-company access.
- Run focused agent execution, Finance access, audit, and authorization suites.

### 8. Definition of done

Every Finance agent tool invocation has an authoritative actor authorization decision that is identical across interactive and background entry points.

---

## Prompt 2 — One effective capability and permission projection

### 1. Title and outcome

Unify configured, displayed, planned, and executed Finance agent capabilities so Laura cannot be shown one authority while the runtime silently grants or denies another.

### 2. Current context

- Laura's seeded profile contains a bounded initial Finance tool list.
- `CompanyAgentToolExecutionService` dynamically grants Laura every registered Finance-scoped definition.
- `AgentCapabilityCatalog` reads persisted agent configuration directly, while execution resolves an effective runtime profile.
- The Agents UI displays capability summaries and workflow cards derived from profile data.

### 3. Dependencies

Prompt 1.

### 4. Implementation requirements

- Introduce one effective agent authority projection/resolver used by the capability catalogue, natural-language planner inputs, execution service, schedule validation, profile API, and Web presentation.
- Preserve configured grants separately from registry-derived compatibility grants and expose the source, version, and reason for each effective tool/action/scope.
- Make registry changes fail closed: a newly registered Finance tool is not automatically executable by an existing agent unless an explicit versioned role policy grants it.
- Provide deterministic states for available, approval required, permission denied, configuration required, integration unavailable, and not implemented.
- Add an effective-authority hash/version used by plans, approvals, and execution to detect stale permission changes.
- Update profile/capability APIs and typed clients without exposing hidden prompts, secrets, or unrelated tools.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Do not remove backward compatibility by silently discarding existing Laura grants; migrate or version them explicitly.
- Capability display is explanatory only. Execution always re-evaluates current authority.

### 6. Acceptance criteria

- Given the same agent and company state, when capability display, planning, and execution resolve authority, then they return the same tool/action/scope set and authority version.
- Given a new sensitive Finance tool registration, when Laura has no explicit role-policy grant, then it is unavailable rather than dynamically executable.
- Given a permission change after a tool plan was created, when execution is attempted, then the stale authority hash blocks execution and requires refresh.

### 7. Verification

- Add resolver, compatibility migration, capability-state, registry-change, hash-stability, and stale-authority tests.
- Add contract tests comparing capability API output with execution decisions.
- Run agent roster/profile, capability, tool manifest, and Finance presentation regressions.

### 8. Definition of done

The product has one explainable effective authority model from configuration through execution, with no hidden dynamic privilege expansion.

---

## Prompt 3 — Enforced sensitivity and approval policy

### 1. Title and outcome

Make Laura's approval configuration authoritative so every sensitive Finance action is identified by backend policy and cannot depend on model- or caller-supplied sensitivity flags.

### 2. Current context

- Laura declares `requireApprovalForExecute` and a workflow `requiresApproval` list.
- Migration execute tools are registry-sensitive and approval-backed.
- `categorize_transaction`, `approve_invoice`, and `post_paid_supplier_bill_expense` are execute tools but are not all intrinsically registry-sensitive.
- `PolicyGuardrailEngine` evaluates autonomy, registry sensitivity, request sensitivity, thresholds, and escalation rules.

### 3. Dependencies

Prompts 1–2.

### 4. Implementation requirements

- Define a versioned Finance tool risk classification containing risk tier, reversibility, required actor permission, default approval behavior, threshold category, segregation requirement, and external-side-effect classification.
- Make the registry and backend Finance policy—not the request payload or model—the source of truth for sensitivity.
- Consume the effective agent/company approval policy directly during guardrail evaluation, including `requireApprovalForExecute` and explicit tool/action rules.
- Classify invoice approval, accounting posting, close/lock, provider writes, payment actions, compliance submission, year-end, and migration execution as sensitive by default.
- Allow low-risk internal categorization to bypass approval only through an explicit company policy with bounded amount/count, reversible state, allowed categories, and audit; default it to review when configuration is absent.
- Persist the risk-policy version and complete threshold evaluation on every attempt and approval request.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Approval requirements are additive to Finance domain eligibility; approval never makes an otherwise ineligible action valid.
- Do not accept `sensitiveAction=false` as an override of registry or policy classification.

### 6. Acceptance criteria

- Given a caller marks a sensitive action non-sensitive, when policy evaluates it, then the authoritative classification still requires approval.
- Given `requireApprovalForExecute=true`, when any execute tool is requested, then an approval is created and no Finance mutation occurs.
- Given an explicitly configured reversible categorization exception within its limits, when executed, then it is audited with the exact policy version; outside the limit it requires approval.

### 7. Verification

- Add exhaustive tool-risk classification and registry coverage tests so every execute tool has one explicit policy.
- Add tests for request tampering, missing configuration, threshold boundaries, batch counts, absent routes, and policy-version changes.
- Run migration-agent, Finance tool-flow, policy-guardrail, and approval suites.

### 8. Definition of done

No Finance action becomes less sensitive because of a model, client, route, or missing configuration, and Laura's declared approval policy is enforced in code.

---

## Prompt 4 — Secure approval continuation and stale-state revalidation

### 1. Title and outcome

Bind approvals to the exact Finance action and safely resume approved executions only after current authority, evidence, and business state are revalidated.

### 2. Current context

- Tool attempts, approval requests, decision chains, correlation IDs, workflow links, versions, and audit events already exist.
- Approved Finance executions can resume through shared approval processing.
- Finance records and provider state can change between request and approval.

### 3. Dependencies

Prompts 1–3.

### 4. Implementation requirements

- Bind an approval to company, actor, agent, tool/version, action/scope, normalized payload hash, target IDs/versions, authority hash, policy/risk version, threshold evaluation, and expiry.
- Enforce segregation of duties and prohibit the requesting agent or initiating user from satisfying approval roles where policy requires independence.
- On approval continuation, re-resolve actor/delegation authority, effective agent authority, target versions, Finance eligibility, policy thresholds, integration state, and approval validity before any mutation.
- Mark stale, superseded, rejected, cancelled, expired, and revoked approvals explicitly; never silently create a replacement approval.
- Use stable business idempotency keys and preserve one durable attempt/continuation history across retries.
- Route external side effects through existing outbox/reconciliation boundaries and preserve ambiguous outcome handling.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Approval payloads must not contain credentials, unrestricted documents, or raw provider data.
- A stale approval cannot be edited into validity; a new reviewed request is required.

### 6. Acceptance criteria

- Given a target or policy changes after approval request, when approval continuation runs, then it stops as stale with no mutation.
- Given the requester attempts self-approval where independent review is required, then the decision is rejected.
- Given duplicate approval delivery or worker restart, then the approved action executes at most once and retains one audit chain.
- Given an ambiguous provider result, then the attempt enters reconciliation rather than success or blind retry.

### 7. Verification

- Add concurrency tests for state changes, approval races, duplicate decisions, expiry, revocation, worker restart, and idempotent continuation.
- Add SQL Server transaction and outbox/reconciliation tests for sensitive Finance actions.
- Verify complete audit linkage from originating request through final outcome.

### 8. Definition of done

Approval authorizes one exact current action, not a general future mutation, and execution remains fail-closed across races and restarts.

---

## Prompt 5 — Permission and approval transparency in the agent workspace

### 1. Title and outcome

Give users a clear, localized view of what Laura may do, why an action is unavailable, and what approval or configuration is required before it can proceed.

### 2. Current context

- The Agents workspace displays capability states, Finance analysis, plans, handoffs, memory, and links to Finance workflows.
- Finance transparency pages display tool manifests and executions for administrators.
- Effective permissions, actor requirements, risk tiers, and approval behavior are not presented as one coherent user contract.

### 3. Dependencies

Prompts 1–4.

### 4. Implementation requirements

- Apply the mandatory screenshot-first workflow in `docs/design.md`; create and retain a reference for the materially redesigned authority/approval surface before implementation.
- Add a role-appropriate capability panel showing read/recommend/execute mode, actor permission, approval requirement, integration/configuration state, and plain-language denial reason from authoritative APIs.
- Add execution preview and approval-state panels with target summary, requested effect, evidence age, risk tier, approver requirement, expiry, and links to the originating record, approval, audit, and tool execution.
- Keep system-administration diagnostics separated from daily Finance work while providing safe operator links for authorized users.
- Add English and Swedish resources, keyboard/focus behavior, screen-reader labels, narrow-layout support, loading/empty/error/stale states, and local date/time/money formatting.
- Never expose policy internals, hidden prompts, secret references, raw payloads, or records the user cannot view.

### 5. Constraints and preservation rules

- Follow the Shared execution contract and `docs/design.md`.
- UI controls consume allowed actions; they never recreate authorization or approval policy.
- A disabled or hidden button is not a security boundary.

### 6. Acceptance criteria

- Given a user lacks permission, when the capability is displayed, then the unavailable state and safe remediation are clear without exposing restricted record details.
- Given approval is required, when a request is created, then the user can trace its exact action, evidence age, approver, expiry, and current state.
- Given the permission or target version changes, when the page refreshes, then stale previews cannot be submitted.

### 7. Verification

- Add component and contract tests for every capability and approval state.
- Perform authenticated EN/SV desktop and narrow browser checks, keyboard-only navigation, screen-reader semantics, contrast, and screenshot comparison against the reference.
- Run localization quality gates and Web/API contract suites.

### 8. Definition of done

Users can understand and supervise Laura's authority without relying on hidden implementation knowledge or misleading controls.

---

## Prompt 6 — Adversarial authorization proof and P0 release gate

### 1. Title and outcome

Complete P0 with a repeatable adversarial proof that Finance agent authorization, approval, tenant isolation, and audit behavior fail closed across every entry point.

### 2. Current context

- The repository has hermetic API/Web matrices, SQL Server lanes, Finance tool-flow tests, approval tests, audit queries, and production-readiness evidence patterns.
- P0 changes cross HTTP, background execution, policy, approval, Finance commands, and UI presentation.

### 3. Dependencies

Prompts 1–5.

### 4. Implementation requirements

- Add a maintained Finance agent authority matrix enumerating every registered tool, action, required actor permission, agent grant, risk tier, approval behavior, external side effect, and owning regression test.
- Add parameterized tests that fail when a tool lacks authorization or risk metadata.
- Add adversarial cases for forged company IDs, target IDs, roles, action classes, scopes, sensitivity flags, thresholds, approval IDs, payload hashes, authority versions, delegation tokens, and idempotency keys.
- Add fault tests proving no partial mutation on authorization, approval, audit, transaction, outbox, or continuation failure.
- Add safe metrics for allow/deny/approval/stale outcomes by tool and reason code without recording sensitive payload content.
- Document operator diagnosis, permission changes, approval recovery, and emergency restriction procedures.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Evidence must be generated from current commands and tied to a repository revision; do not hand-author green results.
- Do not average failed security checkpoints into a release percentage.

### 6. Acceptance criteria

- Given any registered Finance execute tool, when the matrix is inspected, then one tested actor permission and risk/approval rule exists.
- Given every unauthorized and tampered request class, when executed, then no Finance/provider mutation occurs and a safe auditable denial remains.
- Given the complete P0 verification run, then focused tests, full build, hermetic matrix, SQL lanes, and localization gates are green.

### 7. Verification

- Run focused unit/integration/contract/UI suites introduced by P0.
- Run the full Release build and hermetic matrix.
- Run applicable SQL Server migration, concurrency, rollback, and no-pending-model checks.
- Capture the command, revision, counts, failures, and manifest checksum in a P0 release evidence document.

### 8. Definition of done

P0 is complete only when Finance agent authority is consistent, adversarially tested, operationally explainable, and release evidence is green. `finance-update-p1-prompts.md` may then begin.
