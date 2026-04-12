# Goal
Implement backlog task **TASK-12.4.1** for **ST-604 Mobile companion for approvals, alerts, and quick chat** by delivering a focused **.NET MAUI mobile companion experience** in `src/VirtualCompany.Mobile` that supports:

- sign-in
- company selection
- alert list
- approval actions
- daily briefing view
- direct agent chat
- quick company status and task follow-up summaries

The implementation must **reuse existing backend APIs** where available, add only minimal shared contracts if needed, and **avoid introducing mobile-specific business logic**. Keep the mobile app intentionally limited in scope and aligned with the architecture’s “web-first, mobile-companion” principle.

# Scope
Implement the mobile companion app experience for the story with pragmatic, production-oriented defaults.

In scope:
- Mobile app shell/navigation for the companion experience
- Authentication/session bootstrap for signed-in use
- Company/workspace selection when user has multiple memberships
- Alerts/inbox-style list for approvals, escalations, workflow failures, and briefing availability
- Approval detail and approve/reject actions
- Daily briefing view
- Direct chat with named agents
- Quick company status/task follow-up summary surface
- API client integration and shared DTO usage where appropriate
- Basic loading, empty, and error states
- Intermittent connectivity-aware UX at a basic level:
  - retry actions
  - cached last-selected company/session where reasonable
  - clear offline/error messaging

Out of scope unless already trivial and clearly supported by existing APIs:
- Full admin/workflow parity with web
- New backend business workflows unique to mobile
- Rich offline sync engine
- Push notification infrastructure if not already present
- Full design-system overhaul
- New orchestration logic in mobile
- Mobile-specific approval rules or chat behavior

If backend gaps are discovered, prefer:
1. reusing existing endpoints,
2. adding thin API/query endpoints in existing modules,
3. adding shared contracts,
4. avoiding domain redesign.

# Files to touch
Prioritize these areas, adjusting to actual repository structure after inspection.

Primary:
- `src/VirtualCompany.Mobile/**`
- `src/VirtualCompany.Shared/**`

Possible supporting files if contracts/endpoints are missing:
- `src/VirtualCompany.Api/**`
- `src/VirtualCompany.Application/**`

Potential mobile files to add/update:
- `src/VirtualCompany.Mobile/App.xaml`
- `src/VirtualCompany.Mobile/AppShell.xaml`
- `src/VirtualCompany.Mobile/MauiProgram.cs`
- `src/VirtualCompany.Mobile/ViewModels/**`
- `src/VirtualCompany.Mobile/Views/**`
- `src/VirtualCompany.Mobile/Services/**`
- `src/VirtualCompany.Mobile/Models/**`
- `src/VirtualCompany.Mobile/Resources/**`

Potential shared/API files to add/update:
- `src/VirtualCompany.Shared/**/Dtos/*.cs`
- `src/VirtualCompany.Shared/**/Contracts/*.cs`
- `src/VirtualCompany.Api/**/Controllers/*.cs` or endpoint mappings
- `src/VirtualCompany.Application/**/Queries/*.cs`
- `src/VirtualCompany.Application/**/Commands/*.cs`

Tests if present/appropriate:
- `tests/VirtualCompany.Api.Tests/**`

Before coding, inspect the current solution structure and reuse existing patterns for:
- DI registration
- HTTP clients
- auth/session handling
- CQRS/query handlers
- DTO naming
- MAUI page/viewmodel conventions

# Implementation plan
1. **Inspect current implementation and map existing capabilities**
   - Review `README.md`, solution structure, and all project references.
   - Inspect `src/VirtualCompany.Mobile` to determine:
     - current MAUI architecture pattern (MVVM, Shell, CommunityToolkit, etc.)
     - existing auth/session handling
     - existing API client abstractions
     - current navigation and state management
   - Inspect backend/shared projects for existing endpoints/contracts covering:
     - sign-in/session bootstrap
     - memberships/company selection
     - alerts/notifications/inbox
     - approvals list/detail/action
     - daily briefings
     - direct agent conversations/messages
     - company status/task summaries

