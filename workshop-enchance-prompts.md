# Workshop enhancement prompt pack: asynchronous research conversations

## Purpose and delivery order

Implement text-workshop research as a durable asynchronous workflow. A workshop must acknowledge a research request immediately and remain usable while research runs. When the research completes, the agent must add a separate, cited follow-up and proposed draft changes without requiring the user to reload the page.

Run these prompts in order. Each prompt is independently deployable only after its stated prerequisites are complete. Read and follow `production-implementation.md`, `AGENTS.md`, and `/docs/architecture-rules.md` for every prompt. Also follow `architecture-inst.md` if it is supplied in the execution environment; it is not currently present at the repository root. UI work must additionally follow `ui-instructions.md` and `/docs/design.md`.

The current implementation to preserve and evolve is:

- `GuidedWorkSessionService.AddTurnAsync` performs the first checkpoint, runs `IGuidedEvidenceResearchService` synchronously, then performs a second checkpoint before returning the turn.
- `OpenAiGuidedCheckpointProvider` emits `research_query` for permitted text research.
- `CompanyOutboxEnqueuer` and `CompanyOutboxProcessor` already provide durable, leased, idempotent, retried background dispatch.
- Guided sessions, fields, messages, and idempotency records are persisted in `GuidedWorkSessionEntities.cs` and `GuidedWorkSessionConfiguration.cs`.
- `GuidedWorkSession.razor` disables the text composer while `TurnAsync` is active; `GuidedConversationPanel.razor` renders persisted session messages.
- Existing regression coverage belongs primarily in `tests/VirtualCompany.Api.Tests/GuidedWorkSessionIntegrationTests.cs` and provider prompt tests.

Do not use `Task.Run`, detached in-memory jobs, client-only state, or a second application stack. Do not write unreviewed research directly to a business artifact.

---

## Prompt 1 — Define the durable research-work item and outbox contract

### Title and outcome

Create a tenant-scoped, durable contract for a workshop research request and route it through the existing company outbox. The outcome is a restart-safe, idempotent work item that can run outside the HTTP request.

### Current context

`GuidedWorkSessionService` currently stores completed idempotent operations in `GuidedSessionOperation` and invokes research inline. `CompanyOutboxEnqueuer` and `CompanyOutboxProcessor` already implement durable dispatch, leasing, bounded retries, background execution records, and operator-visible terminal failures. Guided data is owned by Operations and persisted through the shared EF Core model.

### Dependencies

None.

### Implementation requirements

- Add an application-level message/contract for a requested guided-work research continuation. Include only the identifiers and bounded context needed to safely reconstruct work: company ID, session ID, agent ID, originating user-message ID, client request ID, artifact type/schema version, bounded query, correlation ID, and stable idempotency key.
- Add a specific `CompanyOutboxTopics` topic for this work. Register its dispatch path in the existing Operations-owned outbox processor; do not add an independent polling system.
- Persist enough explicit state to distinguish queued, completed, failed, and superseded research requests for a session. Prefer a dedicated tenant-owned guided-work entity when the existing immutable `GuidedSessionOperation` idempotency record cannot represent lifecycle and retry state safely.
- If a new entity/table is required, add its EF Core configuration, DbSet, SQL Server migration, and updated model snapshot. Preserve local SQL Server and Docker restore/run compatibility.
- Enqueue within the same transaction as the immediate workshop acknowledgement and use a stable idempotency key based on company, session, originating user message/client request, and query—not a random retry identifier.
- Add audit events for queued, completed, unavailable, retrying, and terminally failed research. Audit metadata must be safe: identifiers, artifact type, source count, and failure code only; never research content, credentials, or provider tokens.
- Add structured logging and telemetry for queue latency, research duration, retry outcome, and terminal failure, scoped to company/session/correlation identifiers.

### Constraints and preservation rules

- Follow the Operations module boundary. Do not add capability logic to the Infrastructure facade or API controller.
- Enforce company scope in every lookup, enqueue, dispatch, and audit operation. Background execution must establish the existing company execution scope.
- The outbox worker may retry transient provider failures with the established bounded retry policy. Validation, authorization, cancelled/completed sessions, and unsupported research are permanent/no-op outcomes with a plain-English recorded reason.
- Research remains read-only. Evidence-derived draft changes remain proposed and retain source title/URL metadata.

### Acceptance criteria

- Given a text workshop asks for permitted research, when the acknowledgement transaction commits, then exactly one durable outbox item and one durable research-work item exist for that request.
- Given a duplicate client request, when the endpoint is retried, then it returns the original acknowledgement and does not enqueue a second research job.
- Given a process restart after acknowledgement, when the outbox dispatcher resumes, then the research job remains eligible for dispatch.
- Given two dispatchers claim the same candidate, when leasing occurs, then at most one performs the research continuation.

### Verification

- Add domain/configuration tests for required company ownership, keys, indexes, and cascade paths.
- Add migration/model validation using the repository’s normal SQL Server migration command and verify Docker/local compatibility.
- Add focused outbox/idempotency integration tests, including duplicate request and worker-restart recovery.
- Build the affected API, Operations, Persistence, migration, and test projects.

