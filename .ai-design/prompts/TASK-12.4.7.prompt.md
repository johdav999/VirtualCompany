# Goal
Implement **TASK-12.4.7 — Optimize for concise payloads and intermittent connectivity** for **ST-604 Mobile companion for approvals, alerts, and quick chat**.

The coding agent should improve the mobile companion and shared backend contracts so the MAUI app performs well on unreliable networks and small mobile bandwidth budgets, while preserving tenant safety and reusing existing backend business logic.

This task should focus on:
- reducing payload size for mobile-focused endpoints and DTOs
- supporting resilient mobile reads/writes under intermittent connectivity
- enabling lightweight sync/retry behavior for approvals, alerts, briefings, quick status, and direct agent chat
- avoiding introduction of mobile-only business rules in the backend

Because no explicit acceptance criteria were provided for this task, derive implementation targets from:
- ST-604 acceptance criteria
- architecture guidance that mobile is a focused companion, not full parity
- backlog note: **“Optimize for concise payloads and intermittent connectivity.”**

# Scope
In scope:
- Review existing mobile-facing API usage and shared DTOs/contracts
- Introduce or refine **mobile-optimized response models** for:
  - alert list
  - approval inbox/list/detail needed for actioning
  - daily briefing view
  - quick company status / task follow-up summary
  - direct agent chat conversation list/messages as needed
- Ensure payloads exclude unnecessary fields, large nested objects, and verbose text where summaries suffice
- Add support for **incremental fetch patterns** where practical, such as:
  - pagination / cursor / since timestamp
  - unread/newer-than queries
  - compact list endpoints vs full detail endpoints
- Improve mobile client resilience for intermittent connectivity:
  - local caching of last successful reads for key screens
  - queued retry for approval actions and chat sends where safe
  - clear online/offline/loading/error states
- Preserve backend consistency so mobile approval actions update the same approval state as web
- Add tests for DTO shaping, endpoint behavior, and any retry-safe semantics

Out of scope unless already trivial in current codebase:
- full offline-first architecture
- background bidirectional sync engine for all entities
- push notification infrastructure
- major auth redesign
- introducing separate mobile-only domain logic
- broad API versioning overhaul

# Files to touch
Likely areas to inspect and update first:
- `src/VirtualCompany.Mobile/`
- `src/VirtualCompany.Api/`
- `src/VirtualCompany.Application/`
- `src/VirtualCompany.Shared/`
- `src/VirtualCompany.Infrastructure/`
- `tests/VirtualCompany.Api.Tests/`

Potential file categories:
- Mobile pages/viewmodels/services for alerts, approvals, briefing, chat, and status
- Shared request/response DTOs currently reused by web/mobile
- API controllers/endpoints exposing mobile-used data
- Application query handlers for mobile list/detail projections
- Infrastructure query/repository code if projections need optimization
- Tests covering API contracts and mobile-safe behavior

Before coding, identify the exact existing files for:
- approval endpoints and DTOs
- notifications/alerts endpoints and DTOs
- briefing/status endpoints
- chat endpoints and DTOs
- MAUI API client abstractions, local storage/cache, and connectivity handling

Prefer touching existing abstractions over creating parallel stacks unless current contracts are too verbose for mobile use.

# Implementation plan
1. **Assess current mobile data flow**
   - Inspect `VirtualCompany.Mobile` to find screens/services for:
     - sign-in/company selection
     - alerts
     - approvals
     - daily briefing
     - quick chat
     - status/task follow-up
   - Trace each screen to the API endpoints and DTOs it consumes.
   - Identify oversized payloads, repeated fields, large nested graphs, and endpoints that force full refreshes.

2. **Define compact mobile contracts**
   - In shared contracts or API-layer DTOs, create compact models where needed, for example:
     - `MobileAlertListItemDto`
     - `MobileApprovalListItemDto`
     - `MobileApprovalActionRequest`
     - `MobileBriefingDto`
     - `MobileCompanyStatusDto`
     - `MobileTaskFollowUpSummaryDto`
     - `MobileConversationSummaryDto`
     - `MobileMessageListItemDto`
   - Keep fields intentionally minimal:
     - ids
     - timestamps
     - status
     - short titles/summaries
     - compact actor/source labels
     - only the fields required to render the mobile screen
   - Avoid embedding full related entities when ids + display labels are enough.
   - Prefer concise rationale summaries over verbose bodies where the mobile UX only needs a preview.

3. **Add list/detail separation**
   - Where current endpoints return heavy detail objects for list screens, split into:
     - compact list endpoint/query
     - detail endpoint/query only when user drills in
   - For example:
     - approvals list returns summary cards only
     - approval detail returns threshold context and concise rationale needed for decision
     - chat conversation list returns latest message preview, not full history
   - Keep web behavior unchanged unless shared compact contracts are clearly better for both.

