# Goal
Implement backlog task **TASK-7.2.5 — Blazor web is the primary onboarding surface** for story **ST-102 Company workspace creation and onboarding wizard**.

The coding agent should ensure the onboarding experience is clearly centered in the **Blazor Web App** as the primary user-facing flow for company workspace creation and guided setup, aligned with the architecture’s **web-first, mobile-companion** direction.

This task should result in a coherent, production-ready web onboarding path that:
- starts in `VirtualCompany.Web`
- supports company workspace creation and guided onboarding steps
- persists and resumes wizard progress through backend application services
- lands the user in the web dashboard with starter guidance on completion
- does **not** introduce mobile-first onboarding behavior or duplicate onboarding logic in MAUI

Because this task has no explicit acceptance criteria of its own, derive implementation boundaries from:
- ST-102 acceptance criteria
- architecture note: **“Blazor web is the primary onboarding surface.”**
- backlog note: **“Web-first, mobile-companion.”**

# Scope
In scope:
- Add or refine the **Blazor Web onboarding UX** for workspace creation and setup wizard.
- Ensure the web app is the canonical entry point for onboarding after authentication/company selection where applicable.
- Implement or wire up wizard steps for:
  - company name
  - industry
  - business type
  - timezone
  - currency
  - language
  - compliance region
- Support persisted wizard progress and resume behavior through backend APIs/application layer.
- Support template-driven prefills for industry/business defaults if the supporting backend already exists or can be added with minimal extension.
- Route successful completion to the web dashboard with starter guidance/empty-state messaging.
- Keep implementation aligned with modular monolith boundaries:
  - Web UI in `VirtualCompany.Web`
  - commands/queries in `VirtualCompany.Application`
  - entities/value objects in `VirtualCompany.Domain`
  - persistence/API wiring in `VirtualCompany.Infrastructure` / `VirtualCompany.Api`

Out of scope:
- Full mobile onboarding in `VirtualCompany.Mobile`
- SSO or advanced identity redesign
- Broad redesign of all dashboard experiences beyond the post-onboarding landing handoff
- Unrelated company setup features such as invitations unless needed for wizard scaffolding
- Overengineering a generic form-builder if a pragmatic wizard implementation is sufficient

# Files to touch
Prefer touching only files relevant to onboarding and routing. Likely areas:

- `src/VirtualCompany.Web/...`
  - onboarding pages/components
  - app routing/navigation
  - dashboard landing/empty-state components
  - shared form components or validation UI
- `src/VirtualCompany.Api/...`
  - endpoints/controllers/minimal APIs for company creation and onboarding progress
- `src/VirtualCompany.Application/...`
  - commands for create/update company setup
  - queries for loading/resuming onboarding state
  - DTOs/view models for wizard steps
  - validation
- `src/VirtualCompany.Domain/...`
  - company aggregate updates if needed
  - onboarding progress model if domain-owned
- `src/VirtualCompany.Infrastructure/...`
  - EF persistence/configuration for onboarding progress and company settings
  - repository implementations
- `README.md`
  - only if onboarding flow/setup documentation needs a brief update

Before editing, inspect the existing solution structure and reuse current patterns for:
- MediatR/CQRS or equivalent application dispatch
- endpoint organization
- EF Core entity configuration/migrations
- Blazor page/component conventions
- auth/tenant resolution

# Implementation plan
1. **Inspect existing onboarding and company setup flow**
   - Search for existing implementations of:
     - company creation
     - onboarding
     - wizard/progress
     - dashboard landing
     - memberships/tenant resolution
   - Determine whether ST-102 is partially implemented already.
   - Identify current route users hit after sign-in and whether onboarding gating already exists.

2. **Define the web-primary onboarding flow**
   - Establish the intended route sequence in the Blazor app, for example:
     - authenticated user with no company or incomplete setup → onboarding route
     - authenticated user with completed setup → dashboard
   - Ensure the web app owns the orchestration of the onboarding experience.
   - If there is any onboarding entry in MAUI, do not expand it; keep it minimal or redirect conceptually to web if appropriate.

3. **Model onboarding progress explicitly**
   - If not already present, add a persisted onboarding progress representation tied to company/workspace creation.
   - Support:
     - current step
     - completed steps
     - last updated timestamp
     - completion flag
     - optional draft values if needed
   - Keep schema pragmatic and compatible with the architecture note that flexible settings can live in JSONB where appropriate.

