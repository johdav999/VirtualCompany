# Goal
Implement backlog task **TASK-26.2.3 — Add unmatched bank transaction classification state and posting exclusion rules** for story **US-26.2 Handle unmatched bank transactions and complete migration and backfill support for cash posting**.

Deliver a production-ready change in the existing .NET modular monolith that:

- adds the required database migration(s) and schema objects to link **payments**, **bank transactions**, and **journal entries**
- adds a safe **backfill job** for existing tenant/company finance data
- persists unmatched bank transactions with an explicit classification/status
- prevents unmatched bank transactions from generating AR/AP settlement journal entries until they are matched or manually classified
- records operational counts per company for migrated, backfilled, skipped, and conflict records
- is idempotent enough to run against an already onboarded dataset without creating duplicate posting links

Use the existing architecture and conventions in this repository. Prefer minimal, cohesive changes over speculative redesign.

# Scope
In scope:

- database schema changes for linkage and traceability between finance entities
- any required enum/status additions for unmatched bank transaction classification
- mapping/link tables and foreign keys needed for traceability
- migration code and any seed/default handling required by the migration
- a backfill worker/job/service that initializes historical records safely
- posting rule changes so unmatched bank transactions are excluded from AR/AP settlement posting
- structured operational logging per company with counts:
  - migrated records
  - backfilled records
  - skipped records
  - posting conflicts
- tests covering migration-safe behavior, backfill idempotency, and posting exclusion rules

Out of scope unless required to satisfy acceptance criteria:

- UI changes
- mobile changes
- broad accounting redesign
- unrelated refactors
- introducing new infrastructure beyond current stack

Assumptions to verify from code before implementation:

- existing finance entities for bank transactions, payments, and journal entries already exist
- there is an existing posting pipeline/service for AR/AP settlement generation
- there is an existing background job framework or hosted service pattern
- migrations are managed in the Infrastructure layer with PostgreSQL

If naming in code differs from backlog wording, align with existing domain terminology and document the mapping in code comments or PR notes.

# Files to touch
Inspect first, then update the actual matching files you find. Likely areas:

- `src/VirtualCompany.Domain/**`
  - finance entities/value objects/enums for bank transaction classification/status
  - domain rules around posting eligibility
- `src/VirtualCompany.Application/**`
  - commands/handlers/services for posting and backfill orchestration
  - interfaces for finance repositories or posting services
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations
  - migrations
  - repositories
  - background jobs/hosted services
  - logging and persistence wiring
- `src/VirtualCompany.Api/**`
  - DI registration if needed for backfill job or finance services
- `tests/**`
  - unit tests for posting exclusion logic
  - integration tests for migration/backfill behavior if test infrastructure exists

Also inspect:

- `README.md`
- `docs/postgresql-migrations-archive/README.md`

Likely concrete file categories to touch:

- finance entity files for `BankTransaction`, `Payment`, `JournalEntry`, or equivalents
- enum file(s) for transaction classification/status
- EF configuration files
- DbContext and migration snapshot files
- posting service / settlement generation service
- background worker / job runner files
- test fixtures and finance posting tests

# Implementation plan
1. **Discover current finance model and migration conventions**
   - Find the existing entities/tables for:
     - bank transactions
     - payments
     - journal entries
     - AR/AP settlements or posting records
   - Identify how tenant/company scoping is represented (`company_id` expected).
   - Identify how migrations are created and applied in this repo.
   - Identify the current posting flow that creates settlement journal entries from bank/payment activity.
   - Identify whether there is already a traceability or posting-link concept to extend rather than duplicate.

2. **Design the minimal schema extension**
   Add only what is needed to satisfy acceptance criteria. Prefer explicit relational links over opaque JSON.

   Expected schema additions:
   - explicit unmatched classification/status on bank transactions, e.g.:
     - `Unmatched`
     - `Matched`
     - `ManuallyClassified`
     - optionally `Ignored`/`Excluded` only if existing domain requires it
   - linkage fields and/or mapping tables connecting:
     - payment ↔ bank transaction
     - bank transaction ↔ journal entry
     - payment ↔ journal entry if required for traceability and dedupe
   - uniqueness constraints/indexes to prevent duplicate posting links
   - foreign keys ordered to allow safe backfill

   Prefer a dedicated mapping table when cardinality is not strictly 1:1 or when historical traceability matters. Add indexes for:
   - company/tenant scoping
   - lookup by source entity
   - uniqueness for posting link dedupe

3. **Implement domain model changes**
   - Add/update enum(s) or constants for bank transaction classification state.
   - Ensure new records can explicitly represent unmatched state.
   - Add domain helper/property/method for posting eligibility, e.g. unmatched transactions are not settlement-postable.
   - Keep logic centralized in domain/application service, not scattered in handlers.

4. **Implement EF Core configuration and migration**
   - Update entity configurations with:
     - new columns
     - FK relationships
     - mapping tables
     - indexes and unique constraints
   - Create migration(s) that:
     - add nullable linkage columns/tables first if needed
     - backfill-safe defaults for unmatched status
     - then enforce constraints where safe
   - Be careful with existing onboarded datasets:
     - avoid non-null additions without defaults/backfill strategy
     - avoid FK creation before data is valid
   - If needed, split into multiple migration steps for safety.

