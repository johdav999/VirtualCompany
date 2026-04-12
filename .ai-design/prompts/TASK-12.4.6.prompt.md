# Goal

Implement backlog task **TASK-12.4.6 — Reuse backend APIs; no mobile-specific business logic** for story **ST-604 Mobile companion for approvals, alerts, and quick chat**.

The coding agent should ensure the **.NET MAUI mobile app consumes the same backend application/API contracts and business workflows as the web app**, with **no mobile-only business rules, approval logic, task state transitions, or domain branching** introduced in the mobile project.

This task is primarily about **architecture enforcement and integration alignment**, not inventing new product behavior.

# Scope

In scope:

- Review current web/API/mobile implementation paths for:
  - sign-in and company selection
  - alerts/notifications
  - approvals and approval actions
  - daily briefing retrieval
  - direct agent chat / quick chat
  - quick company status / task follow-up summaries
- Ensure mobile uses existing backend APIs and shared contracts where available.
- Refactor any mobile code that currently embeds business decisions, workflow rules, approval logic, or data shaping that belongs in backend/application layers.
- Add or tighten shared DTO/service abstractions in API/Application/Shared as needed so mobile can consume backend behavior cleanly.
- Keep mobile logic limited to:
  - presentation
  - local state/view models
  - connectivity/retry UX
  - API invocation
  - auth/session handling
  - offline/intermittent connectivity handling that does not alter business semantics
- Add tests or guardrails that make the architectural intent explicit.

Out of scope unless required to remove duplicated logic:

- Building full new mobile features beyond what ST-604 already targets
- Creating mobile-only endpoints
- Adding mobile-specific domain models or persistence for business state
- Reworking unrelated web UX
- Introducing a separate BFF just for mobile unless already present and clearly necessary

# Files to touch

Prioritize inspection and likely edits in these areas:

- `src/VirtualCompany.Mobile/`
  - API clients
  - service layer
  - view models/pages related to approvals, alerts, chat, briefings, status
  - any local models duplicating backend business concepts
- `src/VirtualCompany.Api/`
  - controllers/endpoints currently used by web/mobile
  - endpoint contracts if mobile needs existing behavior exposed consistently
- `src/VirtualCompany.Application/`
  - commands/queries/handlers for approvals, notifications, chat, summaries
  - shared orchestration-facing application services
- `src/VirtualCompany.Shared/`
  - DTOs/contracts/enums shared across clients if this project is already used that way
- `src/VirtualCompany.Web/`
  - compare how web consumes backend behavior to align mobile usage
- `tests/VirtualCompany.Api.Tests/`
  - API tests covering approval actions, alerts, chat, briefing/status retrieval
- Potentially solution-wide docs/comments if there is an architecture note or README section describing mobile companion constraints

Do not touch archived migration docs unless absolutely necessary.

# Implementation plan

1. **Inspect current architecture and usage paths**
   - Identify how the mobile app currently performs:
     - approval list/detail/action
     - alerts/notifications retrieval
     - daily briefing retrieval
     - direct agent chat send/receive
     - company status/task follow-up summary retrieval
   - Compare with web implementation and backend application flows.
   - Find any mobile code that:
     - computes approval eligibility
     - maps business statuses with custom rules
     - performs domain validation beyond basic client validation
     - constructs business summaries that should come from backend
     - branches behavior by mobile-specific domain assumptions

2. **Define the architectural rule in code**
   - Treat backend as the single source of truth for:
     - approval state transitions
     - alert prioritization semantics
     - briefing/status summary composition
     - chat/task creation side effects
     - tenant/company scoping and authorization
   - Keep mobile limited to consuming backend responses and sending user intents.

3. **Refactor mobile to consume shared/backend contracts**
   - Replace any duplicated mobile business models with shared DTOs/contracts where appropriate.
   - Route mobile actions through existing API endpoints instead of local decision logic.
   - If mobile currently transforms raw data into business meaning, move that meaning to backend query/response models where feasible.
   - Preserve UI-only formatting in mobile, such as:
     - grouping for display
     - relative timestamps
     - loading/empty/error states

