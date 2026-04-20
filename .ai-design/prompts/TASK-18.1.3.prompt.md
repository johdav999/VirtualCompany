# Goal
Implement `TASK-18.1.3` for `US-18.1 ST-FUI-201 — Invoice review workbench and recommendation drilldown` by adding role-aware action gating and actionable-state checks for invoice review controls on the invoice review detail page.

The change must ensure that approve, reject, and send-for-follow-up controls are only rendered and/or enabled when both of these are true:

1. The current user has permission to perform the action.
2. The invoice is currently in an actionable review state according to finance workflow data returned by existing APIs.

Preserve the existing architecture and reuse current finance workflow APIs, authorization patterns, and UI state conventions where possible.

# Scope
In scope:

- Review the existing invoice review list/detail implementation for ST-FUI-201.
- Identify the current source of truth for:
  - current user role/permissions
  - invoice review status / recommendation status / actionable state
  - finance workflow actions and route structure
- Add a UI-level gating model for invoice review actions:
  - approve
  - reject
  - send-for-follow-up
- Ensure gating is based on both permission and invoice actionable state.
- Prevent users from seeing actionable controls when they are not allowed.
- If the UI pattern in this codebase prefers disabled buttons with explanation instead of hiding, follow the established pattern consistently. Otherwise hide unavailable actions.
- Add or update component/page tests covering:
  - allowed + actionable
  - allowed + non-actionable
  - not allowed + actionable
  - loading/error states if impacted by the gating logic
- Keep filters, routing, list/detail rendering, and API error/loading states working as required by the story.

Out of scope unless required by existing code structure:

- Creating new backend finance workflow endpoints.
- Redesigning the invoice review UX beyond what is needed for gating.
- Introducing a new authorization framework if one already exists.
- Broad refactors unrelated to invoice review controls.

# Files to touch
Inspect and update the actual files you find, likely in these areas:

- `src/VirtualCompany.Web/**`
  - invoice review list page
  - invoice review detail page
  - invoice review row/detail components
  - shared authorization/permission helpers
  - route registration/navigation components
  - query-string filter state helpers
- `src/VirtualCompany.Shared/**`
  - DTOs/view models/enums for invoice review status, recommendation outcome, available actions, or permissions
- `src/VirtualCompany.Api/**`
  - only if the web app currently depends on an API contract that must expose actionable-state metadata already available from backend workflow responses
- `src/VirtualCompany.Application/**`
  - only if there is an application-layer query/mapper already shaping invoice review detail data for the UI and it needs to surface actionability flags
- `tests/**`
  - web/component tests for list/detail views and action gating
  - API/application tests only if contract shaping changes are necessary

Before editing, locate the concrete implementation files by searching for terms like:

- `invoice review`
- `finance review`
- `recommendation`
- `approve`
- `reject`
- `follow-up`
- route/page names related to finance workflow review

# Implementation plan
1. **Discover the existing implementation**
   - Find the finance review route and the invoice review detail page.
   - Identify the DTO/view model used by the list and detail views.
   - Identify how the current user’s role/permissions are exposed in the Blazor app.
   - Identify whether the API already returns:
     - allowed actions
     - invoice workflow status
     - recommendation status
     - actionable/non-actionable state
   - Prefer existing fields from the finance workflow API over inventing new client-only rules.

2. **Define the gating rule clearly**
   - Implement a single, easy-to-test decision point for each action:
     - `CanApprove`
     - `CanReject`
     - `CanSendForFollowUp`
   - Each decision must combine:
     - user permission/role check
     - invoice actionable-state check
   - If the backend already returns per-action availability, treat that as authoritative and combine it with current-user permission if needed.
   - If the backend only returns status, derive actionable state from the existing finance workflow status enum/values in one place only.