5. **Implement backfill job**
   Create a backfill process that runs per company and is safe on existing data.

   Requirements:
   - initialize traceability links for historical finance data where deterministically possible
   - initialize unmatched-state records/status for existing bank transactions lacking classification
   - avoid FK violations by ordering writes correctly
   - avoid duplicate posting links via:
     - existence checks
     - unique constraints
     - idempotent upsert-style behavior where appropriate
   - log per-company counts:
     - migrated
     - backfilled
     - skipped
     - conflicts

   Suggested behavior:
   - iterate company by company
   - process in batches if datasets may be large
   - use correlation IDs / structured logging
   - classify ambiguous historical records as unmatched rather than forcing links
   - record conflicts when multiple candidate links exist or uniqueness would be violated

6. **Update posting/settlement generation rules**
   - Find the AR/AP settlement journal entry generation path.
   - Add a guard so unmatched bank transactions do **not** generate settlement journal entries.
   - Allow posting only when transaction is:
     - matched to the relevant payment, or
     - manually classified in a way the domain explicitly allows
   - Ensure this rule applies both to new processing and any replay/rebuild path.
   - Preserve existing behavior for already matched/classified transactions.

7. **Add operational logging**
   - Use structured logs, not only free-form strings.
   - Include company/tenant context and job correlation ID.
   - Emit summary logs per company and overall run.
   - Include conflict/skipped reasons where useful but avoid noisy per-row logs unless debug level.

   Example fields:
   - `CompanyId`
   - `JobName`
   - `MigratedCount`
   - `BackfilledCount`
   - `SkippedCount`
   - `PostingConflictCount`

8. **Add tests**
   Add the highest-value tests first.

   Minimum expected coverage:
   - unmatched bank transaction is persisted with explicit unmatched status
   - unmatched bank transaction does not generate AR/AP settlement journal entry
   - matched or manually classified transaction can proceed through posting path
   - backfill can run on existing seeded dataset without FK violations
   - backfill is idempotent and does not create duplicate posting links
   - conflict cases are skipped/logged rather than crashing the whole company run

   Prefer:
   - unit tests for posting eligibility logic
   - integration tests for repository/migration/backfill behavior if test infra supports PostgreSQL or realistic persistence

9. **Keep implementation aligned with modular monolith boundaries**
   - Domain: statuses/rules
   - Application: orchestration/use cases
   - Infrastructure: persistence, migrations, background execution
   - API: registration only if needed

10. **Document assumptions in code**
   If you must infer matching logic for historical records, make it explicit in comments and keep it conservative:
   - exact reference match first
   - deterministic amount/date/company match only if already established in codebase
   - otherwise mark unmatched and skip posting link creation

# Validation steps
1. **Static discovery**
   - Confirm actual entity/table names and update implementation accordingly.
   - Confirm migration generation command/pattern used by repo.

2. **Build**
   - Run:
     - `dotnet build`

3. **Tests**
   - Run:
     - `dotnet test`

4. **Migration validation**
   - Generate/apply the migration locally against a representative existing database or test fixture.
   - Verify:
     - new columns/tables exist
     - FK constraints apply successfully
     - indexes/unique constraints are present
     - existing data remains queryable

5. **Backfill validation**
   - Execute the backfill job against an existing onboarded company dataset or seeded equivalent.
   - Verify:
     - unmatched status is initialized where needed
     - traceability links are created only once
     - no FK violations occur
     - rerunning the job does not create duplicates
     - per-company logs include migrated/backfilled/skipped/conflict counts

6. **Posting rule validation**
   - Create/seed scenarios for:
     - unmatched bank transaction
     - matched bank transaction
     - manually classified bank transaction
   - Verify:
     - unmatched does not create AR/AP settlement journal entries
     - matched/manual classification does create entries when otherwise valid
     - duplicate posting links are prevented

7. **Regression validation**
   - Verify no unrelated posting flows break for already linked transactions.
   - Verify tenant/company scoping is preserved in queries and backfill processing.

8. **Final check against acceptance criteria**
   Explicitly confirm in your implementation notes or PR summary:
   - migration adds all required linkage fields and mapping tables
   - backfill initializes traceability and unmatched-state records safely
   - unmatched transactions persist explicit status and are excluded from settlement posting
   - migration/backfill succeed on existing data without duplicate posting links
   - logs record required counts per company

# Risks and follow-ups
- **Risk: ambiguous historical matching**
  - Historical data may not support deterministic linking between payments and bank transactions.
  - Mitigation: prefer conservative unmatched classification and log conflicts/skips.

- **Risk: FK ordering during migration/backfill**
  - Adding strict constraints too early can break onboarded datasets.
  - Mitigation: use phased migration and backfill-safe nullability/defaults before tightening constraints.

- **Risk: duplicate links from reruns or retries**
  - Backfill jobs and posting retries can create duplicate mappings.
  - Mitigation: add unique constraints and idempotent existence checks.

- **Risk: posting logic scattered across multiple services**
  - Exclusion rule may need to be enforced in more than one path.
  - Mitigation: centralize eligibility checks and search for all settlement creation entry points.

- **Risk: large tenant datasets**
  - Backfill may be expensive or lock-heavy.
  - Mitigation: process per company in batches and avoid long-running transactions where possible.

Follow-ups if not fully covered by this task:
- admin/reporting visibility for unmatched/manual-classification states
- operator tooling to review and resolve posting conflicts
- richer audit/business event persistence for backfill outcomes beyond technical logs
- replay/reconciliation tooling for transactions that move from unmatched to matched later