4. **Consolidate missing backend-facing contracts**
   - If mobile needs data not currently exposed cleanly, extend existing backend endpoints or application queries rather than adding mobile-only business services.
   - Prefer shared request/response contracts in `VirtualCompany.Shared` if that is the established pattern.
   - Ensure any new endpoint behavior is generic and reusable by both web and mobile.

5. **Remove mobile-specific business logic**
   - Eliminate code in `VirtualCompany.Mobile` that:
     - decides whether an approval can be approved/rejected based on business rules
     - infers workflow/task transitions
     - applies tenant/business authorization logic
     - composes official briefings or operational summaries
     - creates alternate chat/orchestration behavior not backed by API
   - Replace with server-driven flags/data such as:
     - allowed actions
     - status values
     - summary text
     - rationale snippets
     - unread/actioned state
     - task follow-up summaries

6. **Ensure approval actions hit the same backend state transitions as web**
   - Verify mobile approval actions call the same command path as web.
   - No separate mobile approval mutation logic.
   - Confirm response models reflect updated approval/entity state from backend.

7. **Keep intermittent connectivity handling presentation-only**
   - If mobile has retry/queue behavior, ensure it does not invent business outcomes locally.
   - Retries should replay the same API intent safely.
   - Do not persist local “approved/rejected/completed” business state as authoritative unless confirmed by backend.

8. **Add tests**
   - API tests for shared behavior used by both web and mobile:
     - approval action updates state correctly
     - alerts/briefings/status queries return expected contract shape
     - chat endpoint behavior remains backend-driven
   - If there are mobile unit tests, add tests ensuring mobile services are thin wrappers over API clients and do not contain domain rules.
   - If practical, add a small architecture test or code comment/assertion pattern documenting that mobile contains no business logic.

9. **Document the constraint**
   - Add concise code comments or a short note in relevant mobile service files or project docs:
     - mobile companion reuses backend APIs
     - business logic belongs in API/Application/Domain
     - mobile is a presentation client

10. **Keep changes minimal and aligned with existing patterns**
   - Follow current solution conventions.
   - Do not introduce unnecessary abstractions if existing service/client patterns already support this.

# Validation steps

1. Restore/build solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually verify code paths for ST-604 scenarios:
   - mobile sign-in and company selection use backend auth/tenant flows
   - mobile alert list is fetched from backend, not locally derived
   - mobile approval actions call backend and reflect returned state
   - mobile daily briefing view displays backend-provided summary/content
   - mobile direct agent chat uses backend chat/orchestration endpoints
   - mobile quick status/task follow-up summaries come from backend queries

4. Confirm no mobile-specific business logic remains:
   - search `src/VirtualCompany.Mobile/` for approval/status/workflow decision code
   - verify no domain entities/handlers/business policies were recreated in mobile
   - verify no mobile-only API endpoints were added

5. Confirm shared behavior with web:
   - compare web and mobile usage of approval/chat/briefing/status endpoints
   - ensure both clients rely on the same backend command/query semantics

6. If new/updated contracts were introduced:
   - verify serialization/deserialization works across API and mobile
   - verify backward compatibility where relevant

# Risks and follow-ups

- **Risk: existing mobile code may already contain convenience logic that is actually business logic.**
  - Be careful to distinguish UI formatting from domain decisions.

- **Risk: backend endpoints may not yet expose sufficiently mobile-friendly payloads.**
  - If so, extend shared endpoints/contracts rather than creating mobile-only logic.

- **Risk: shared DTO changes could affect web or API tests.**
  - Validate all consumers after refactoring.

- **Risk: intermittent connectivity may tempt local state mutation.**
  - Keep local retry behavior idempotent and non-authoritative.

Follow-ups to note if discovered during implementation:

- Add explicit server-driven fields like `allowedActions`, `summaryCards`, or `actionability` if mobile currently infers them.
- Consider a lightweight architecture guideline in the repo documenting:
  - web-first, mobile-companion
  - shared backend APIs
  - no client-specific business rules
- If duplication exists in both web and mobile, consider moving common contracts/query shaping into Application/Shared rather than only fixing mobile.