4. **Support incremental and bandwidth-aware fetches**
   - Add query parameters or request models for:
     - page size / cursor / continuation token if patterns already exist
     - `since` / `updatedSince`
     - unread-only or pending-only filters
     - message pagination for chat history
   - Ensure application queries project directly to compact DTOs rather than loading full aggregates first.
   - If current implementation materializes large domain objects, refactor to efficient query projections.

5. **Make approval and chat writes retry-safe**
   - For mobile-originated write actions, inspect whether idempotency or duplicate protection exists.
   - If absent and feasible, add lightweight safeguards such as:
     - client-generated operation/request id
     - server-side duplicate detection for approval decisions and chat sends
   - Approval actions must remain authoritative in backend domain logic and update the same state as web.
   - Do not allow duplicate approval decisions or duplicate chat messages from transient reconnect retries.

6. **Improve MAUI connectivity handling**
   - In `VirtualCompany.Mobile`, use existing connectivity abstractions or add a small one around MAUI network status.
   - Implement:
     - cached last-known-good data for alerts, approvals, briefing, and status
     - stale-data indication in UI
     - retry on reconnect or manual refresh
     - queued submission for safe actions if architecture already supports local persistence; otherwise at minimum preserve unsent intent and surface retry UX
   - For chat:
     - show pending/sending/failed states for outbound messages
     - retry failed sends without duplicating on success
   - For approvals:
     - if offline, either queue safely with idempotency support or block with explicit messaging; prefer queue only if implementation can be made reliable in this task scope

7. **Add lightweight local persistence**
   - Reuse existing storage mechanism if present; otherwise add minimal local storage for:
     - cached list/detail payloads
     - pending outbound operations metadata
     - last sync timestamps
   - Keep cached data scoped by signed-in user and selected company to avoid tenant leakage on shared devices.
   - Clear or partition cache appropriately on sign-out/company switch.

8. **Preserve clean architecture boundaries**
   - Business rules stay in Application/Domain/API layers shared by web and mobile.
   - Mobile-specific optimization should mostly live in:
     - API query/DTO shaping
     - mobile client caching/retry/presentation
   - Avoid embedding approval policy or workflow logic in the MAUI app.

9. **Testing**
   - Add/extend API tests to verify:
     - compact endpoints return only expected fields/shape
     - tenant scoping still applies
     - approval action retries do not create invalid duplicate state transitions
     - pagination/incremental fetch behavior works as intended
   - Add mobile unit tests where practical for:
     - cache fallback behavior
     - connectivity-aware service logic
     - pending/failed/retry state transitions

10. **Document assumptions**
   - If no existing mobile endpoints exist, document which shared endpoints were optimized.
   - If true offline approval queueing is too risky without stronger idempotency infrastructure, implement read caching + explicit retry UX and note queueing as follow-up.

# Validation steps
1. Restore/build/test:
   - `dotnet build`
   - `dotnet test`

2. Verify API contract behavior:
   - Confirm mobile list endpoints return compact payloads for:
     - alerts
     - approvals
     - briefing/status
     - chat summaries/messages
   - Confirm detail endpoints still provide enough data for action screens.
   - Confirm tenant scoping remains enforced.

3. Verify payload optimization manually:
   - Compare before/after response shapes for mobile-used endpoints.
   - Ensure large nested objects and unnecessary fields are removed from list responses.
   - Ensure chat history and approval lists are paged or incrementally fetched where implemented.

4. Verify intermittent connectivity behavior in MAUI:
   - Launch mobile app and load key screens online.
   - Disable network and confirm cached data is shown where expected.
   - Re-enable network and confirm refresh/retry succeeds.
   - For chat, verify pending/failed/retry UX.
   - For approvals, verify offline/online behavior is explicit and safe.

5. Verify approval consistency:
   - Approve/reject from mobile path.
   - Confirm backend approval state matches web-visible state and no duplicate decision is recorded on retry.

6. Verify company isolation in local cache:
   - Switch company or sign out/sign in as another user if supported.
   - Confirm cached mobile data does not bleed across tenant/user context.

# Risks and follow-ups
- **Risk: duplicate writes under reconnect conditions**
  - Approval decisions and chat sends are sensitive to retry duplication.
  - Mitigate with idempotency/request IDs where feasible.

- **Risk: shared DTO changes may break web consumers**
  - Prefer additive mobile-specific DTOs/endpoints or carefully preserve existing contracts.

- **Risk: offline approval queueing may be unsafe without stronger guarantees**
  - If robust idempotent processing cannot be added cleanly in this task, prefer explicit failed/pending retry UX over silent queueing.

- **Risk: local cache can leak tenant data**
  - Partition cache by user + company and clear on sign-out/company switch.

- **Risk: over-optimizing payloads may remove fields needed by current UI**
  - Validate each mobile screen against actual rendering needs before trimming.

Follow-ups to note if not completed here:
- push notifications for alerts/approvals
- richer sync protocol with server-issued cursors
- ETag/If-None-Match or delta sync support
- background sync worker in MAUI
- telemetry on payload sizes, cache hit rate, and retry outcomes