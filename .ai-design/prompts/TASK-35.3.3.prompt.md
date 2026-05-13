# Goal
Implement backlog task **TASK-35.3.3** for story **US-35.3 Add automation policies, approval controls, and website lead capture entrypoints** by adding a production-ready **website lead capture API** in the existing .NET modular monolith.

The implementation must:
- Accept public website lead submissions through an ASP.NET Core API endpoint.
- Resolve the correct tenant/company for the submission.
- Validate and normalize incoming payloads.
- Apply deduplication by email within a tenant-configured deduplication window.
- Create or update the lead under the correct tenant.
- Enroll the lead into a configured follow-up sequence.
- Ensure enrollment happens within **1 minute** via synchronous orchestration or reliable background processing.
- Integrate with existing/adjacent automation policy concepts so future sequence execution can enforce outbound and approval policies.
- Persist audit-friendly metadata and decision context where appropriate.

Keep the implementation aligned with the architecture:
- ASP.NET Core modular monolith
- PostgreSQL transactional persistence
- shared-schema multi-tenancy with `company_id`
- CQRS-lite application layer
- outbox/background worker for reliable side effects where needed
- auditability as a domain feature

# Scope
Implement only what is necessary to satisfy this task and its acceptance criteria slice for website lead capture, while wiring clean extension points for the broader policy/approval system.

In scope:
- Public API contract for website lead capture.
- Tenant resolution strategy for public submissions.
- Request validation and normalization.
- Deduplication logic for same email within configured window.
- Lead create/update behavior.
- Sequence enrollment trigger and persistence.
- Configuration model needed for:
  - website form tenant resolution
  - deduplication window
  - default follow-up sequence
- Audit/event recording for submission handling.
- Automated tests covering happy path, validation failures, tenant resolution, deduplication, and enrollment dispatch.
- Minimal persistence changes/migrations required.

Also in scope if missing and needed for this task:
- Domain/application entities for leads, website submission source, and sequence enrollment records.
- Background job/outbox hook to guarantee enrollment within 1 minute.
- Idempotency/concurrency protection for duplicate near-simultaneous submissions.

Out of scope unless already partially present and required to complete the flow:
- Full agent control panel UI for policy configuration.
- Full review queue UX.
- Full outbound send execution engine.
- Full approval decision UI/actions.
- Broad CRM feature set beyond what this endpoint needs.
- External email sending.

If broader acceptance criteria are only partially represented in current code, implement the backend primitives and persistence hooks needed by this task without inventing large unrelated UI surfaces.

# Files to touch
Inspect the solution first and then touch the minimum coherent set of files across these likely areas.

Likely projects:
- `src/VirtualCompany.Api`
- `src/VirtualCompany.Application`
- `src/VirtualCompany.Domain`
- `src/VirtualCompany.Infrastructure`
- `tests/VirtualCompany.Api.Tests`

Likely file categories to add/update:
- API endpoint/controller/minimal endpoint registration for public website lead capture
- Request/response DTOs
- Application command + handler for capture flow
- Tenant resolution service/interface
- Validation classes
- Domain entities/value objects for:
  - Lead
  - Lead source / website submission
  - Sequence enrollment
  - Tenant website capture settings
- Infrastructure EF Core/PostgreSQL mappings
- Repository/query implementations
- Migration files
- Background worker/outbox dispatcher integration if enrollment is async
- Audit event creation
- Tests:
  - endpoint tests
  - application/service tests
  - deduplication/concurrency tests

Before coding, identify existing equivalents for:
- company/tenant settings
- workflows/sequences
- approvals/policies
- audit events
- outbox/background jobs
- public API routing conventions
- validation patterns
- persistence/migration approach

Prefer extending existing modules over creating parallel patterns.

# Implementation plan
1. **Survey the current codebase and map existing patterns**
   - Find how the solution currently structures:
     - API endpoints/controllers
     - commands/handlers
     - tenant scoping
     - EF Core entities/configurations
     - migrations
     - background jobs/outbox
     - audit events
   - Search for existing concepts that may already exist under different names:
     - lead/contact/prospect
     - sequence/cadence/workflow/follow-up
     - company settings/policy settings
     - approval policy / outbound policy
   - Reuse existing abstractions wherever possible.