### Definition of done

The work item is production-ready, migration-backed, tenant-scoped, auditable, idempotent, and dispatched through the existing durable outbox with no in-memory-only execution path or deferred in-scope TODO.

---

## Prompt 2 — Return an immediate workshop acknowledgement and complete research in the background

### Title and outcome

Split a research-requested text turn into an immediate conversational acknowledgement and a later durable research continuation. The user receives a useful agent response and can continue the workshop without waiting for external research.

### Current context

`GuidedWorkSessionService.AddTurnAsync` currently calls the checkpoint provider, awaits research, and calls the provider again with `BuildPublicResearchContext`. `OpenAiGuidedCheckpointProvider.CheckpointInstructions` currently tells the model to set `research_query` and waits for a second checkpoint. Guided turns already have user/agent messages, optimistic-version handling, audit, and idempotency records.

### Dependencies

Prompt 1 is complete and deployed, including durable research dispatch.

### Implementation requirements

- Change the initial text research-request path to persist and return the first checkpoint immediately. Its agent message must acknowledge the request in plain English, state what it will research without promising a result, and ask one useful next workshop question.
- Persist the user message, acknowledgement agent message, session summary/next question, and queued research item atomically. Do not apply evidence findings or evidence-derived field patches in this first response.
- Permit the text composer for subsequent turns once that acknowledgement response returns. Preserve expected-version/idempotency behavior for concurrent turns.
- Implement the outbox handler/continuation service that executes permitted research, rebuilds safe current session context, calls the final checkpoint with `BuildPublicResearchContext`, validates/normalizes it, and writes a separate agent follow-up message plus proposed draft changes.
- The completion handler must re-read the session and fields under company scope. It must not overwrite fields changed after the research was requested; reuse or extend the existing field-version/rebase safeguards and retain workshop insights safely.
- If research is unavailable, write a separate plain-English follow-up explaining that this specific search could not be completed. Do not substitute model knowledge, invent sources, or silently mark the job successful.
- If the session is cancelled/completed, the originating message is no longer valid, or the final checkpoint is invalid, safely stop without mutating the draft and record an operator-visible/auditable reason.
- Update the provider instructions so the initial `research_query` checkpoint produces an acknowledgement and next question, and the research-result checkpoint uses only supplied research context and citations.

### Constraints and preservation rules

- Preserve all existing authorization, session ownership, artifact eligibility, review/commit, source-provenance, and tenant-isolation guarantees.
- Never call external research from the HTTP request after this change.
- The follow-up must be idempotent: duplicate outbox delivery cannot create duplicate agent messages, duplicate draft patches, or duplicate audit events.
- Do not confirm evidence-derived information automatically. It must be `proposed` and traceable to bounded titles/URLs supplied by the research service.
- Use plain English. Do not expose outbox, retry, checkpoint, or provider terminology in workshop messages.

### Acceptance criteria

- Given a permitted research request, when the first checkpoint requests research, then the turn API returns an acknowledgement with a next question without waiting for the research provider.
- Given the acknowledgement is returned, when the user sends another workshop message, then it is accepted and persisted while research remains pending.
- Given research completes, when the worker processes the item, then one later agent message appears with cited, proposed draft updates and no duplicate updates on retry.
- Given research fails permanently, when the worker reaches a terminal outcome, then the workshop displays one clear follow-up and contains no invented findings or evidence patches.
- Given a user edits a field after the request, when the research continuation completes, then the newer edit is preserved.

### Verification

- Extend `GuidedWorkSessionIntegrationTests` with immediate-return, continued-turn, completion, duplicate-delivery, cancellation, cross-company, and rebased-field scenarios.
- Extend `OpenAiGuidedCheckpointProviderTests` for acknowledgement and research-result prompt constraints.
- Verify audit records, source metadata, and idempotency records directly from the database in integration tests.
- Run focused API tests and the relevant architecture/dependency tests.

### Definition of done

Research is no longer a request-thread blocker. The workshop remains a coherent conversation, all state transitions are durable and replay-safe, and error paths remain visible and safe.

---

## Prompt 3 — Make the workshop surface live while research continues

### Title and outcome

Update the guided-work UI so users can see a calm, clear research-in-progress state, keep contributing to the workshop, and receive the background follow-up automatically.

### Current context

`GuidedWorkSession.razor` owns text submission and currently sets `busy` around `Api.TurnAsync`; its textarea and send button are disabled while busy. `GuidedConversationPanel.razor` renders persisted messages and already has a reusable work-indicator treatment for voice. The typed API client can fetch the latest session with `GetAsync`.

### Dependencies

Prompt 2 is complete and exposes sufficient session/message state to identify pending research without leaking technical details.

### Implementation requirements

