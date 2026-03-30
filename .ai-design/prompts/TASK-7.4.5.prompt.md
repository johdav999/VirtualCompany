# Goal
Implement backlog task **TASK-7.4.5 — Separate technical logs from business audit events** for story **ST-104 Baseline platform observability and operational safeguards**.

The coding agent should make the platform’s observability design explicit and enforceable by:
- keeping **technical/operational logging** in the application logging pipeline,
- keeping **business audit events** in a dedicated domain/application path and persistence model,
- preventing accidental use of app logs as a substitute for audit history,
- aligning with the architecture note that **auditability is a domain feature, not just logging**.

This task should produce a minimal but solid foundation that supports current observability work without overbuilding future audit UX.

# Scope
In scope:
- Inspect the current solution structure and existing logging/audit-related code.
- Introduce or refine clear abstractions separating:
  - **technical logs**: diagnostics, exceptions, retries, dependency failures, health/worker telemetry,
  - **business audit events**: actor/action/target/outcome/rationale/data-source style records.
- Ensure application and infrastructure code paths use the correct abstraction.
- Add or update persistence model(s) for business audit events if missing.
- Add developer-facing guidance in code/comments/README-level docs where useful.
- Add tests covering the separation behavior.

Out of scope:
- Full audit UI/views from ST-602.
- Comprehensive audit event taxonomy for all modules.
- External SIEM/export integrations.
- Reworking the entire logging stack unless required for clean separation.
- Implementing every future audit event producer across the whole product.

If the repo already contains partial implementations, prefer **refactor and standardize** over duplicate systems.

# Files to touch
Inspect first, then update the relevant files you actually find. Likely areas include:

- `README.md`
- `src/VirtualCompany.Api/Program.cs`
- `src/VirtualCompany.Api/**` for middleware, exception handling, correlation/tenant logging enrichment, DI registration
- `src/VirtualCompany.Application/**` for audit service interfaces, commands, behaviors, use cases
- `src/VirtualCompany.Domain/**` for audit event domain models/value objects if domain-owned
- `src/VirtualCompany.Infrastructure/**` for persistence, EF Core configurations, repositories, logging adapters
- `src/VirtualCompany.Shared/**` only if shared contracts/constants are appropriate
- Test projects under `tests/**` or existing `*.Tests` projects for unit/integration coverage

Potential concrete artifacts to add/update, depending on current codebase:
- `IAuditEventWriter` / `IAuditEventRepository`
- `AuditEvent` entity and EF configuration
- logging helper/enricher abstractions
- middleware or pipeline behavior for correlation/tenant context
- migration(s) for `audit_events` table if not already present
- tests validating that technical logs do not create audit rows and audit writes do not depend on logger output

# Implementation plan
1. **Assess the current implementation**
   - Search for:
     - `ILogger`
     - `Audit`
     - `audit_events`
     - exception middleware
     - worker retry/failure logging
     - correlation ID / tenant context enrichment
   - Identify whether business-significant actions are currently being written only to logs, or whether an audit persistence path already exists.
   - Document the current state in your working notes and preserve backward compatibility where practical.

2. **Define the separation contract**
   - Establish a simple rule set in code:
     - `ILogger<T>` and related logging pipeline = **technical logs only**
     - dedicated audit abstraction/service/repository = **business audit events only**
   - Add a focused interface such as:
     - `IAuditEventWriter`
     - or `IAuditService`
   - The abstraction should accept structured business fields aligned with the architecture/backlog, e.g.:
     - `companyId`
     - `actorType`, `actorId`
     - `action`
     - `targetType`, `targetId`
     - `outcome`
     - `rationaleSummary` (optional)
     - `dataSources` / metadata (optional)
     - timestamp
   - Keep the contract small and implementation-friendly.