4. **Implement application-layer commands/queries**
   - Add or refine commands such as:
     - create workspace
     - save onboarding step
     - resume onboarding
     - complete onboarding
   - Add query/read model for loading current onboarding state for the signed-in user/company context.
   - Add validation for required fields and invalid transitions.

5. **Implement/update API endpoints**
   - Expose tenant-aware endpoints for:
     - creating a company workspace
     - fetching onboarding progress
     - saving/updating wizard state
     - completing onboarding
   - Ensure authorization and tenant scoping are enforced.
   - Return safe validation errors suitable for Blazor form display.

6. **Build/refine the Blazor onboarding wizard**
   - Create or update pages/components in `VirtualCompany.Web` to provide a guided wizard.
   - Include:
     - step navigation
     - field validation
     - save-and-resume behavior
     - prefill/default application where available
     - loading/error/empty states
   - Keep UX SSR-first and only add interactivity where needed.
   - Make the wizard clearly the main onboarding surface in navigation and routing.

7. **Add onboarding gating and redirect behavior**
   - Ensure users who have not completed onboarding are routed into the wizard from the web app.
   - Ensure successful completion redirects to the web dashboard.
   - Add starter guidance/empty-state content on the dashboard after completion, if not already present.

8. **Support template-prefill behavior**
   - If industry/business templates exist, wire them into the wizard to prefill recommended defaults.
   - If they do not exist, implement a minimal extensible mechanism rather than hardcoding UI-only logic.
   - Keep template handling backend-driven where possible.

9. **Persist completion and dashboard handoff**
   - On final step completion:
     - mark onboarding complete
     - persist company settings
     - ensure subsequent visits land on dashboard instead of wizard
   - Add starter guidance on the dashboard consistent with ST-102.

10. **Add tests**
   - Application tests:
     - create workspace
     - save/resume onboarding
     - complete onboarding
     - invalid field validation
   - API/integration tests if test infrastructure exists:
     - unauthorized/forbidden access
     - tenant-scoped progress retrieval
   - UI/component tests only if the repo already uses them; otherwise prioritize application/API coverage.

11. **Keep implementation aligned with architecture**
   - No business logic in Blazor components beyond presentation/form state.
   - No direct DB access from UI.
   - Respect tenant isolation.
   - Keep mobile untouched except where shared contracts require compile-safe updates.

# Validation steps
Run the relevant local validation after implementation:

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Manually verify the web onboarding flow:
   - sign in as a user without a completed workspace setup
   - confirm the user lands in the Blazor onboarding flow
   - create a company with:
     - name
     - industry
     - business type
     - timezone
     - currency
     - language
     - compliance region
   - save progress mid-flow
   - leave and resume
   - complete onboarding
   - confirm redirect to dashboard
   - confirm starter guidance is visible
   - confirm subsequent navigation does not force completed users back into onboarding

4. Verify tenant/security behavior:
   - confirm onboarding/progress endpoints are authenticated
   - confirm company-scoped data cannot be accessed across tenants
   - confirm validation errors are surfaced cleanly in the web UI

5. If migrations are added:
   - generate/apply migration per repo conventions
   - verify schema updates for onboarding progress/settings persistence

# Risks and follow-ups
- **Existing implementation overlap:** ST-102 may already be partially implemented; avoid duplicating flows. Refactor toward a single canonical Blazor onboarding path.
- **Unknown auth/tenant bootstrap flow:** If company creation occurs before tenant context exists, carefully handle the transition from authenticated user to first company membership.
- **Progress persistence design:** Storing wizard progress in company settings JSONB is fast, but a dedicated structure may be cleaner if resume/state transitions are important.
- **Template extensibility:** Avoid hardcoded switch statements in UI for industry/business defaults if the story expects extensibility without code changes.
- **Routing complexity in Blazor:** Onboarding gating can create redirect loops if completion state is not loaded carefully; validate authenticated/loading/completed/incomplete states explicitly.
- **Dashboard dependency:** If the dashboard is incomplete, implement minimal starter guidance/empty state rather than blocking onboarding completion.
- **Follow-up candidates:**
  - invitation step integration after company creation
  - richer template catalog for onboarding defaults
  - analytics on onboarding completion/drop-off
  - responsive/mobile-web polish while keeping MAUI as companion only