2. **Define the minimum mobile information architecture**
   Implement a compact companion app structure such as:
   - Sign-in
   - Company selection
   - Home / Status
   - Alerts
   - Approvals
   - Briefing
   - Chat

   If a combined inbox pattern is already present, align with it rather than forcing separate tabs.

3. **Implement authentication bootstrap**
   - Reuse existing auth flow if already implemented.
   - If missing in mobile, add a minimal sign-in/session flow compatible with backend auth.
   - Persist only necessary session data securely using platform-appropriate storage already used in the app.
   - On app launch:
     - restore session if valid
     - otherwise route to sign-in
   - Do not invent a new auth protocol; adapt to existing backend expectations.

4. **Implement company selection**
   - Fetch available company memberships for the signed-in user.
   - If only one company exists, auto-select it.
   - If multiple companies exist, present a selection screen.
   - Persist last selected company locally for convenience.
   - Ensure all tenant-owned requests include selected company context using existing API conventions.

5. **Implement mobile API service layer**
   In `VirtualCompany.Mobile`, add or extend typed service clients for:
   - auth/session
   - memberships/company context
   - alerts/notifications
   - approvals
   - briefings/status
   - chat/conversations/messages

   Requirements:
   - centralize HTTP configuration
   - include auth token and company context
   - support cancellation tokens
   - map backend errors to user-friendly messages
   - keep payloads concise and mobile-friendly
   - avoid embedding business rules in the client

6. **Implement alerts list**
   - Build an alerts/inbox page showing concise items such as:
     - pending approvals
     - escalations
     - workflow failures
     - briefing availability
   - Include:
     - loading state
     - empty state
     - pull-to-refresh if consistent with current app patterns
     - unread/read or actioned indicators if supported by backend
   - Tapping an item should navigate to the relevant detail screen where possible.

7. **Implement approval actions**
   - Build approval list and/or detail view depending on existing API shape.
   - Show concise approval context:
     - approval type
     - linked entity
     - threshold/rationale summary
     - status
     - created date
   - Support approve/reject actions.
   - Require comment on reject only if backend already expects it; otherwise keep optional.
   - After action:
     - refresh approval state
     - update list/inbox
     - show success/failure feedback
   - Ensure mobile actions update the same backend approval state as web.

8. **Implement daily briefing view**
   - Add a page for the latest daily briefing.
   - Show:
     - summary text
     - alerts/highlights
     - approvals needing attention
     - KPI/anomaly/notable update snippets if available
   - If multiple briefings/history are already supported, show a simple list with latest-first.
   - If only latest is supported, keep UI simple and explicit.
   - Reuse message/notification models if briefings are stored that way.

9. **Implement quick company status and task follow-up summaries**
   - Add a lightweight home/status page that surfaces:
     - selected company
     - quick status summary
     - pending approvals count
     - alert count
     - recent task follow-up summaries
   - Prefer existing dashboard summary endpoints or briefing-derived summaries.
   - If no dedicated endpoint exists, compose from existing lightweight queries without duplicating business logic.

10. **Implement direct agent chat**
    - Add:
      - agent conversation list or agent picker
      - direct conversation page
      - message history
      - send message action
    - Reuse existing conversation/message contracts and direct-agent channel type.
    - Support pagination or incremental loading if backend already supports it.
    - Keep chat UX simple:
      - text messages only unless existing message types are already supported
      - loading/sending indicators
      - retry/error feedback
    - If chat can create/link tasks through backend orchestration, display returned task linkage when available, but do not implement extra mobile-only task creation logic.

11. **Add shared DTOs/contracts only where necessary**
    If mobile needs contracts not yet shared:
    - place reusable request/response DTOs in `VirtualCompany.Shared`
    - keep naming consistent with existing modules
    - avoid leaking domain entities directly to mobile
    - prefer query/result DTOs tailored to UI needs

