# Goal
Implement **TASK-7.2.2: Setup wizard persists progress and supports resume** for **ST-102 Company workspace creation and onboarding wizard** in the existing .NET solution.

The coding agent should add end-to-end support for:
- saving in-progress onboarding/setup wizard state for a company workspace,
- resuming the wizard later for the same authenticated user/company context,
- restoring previously entered values into the Blazor web onboarding flow,
- safely handling incomplete, completed, and abandoned setup sessions.

This task must align with the architecture and backlog:
- multi-tenant, tenant-scoped behavior,
- Blazor Web App as the primary onboarding surface,
- ASP.NET Core modular monolith,
- PostgreSQL persistence,
- company setup module ownership,
- CQRS-lite application structure,
- no mobile work required.

# Scope
In scope:
- Persist wizard progress for workspace creation/onboarding.
- Support draft/in-progress state across page refreshes, logout/login, and later resume.
- Store the setup data needed by ST-102 fields:
  - company name
  - industry
  - business type
  - timezone
  - currency
  - language
  - compliance region
  - any wizard step/progress metadata needed for resume
- Add backend domain/application/infrastructure support for draft onboarding state.
- Add or update API/endpoints/services used by the Blazor web app.
- Update Blazor onboarding UI to:
  - load existing draft state,
  - save progress incrementally,
  - resume at the correct step,
  - handle completed setup appropriately.
- Ensure tenant/user authorization rules are respected.
- Add tests for persistence/resume behavior.

Out of scope unless required by existing code patterns:
- Full template-prefill implementation beyond preserving selected values already entered.
- Dashboard/starter guidance after completion.
- Invitations, memberships, or unrelated onboarding stories.
- Mobile app changes.
- Large UX redesigns.
- Background jobs unless already required by the current implementation approach.

If the repository already has partial onboarding/company creation flow, extend it rather than replacing it.

# Files to touch
Inspect the solution first and then modify the minimal correct set of files. Likely areas include:

- `src/VirtualCompany.Domain/**`
  - company setup/onboarding entities, value objects, enums, domain services
- `src/VirtualCompany.Application/**`
  - commands/queries/DTOs/validators/handlers for:
    - save wizard progress
    - get resumable wizard state
    - complete wizard
- `src/VirtualCompany.Infrastructure/**`
  - EF Core persistence configuration
  - repositories
  - migrations
- `src/VirtualCompany.Api/**`
  - onboarding/company setup endpoints or controllers
- `src/VirtualCompany.Web/**`
  - Blazor pages/components/view models/services for onboarding wizard
- `README.md`
  - only if setup/build/run notes must be updated

Expected concrete additions may include:
- a persisted onboarding draft/session model, likely under Company Setup module
- EF migration for new table/columns
- request/response contracts in shared or application layer
- Blazor page/component updates for auto-save/manual save and resume

Prefer touching existing onboarding/company setup files if they already exist. Do not introduce parallel patterns if the codebase already has conventions.

# Implementation plan
1. **Inspect current onboarding/company creation flow**
   - Find existing implementation for ST-102 or related company creation.
   - Identify:
     - where company creation currently happens,
     - whether wizard steps already exist,
     - whether there is already a draft/session concept,
     - how tenant/user context is resolved,
     - whether the web app calls API endpoints directly or via shared application services.
   - Reuse existing architectural patterns and naming.

2. **Design persistence model for resumable setup**
   - Introduce a persisted onboarding draft/session model if none exists.
   - Recommended shape:
     - `Id`
     - `UserId` or creator/owner user reference
     - optional `CompanyId` if company record is created early
     - status: `InProgress`, `Completed`, `Abandoned` or equivalent
     - current step identifier/index
     - setup payload fields for ST-102
     - optional flexible JSON payload for future wizard expansion
     - timestamps: created/updated/completed
   - Keep it tenant-safe:
     - if company does not yet exist, scope by authenticated user;
     - if company exists during setup, also scope by `company_id`.
   - Prefer a model that can evolve for future onboarding steps without schema churn.

3. **Add domain and persistence support**
   - Add the entity/aggregate and any enums/value objects needed.
   - Configure EF Core mapping in Infrastructure.
   - Add migration.
   - Ensure constraints/indexes support:
     - one active draft per user per onboarding flow, or another repository-consistent rule,
     - efficient lookup for “resume my setup”.
   - If the architecture already stores flexible settings in JSONB, use that where appropriate.