3. **Implement or refine the business audit persistence path**
   - If `audit_events` persistence does not exist, add it in the appropriate layer.
   - Use the architecture guidance for the audit schema as the baseline, but keep implementation proportional to this task.
   - At minimum, persist enough fields to distinguish audit records from logs and support future ST-602 work.
   - Add EF Core configuration and migration if the project uses EF Core migrations.
   - Ensure tenant scoping via `company_id` for tenant-owned audit records.

4. **Keep technical logging in the logging pipeline**
   - Review API middleware, background workers, and infrastructure services.
   - Ensure technical concerns remain logged via `ILogger`, including:
     - unhandled exceptions,
     - dependency failures,
     - retries,
     - health-check-related diagnostics,
     - background job failures.
   - Do **not** persist these as business audit events unless there is a clear business-significant action that warrants both.
   - If needed, improve message templates to make the distinction obvious.

5. **Refactor misuse points**
   - Replace any code that currently writes business audit information only through `ILogger` with calls to the audit abstraction.
   - Replace any code that incorrectly stores technical operational noise in audit persistence with normal logging.
   - Where a single operation needs both:
     - write a technical log for diagnostics,
     - write a business audit event for the user/compliance-facing record,
     - keep them as separate calls with separate intent.

6. **Wire dependency injection and boundaries**
   - Register the audit writer/service in DI from API startup/composition root.
   - Keep infrastructure implementation behind application/domain abstractions.
   - Avoid leaking EF or logging implementation details into domain logic.

7. **Add guardrails for future developers**
   - Add concise comments or README notes clarifying:
     - when to use `ILogger`
     - when to use the audit service
     - examples of each
   - If there is an architecture or contributing doc section, update it briefly rather than adding large documentation.

8. **Add tests**
   - Add unit/integration tests for the separation behavior. Prefer the strongest tests the repo supports.
   - Cover scenarios such as:
     - writing a business audit event persists to the audit store with tenant context,
     - technical exception logging does not create an audit record,
     - a business action can emit both a technical log and an audit event without coupling,
     - missing tenant/business context is handled safely according to existing conventions.

9. **Preserve observability requirements from ST-104**
   - Ensure this task does not regress:
     - structured logging,
     - correlation IDs,
     - tenant context in logs where applicable,
     - safe exception handling,
     - worker failure logging/retryability.

10. **Keep the implementation minimal and production-appropriate**
   - Do not build a full event bus or audit UI.
   - Do not introduce a second logging framework unless already present.
   - Favor small, explicit abstractions and clear naming.

# Validation steps
Run the relevant commands after implementation:

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. If migrations are used, verify:
   - migration adds/updates audit persistence cleanly,
   - app starts with the updated schema,
   - no unrelated schema drift is introduced.

4. Manually validate key behavior:
   - Trigger a technical failure path and confirm it is logged through the normal logging pipeline.
   - Trigger a representative business-significant action that writes an audit event and confirm it is persisted separately.
   - Confirm the two records are not conflated.

5. Review code for boundary correctness:
   - no business audit persistence hidden inside generic logging helpers,
   - no operational exception spam being stored as audit history,
   - tenant/company context applied to audit records where required.

# Risks and follow-ups
- **Risk: existing code may already mix concerns heavily.**
  - Follow-up: refactor incrementally and standardize on one audit abstraction rather than trying to fix every module at once.

- **Risk: no current audit table/entity exists.**
  - Follow-up: add a minimal schema now that supports future ST-602 without overcommitting to a final audit model.

- **Risk: developers may continue misusing `ILogger` for business history.**
  - Follow-up: add concise guidance and, if appropriate, a small application service pattern/example to copy.

- **Risk: some events legitimately need both technical logs and business audit records.**
  - Follow-up: document this explicitly so dual-write cases are intentional, not accidental duplication.

- **Risk: tenant context may be available in API flows but weaker in background workers.**
  - Follow-up: verify worker-originated audit events and logs carry the correct company/correlation context.

- **Suggested next backlog follow-up:**
  - expand audit event producers in task/workflow/approval/tool execution flows,
  - add query APIs and UI for ST-602 audit trail and explainability views,
  - define a canonical audit event taxonomy and retention policy.