2. **Define the public website lead capture contract**
   - Add a public endpoint under a clear route such as:
     - `POST /api/public/website-leads`
     - or existing public API convention if present.
   - Request payload should include enough data to resolve tenant and create/update a lead, for example:
     - tenant key / form key / site key
     - name
     - email
     - phone
     - company
     - message
     - source page/url
     - utm metadata
     - external submission id if available
   - Response should be safe and minimal:
     - accepted/success status
     - lead id if appropriate and not sensitive
     - enrollment accepted status
   - Do not leak tenant existence or internal policy details in public error responses.

3. **Implement tenant resolution for public submissions**
   - Add a tenant resolution mechanism suitable for website forms, preferring one of:
     - explicit public form key mapped to company
     - tenant slug + form key
     - verified host/domain mapping if already supported
   - Make resolution deterministic and testable.
   - Ensure all downstream persistence uses resolved `company_id`.
   - Return safe not-found/invalid responses without exposing internal tenant data.

4. **Add/extend tenant website lead capture settings**
   - Persist settings needed for this flow, ideally in existing company settings JSONB or a dedicated table if the codebase already uses typed settings:
     - website lead capture enabled flag
     - deduplication window
     - default follow-up sequence/workflow id
     - public form key / source mapping
   - If policy settings already exist, align naming and storage style.
   - Keep the model extensible for future outbound policy settings:
     - outbound enabled
     - max emails per day
     - approval requirements for first contact, pricing discussion, follow-ups, re-engagement
   - If those settings are already modeled elsewhere, reference them rather than duplicating.

5. **Model lead and submission persistence**
   - If no lead entity exists, add a minimal lead aggregate/table with fields such as:
     - id
     - company_id
     - email
     - normalized_email
     - full_name
     - phone
     - company_name
     - status
     - source
     - source_details_json
     - created_at / updated_at
     - last_inbound_at
     - active flag/state
   - Add website submission tracking if useful for audit/idempotency:
     - submission id
     - company_id
     - normalized_email
     - payload snapshot
     - received_at
     - deduplication decision
   - Normalize email consistently, e.g. trim + lowercase.

6. **Implement validation and normalization**
   - Add server-side validation for:
     - required tenant/form identifier
     - valid email format
     - max lengths
     - optional phone/company/message constraints
     - anti-abuse basics if existing patterns support it
   - Normalize:
     - email
     - whitespace
     - nullable empty strings
     - source metadata
   - Return field-level validation errors in the project’s standard API format.

7. **Implement deduplication logic**
   - Deduplicate by:
     - same `company_id`
     - same normalized email
     - existing active lead
     - within configured deduplication window
   - Behavior:
     - if duplicate found within window, update existing lead instead of creating a second active lead
     - refresh relevant fields from latest submission
     - update last inbound/submission timestamps
     - record that deduplication occurred
   - If no duplicate in window, create a new lead.
   - Make this concurrency-safe:
     - use transaction boundaries
     - consider unique/partial index or locking strategy where appropriate
     - ensure two simultaneous submissions do not create duplicate active leads

8. **Implement sequence enrollment**
   - After create/update, enroll the lead into the configured follow-up sequence/workflow.
   - Reuse existing workflow/sequence infrastructure if present.
   - If no direct sequence engine exists yet, create a minimal enrollment record and dispatch a background job/event that the existing workflow runner can consume.
   - Guarantee processing within 1 minute:
     - synchronous creation of enrollment record in request path
     - plus outbox/background dispatch for actual execution
   - Ensure duplicate submissions do not create duplicate active enrollments unless intended by business rules.
   - Prefer idempotent enrollment keyed by lead + sequence + active state.

9. **Wire policy/approval compatibility**
   - This task is part of a story that includes automation policies and approval controls.
   - Do not build the full send engine unless already present, but ensure sequence enrollment and future execution can carry policy context.
   - Add structured fields/placeholders where needed so later sequence execution can:
     - check outbound enabled
     - enforce max emails per day
     - require approval for first contact/pricing/follow-ups/re-engagement
     - record policy decision reason visible to review queue/audit trail
   - If policy engine already exists, connect enrollment/execution initiation to it.
   - If not, add a clean interface/contract rather than hardcoding logic into the endpoint.