12. **Fill backend gaps minimally if required**
    Only if inspection shows missing endpoints needed for the story, add thin backend support:
    - tenant-scoped query endpoints for alerts, briefings, status summaries, approvals, and chat
    - approval action command endpoints
    - direct conversation/message endpoints

    Backend additions must:
    - follow existing modular monolith boundaries
    - enforce tenant scoping
    - use CQRS-lite patterns already present
    - avoid mobile-specific branching in application logic

13. **Handle state, loading, and resilience**
    - Add consistent busy/error/empty states across pages.
    - Handle expired session by redirecting to sign-in.
    - Handle missing company selection gracefully.
    - For intermittent connectivity:
      - show non-blocking error states
      - allow manual refresh/retry
      - cache only lightweight local state such as selected company and maybe last-loaded briefing if easy and already patterned

14. **Keep UX intentionally constrained**
    Ensure the mobile app remains a companion:
    - concise summaries over dense admin screens
    - action-oriented views over full workflow builders
    - no attempt to mirror the full web dashboard/admin surface

15. **Add tests where practical**
    - Add/adjust API tests for any new endpoints or approval action behavior.
    - Add unit tests for application handlers if backend logic is introduced.
    - If the mobile project already has test coverage patterns, add viewmodel/service tests for key flows; otherwise do not create a large new test harness just for this task.

16. **Document assumptions in code comments or a short README update if needed**
    - Note any backend dependencies
    - Note any intentionally stubbed or deferred mobile features
    - Keep comments concise and implementation-focused

# Validation steps
1. Restore/build solution:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually validate mobile flow in the MAUI app:
   - launch app
   - sign in successfully
   - verify company selection appears for multi-membership users
   - verify single-company users proceed automatically
   - verify selected company context persists across app restart if implemented

4. Validate alerts/inbox:
   - alerts load for selected company
   - empty state is shown when no alerts exist
   - refresh works
   - tapping alert navigates correctly where supported

5. Validate approvals:
   - pending approvals are visible
   - approval detail shows concise context
   - approve action updates backend state
   - reject action updates backend state
   - acted-on approvals no longer appear as pending after refresh

6. Validate daily briefing:
   - latest briefing loads
   - summary/highlights render correctly
   - empty state appears if no briefing exists

7. Validate status/task follow-up summary:
   - home/status page loads concise company summary
   - pending counts and recent follow-up items display correctly

8. Validate direct agent chat:
   - open direct conversation with an agent
   - load message history
   - send a message
   - receive/display agent response if backend supports it in current environment
   - verify conversation remains tenant-scoped and company-scoped

9. Validate auth and tenant safety:
   - switching company changes data context
   - unauthorized or expired session responses are handled safely
   - no cross-company data leakage in mobile views

10. If backend endpoints were added:
   - verify API tests cover tenant scoping and approval action behavior
   - verify new endpoints follow existing auth/authorization conventions

# Risks and follow-ups
- **Backend readiness risk:** Some required mobile endpoints may not yet exist. If so, add only thin tenant-scoped query/command endpoints and avoid domain redesign.
- **Auth integration risk:** Mobile sign-in may depend on an existing auth approach not yet wired into MAUI. Reuse current patterns rather than inventing a parallel flow.
- **Notification model ambiguity:** Alerts may be represented as notifications, messages, approvals, or dashboard summaries. Normalize in the mobile service layer only as needed for display.
- **Chat complexity risk:** Real-time updates may not be available. Polling/manual refresh is acceptable for this task if consistent with current backend capabilities.
- **Offline expectations:** The story mentions intermittent connectivity optimization, but full offline sync is out of scope. Keep to graceful retry and lightweight local persistence.
- **UI scope creep:** Do not expand into full workflow/task/admin parity. Preserve the companion-app boundary.
- **Follow-up candidates after this task:**
  - push notifications
  - richer approval inbox filtering
  - cached briefing/history for offline reading
  - chat streaming/real-time updates
  - deeper task follow-up drill-down
  - mobile-specific accessibility and polish pass