4. **Add application commands/queries**
   - Implement CQRS-lite operations such as:
     - save/update onboarding progress,
     - get current onboarding draft for authenticated user,
     - mark onboarding complete,
     - optionally discard/reset draft.
   - Include validation:
     - valid step transitions if enforced,
     - required fields only when completing, not while saving draft,
     - field-level validation for known company fields.
   - Ensure handlers enforce authorization and tenant/user scoping.

5. **Expose API surface**
   - Add or update endpoints for:
     - `GET` current resumable onboarding state
     - `POST`/`PUT` save progress
     - `POST` complete onboarding
   - Return safe, UI-friendly DTOs including:
     - current step
     - saved values
     - completion status
     - whether resume is available
   - Follow existing API conventions for auth, problem details, and route structure.

6. **Update Blazor onboarding wizard**
   - On load:
     - query for existing draft/resumable state,
     - if found and not completed, restore values and navigate to saved step,
     - if completed, redirect appropriately or show completed state according to existing flow.
   - During use:
     - save progress on step advance and/or explicit save points,
     - optionally debounce autosave if the current UI pattern supports it,
     - surface save/resume state clearly but minimally.
   - On completion:
     - finalize company creation/setup,
     - mark draft/session completed,
     - prevent stale resume from reopening a finished wizard.
   - Keep SSR/interactivity consistent with current Blazor app patterns.

7. **Handle edge cases**
   - Refresh during setup should not lose progress.
   - Logging out and back in should allow resume for the same user.
   - Starting setup again after completion should not create ambiguous active drafts.
   - Invalid or stale draft data should fail safely and allow reset/restart.
   - If a company was partially created earlier, resume should not duplicate company records.

8. **Testing**
   - Add unit and/or integration tests for:
     - saving draft progress,
     - loading resumable state,
     - restoring current step,
     - completing setup clears or finalizes resumable state,
     - unauthorized access to another user/company draft is blocked,
     - incomplete drafts do not require all final fields.
   - Add UI/component tests if the repo already uses them; otherwise focus on application/API tests.

9. **Keep implementation aligned with backlog intent**
   - This task specifically fulfills the ST-102 acceptance criterion:
     - “Setup wizard persists progress and supports resume.”
   - Do not overreach into unrelated onboarding features unless necessary for correctness.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run tests before changes to establish baseline:
   - `dotnet test`

3. Apply migrations if the solution uses EF Core migrations in the normal workflow.
   - Generate migration only if one does not already exist for this task.
   - Verify database schema updates cleanly.

4. Run the app and manually verify the onboarding flow:
   - Start the relevant API/web projects per existing repo conventions.
   - Begin workspace setup.
   - Enter partial data and advance at least one step.
   - Refresh the page and confirm values/step are restored.
   - Sign out/sign back in and confirm resume works.
   - Complete the wizard and confirm:
     - completion succeeds,
     - resumable draft is marked completed or no longer resumes as in-progress,
     - no duplicate company/workspace is created.

5. Verify authorization/tenant safety:
   - Confirm one user cannot load another user’s onboarding draft.
   - Confirm company-scoped data remains isolated.

6. Re-run full tests:
   - `dotnet test`

7. If practical, include a short implementation summary in the PR/change notes:
   - persistence model added,
   - endpoints/handlers added,
   - UI resume behavior added,
   - tests added.

# Risks and follow-ups
- **Unknown existing onboarding design:** The repo may already create the `companies` record at step 1 or only at final completion. The implementation must adapt to the current pattern instead of forcing a new lifecycle.
- **Duplicate state risk:** If both local UI state and server draft state exist, server state should be the source of truth for resume.
- **Validation mismatch:** Draft save should allow partial data, while final completion should enforce required company fields.
- **Tenant/user scoping ambiguity:** Before a company exists, drafts are likely user-scoped; after creation begins, they may need both user and company references.
- **Migration impact:** Adding a new table or JSONB columns may require careful indexing and nullability choices.
- **Future extensibility:** Prefer a draft/session model that can support later onboarding steps like templates, branding, invites, and starter guidance without redesign.

Follow-ups after this task:
- Add industry/business template prefill support if not already implemented.
- Add explicit “Save and exit” / “Resume setup” UX affordances if current UI is minimal.
- Add cleanup/expiration policy for abandoned drafts.
- Add audit events for onboarding start/save/complete if the audit module conventions already exist.
- Add dashboard redirect/starter guidance completion polish for the remaining ST-102 acceptance criteria.