10. **Create audit and operational records**
   - Record business audit events for:
     - website lead submission received
     - lead created
     - lead updated via deduplication
     - sequence enrollment requested/created
   - Include actor type `system` or `external`
   - Include company/tenant context
   - Store concise rationale/decision summaries, not raw payload dumps unless existing audit policy allows it.
   - Keep technical logs separate from business audit events.

11. **Add persistence configuration and migration**
   - Add EF Core configurations and migration(s).
   - Include indexes for:
     - company_id + normalized_email
     - public form key / tenant resolution key
     - enrollment lookup
     - submission received timestamps if queried
   - If using PostgreSQL-specific features, keep them explicit and migration-safe.

12. **Expose endpoint and secure appropriately**
   - This is a public endpoint, so:
     - allow anonymous access only for this route
     - apply rate limiting if the project already has middleware/hooks
     - avoid tenant data leakage
     - log correlation IDs
   - Ensure internal tenant-scoped authorization is still enforced after tenant resolution for any internal services.

13. **Add tests**
   - API tests for:
     - valid submission creates lead and enrollment
     - invalid payload returns validation errors
     - unknown/invalid tenant key returns safe failure
     - duplicate submission within window updates existing lead
     - submission outside window creates new lead if intended by rules
     - concurrent duplicate submissions do not create two active leads
   - Application/service tests for:
     - normalization
     - deduplication decisioning
     - enrollment idempotency
     - audit event creation
   - If background dispatch is async, test outbox/enqueue behavior rather than timing-based flakiness.

14. **Keep implementation production-minded**
   - Use cancellation tokens.
   - Keep handlers thin and domain/application logic testable.
   - Avoid direct DB access from controllers.
   - Follow existing naming and folder conventions.
   - Add concise code comments only where behavior is non-obvious.

# Validation steps
1. Restore/build the solution:
   - `dotnet build`

2. Run the relevant automated tests:
   - `dotnet test`

3. Add or update tests to verify:
   - public endpoint accepts a valid website lead submission
   - tenant resolution maps submission to the correct company
   - invalid email / missing required fields fail validation
   - duplicate same-email submission within deduplication window updates existing lead
   - no second active lead is created for duplicate within window
   - sequence enrollment record/job is created
   - enrollment path is idempotent for duplicate submissions
   - audit events are persisted for create/update/enrollment
   - concurrent duplicate submissions remain safe

4. If migrations are part of the repo workflow:
   - generate/apply migration
   - verify schema matches entity configuration
   - verify indexes exist for deduplication and tenant resolution paths

5. Manually verify endpoint behavior with representative payloads:
   - new lead
   - duplicate lead
   - bad tenant key
   - malformed email
   - missing sequence configuration if applicable

6. Confirm no regressions in existing modules touched by:
   - tenant scoping
   - workflow/sequence startup
   - audit event persistence
   - background dispatch

# Risks and follow-ups
- **Ambiguous existing domain model**: the codebase may already have lead/contact/workflow concepts under different names. Reconcile carefully instead of duplicating.
- **Tenant resolution design**: if no public form key/domain mapping exists, choose the smallest secure approach and document assumptions.
- **Deduplication race conditions**: concurrent submissions can create duplicates unless transaction/index strategy is solid.
- **Sequence engine maturity**: if follow-up sequences are not fully implemented, create a durable enrollment record plus outbox event rather than fake synchronous behavior.
- **Policy acceptance criteria breadth**: this task references outbound policy and approval controls beyond the endpoint itself. If full backend support is not already present, add extension points and note any remaining gaps explicitly in code comments/PR notes.
- **Public endpoint abuse**: rate limiting, bot protection, and spam controls may need a follow-up task if not already available.
- **PII handling**: ensure logs and audit records do not overexpose raw submission content.
- **Configuration UX gap**: if admin UI for website capture settings or policy settings is not yet implemented, note the backend is ready but UI follow-up may still be required.

Potential follow-up tasks to call out if not already covered:
- admin UI for website form/public key configuration
- review queue UI and approval actions
- sequence execution policy enforcement implementation
- spam protection/captcha
- richer lead source attribution and analytics