3. **Centralize action availability logic**
   - Add a small helper/policy/view-model mapper in the web layer or shared layer, depending on existing patterns.
   - Avoid scattering inline conditional logic across Razor components.
   - Example shape, adapt to project conventions:
     - `InvoiceReviewActionAvailability`
     - `InvoiceReviewActionPolicy`
     - computed properties on detail view model
   - Include optional reason text only if the current UX already supports tooltips/help text for unavailable actions.

4. **Update the detail page UI**
   - On the dedicated review result page, render action controls according to the centralized gating logic.
   - Ensure approve/reject/send-for-follow-up are only available when permitted and actionable.
   - Keep recommendation summary, recommended action, confidence, source invoice link, and related approval link intact.
   - Preserve loading, empty/not-found, and API error states.

5. **Verify list page and route acceptance criteria remain satisfied**
   - Confirm the finance review route exists and still lists invoices with reviewable statuses from existing APIs.
   - Confirm row fields still include:
     - invoice number
     - supplier name
     - amount
     - currency
     - risk level
     - recommendation status
     - confidence
     - last updated timestamp
   - Confirm filters still sync to URL query string.
   - Do not regress navigation from list row to detail page.

6. **Handle edge cases**
   - Non-actionable statuses should not expose actions even for authorized users.
   - Unauthorized users should not see actions even if the invoice is actionable.
   - If invoice detail data is partially loaded, do not show actions until the required permission/state data is known.
   - If the API returns an unknown status, default to non-actionable.
   - Follow default-deny behavior consistent with the architecture and backlog notes.

7. **Add tests**
   - Add/extend component tests for the detail page or action panel covering:
     - user has permission + invoice actionable => actions shown/enabled
     - user has permission + invoice not actionable => actions hidden/disabled
     - user lacks permission + invoice actionable => actions hidden/disabled
     - unknown/unsupported status => actions hidden/disabled
   - If there is a dedicated action availability helper, add focused unit tests for it.
   - Keep or add tests for loading/empty/error states if the component structure changes.

8. **Keep implementation aligned with architecture**
   - Respect modular boundaries.
   - Keep authorization checks policy-oriented and tenant-safe.
   - Do not move business workflow logic into ad hoc UI code if an application/shared contract already exists.
   - Prefer CQRS-lite read-model shaping if the detail page already uses a query model.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run relevant tests first, then full suite if practical:
   - `dotnet test`

3. Manually verify in the web app, if runnable in this workspace:
   - Navigate to the finance review route.
   - Confirm invoice list renders reviewable invoices.
   - Confirm filters update the URL query string.
   - Open an invoice detail page.
   - Verify recommendation summary and related links still render.
   - Verify action controls for these scenarios:
     - authorized user + actionable invoice
     - authorized user + non-actionable invoice
     - unauthorized user + actionable invoice
     - API loading/error state

4. Validate no regressions in route/navigation behavior:
   - list to detail navigation
   - back navigation preserving filters/query string if already supported

5. If contracts changed, verify serialization/deserialization and any API/client integration tests still pass.

# Risks and follow-ups
- **Risk: permission source ambiguity**
  - The app may have multiple role/permission mechanisms. Reuse the established one rather than adding a parallel check.

- **Risk: actionable-state rules duplicated**
  - If status-to-actionability mapping is implemented in multiple places, behavior may drift. Centralize it.

- **Risk: backend vs UI authority mismatch**
  - If the backend already determines available actions, do not override it with conflicting client logic. Prefer backend-authoritative flags when present.

- **Risk: hidden vs disabled UX inconsistency**
  - Match the existing design system/page pattern. Do not introduce a new interaction style unless necessary.

- **Risk: acceptance criteria regression**
  - This task is specifically about action gating, but the story also requires route availability, list fields, filters, detail content, and loading/error states. Re-verify all of them after changes.

Follow-ups if needed after implementation:
- Add server-side enforcement tests if action endpoints are not already protected.
- Consider exposing explicit per-action availability and denial reasons from the API if the current contract is too implicit.
- Add audit-friendly UI messaging for why an action is unavailable, if product/design wants that behavior later.