- Add a user-facing, plain-English pending-research presentation to the existing conversation panel. Reuse the current work-indicator visual language; do not create a new visual style.
- After the acknowledgement turn returns, clear `busy`, preserve the user’s ability to send another text message, and show that the agent is checking the requested public information.
- Add bounded polling while the page is active and the session has pending research. Poll the existing session endpoint at a conservative interval, stop on completion/failure/cancellation/navigation/disposal, avoid overlapping polls, and refresh only when the session version changes.
- When the completion follow-up arrives, render it naturally in the conversation, update draft fields, remove the in-progress indicator, and retain the user’s unsent text.
- Handle transient polling errors quietly and retry on the next interval; surface only a concise actionable message for sustained/unrecoverable access errors.
- Follow `ui-instructions.md` and `/docs/design.md`. Because this is a significant interaction change to an existing workshop surface, create and save a screenshot reference under `docs/design/references/` before implementation, then verify the implemented surface against it.
- Add localization keys for every new user-facing string in the existing GuidedWork resource files, including Swedish equivalents.

### Constraints and preservation rules

- Do not expose raw states such as `queued`, `outbox`, `checkpoint`, or provider names.
- Keep the existing keyboard, focus, accessibility, ARIA-live, mobile/responsive, voice, document-upload, review, and cancellation behavior intact.
- Do not use browser timers after component disposal and do not run duplicate polling loops after navigation/re-render.
- The UI must not decide whether research is allowed; it only presents backend state.

### Acceptance criteria

- Given the agent starts research, when the acknowledgement appears, then the user can send another text turn immediately and sees a plain-English in-progress indicator.
- Given a background completion is persisted, when the workshop is open, then the agent’s follow-up and draft changes appear without manual reload within the configured polling interval.
- Given the user navigates away or the session ends, when disposal occurs, then no further client polling occurs.
- Given research fails, when the failure follow-up is received, then the indicator ends and the UI explains the next step without technical jargon.

### Verification

- Add focused Web component/surface tests for pending display, enabled composer, polling lifecycle, completion refresh, and localization coverage.
- Run the existing GuidedWork web tests and localization quality gates.
- Perform the mandatory screenshot-first workflow and a browser check against the local workshop route using a safe test session.
- Build the Web project and verify no JS interop/disposal exceptions occur.

### Definition of done

The workshop remains responsive throughout research, clearly communicates progress, receives results automatically, and preserves all existing accessibility and localization standards.

---

## Prompt 4 — Operational hardening, observability, and end-to-end acceptance

### Title and outcome

Harden and validate the complete asynchronous research conversation from request through background completion, retries, failure, recovery, and UI presentation.

### Current context

The completed implementation uses the existing company outbox processor, guided-work service, evidence provider, session/message persistence, and workshop polling surface. Existing operations documentation is in `docs/guided-dialogue-operations.md`; existing evaluation corpus is `tests/VirtualCompany.Api.Tests/Fixtures/guided-dialogue-evaluation-corpus.json`.

### Dependencies

Prompts 1–3 are complete.

### Implementation requirements

- Update `docs/guided-dialogue-operations.md` with plain operational runbooks: pending work, retry behavior, terminal failure, cancellation/completion supersession, reconciliation/idempotency investigation, and safe log/audit lookup fields.
- Add telemetry dashboards/metrics or documented metric names for queue age, completion duration, provider availability, retry counts, terminal failures, duplicate suppression, and delayed follow-up delivery. Do not include user message content, research content, credentials, tokens, or raw provider payloads in telemetry.
- Add a bounded health/readiness check for the background research dispatch dependencies that reports actionable configuration/provider status without making external calls on every health probe.
- Extend the guided-dialogue evaluation corpus with research-request, continued-conversation, failed-research, stale-result, and duplicate-delivery cases. Ensure assertions require source attribution and proposed status for research-derived updates.
- Review and correct every authorization, tenant-isolation, idempotency, and cancellation edge case discovered by the end-to-end suite.

### Constraints and preservation rules

- Do not weaken the existing public-research policy: no fabricated research, no unapproved external side effects, no automatic confirmation of evidence, and no silent failure.
- Do not add production mock data or bypass the outbox worker to make tests pass.
- Preserve local SQL Server and Docker SQL Server compatibility and preserve existing API routes/wire contracts unless a versioned, documented extension is necessary.

### Acceptance criteria

- Given a provider timeout, when retries are exhausted, then the work is terminally visible to operators and the workshop receives exactly one safe follow-up.
- Given an outbox retry or lease recovery, when the final action runs again, then no duplicate user-visible messages or draft mutations are produced.
- Given a session is cancelled or committed while research is pending, when the job later runs, then it performs no draft mutation and records the superseded reason.
- Given two companies use workshops, when either queries or dispatches research, then no data, messages, citations, or updates cross company boundaries.

### Verification

- Run the full guided-work API integration suite, relevant Web tests, dependency/architecture tests, and migration validation.
- Execute targeted retry/lease-recovery and cross-tenant integration scenarios.
- Run formatting, static analysis, dependency vulnerability scan, and secret scan according to repository tooling; record pre-existing failures separately from new ones.
- Verify the deployment/rollback runbook by confirming a queued job survives an application restart and can be processed after rollback-compatible migration deployment.

### Definition of done

The asynchronous workshop research workflow is observable, recoverable, tenant-safe, fully tested by risk, and ready for production operation with no hidden synchronous fallback or unresolved failure state.
