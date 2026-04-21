# Goal
Implement backlog task **TASK-26.2.2 — Implement backfill routine for existing payments, bank transactions, and posting state** for story **US-26.2 Handle unmatched bank transactions and complete migration and backfill support for cash posting**.

The coding agent should deliver:
- a **database migration** that adds the linkage fields and mapping tables needed to connect **payments**, **bank transactions**, and **journal entries**
- a **backfill routine/job** that initializes traceability and unmatched-state records for existing tenant finance data
- logic ensuring **unmatched bank transactions** are explicitly persisted as unmatched and **do not create AR/AP settlement journal entries**
- **idempotent execution** with no duplicate posting links
- **operational logging per company** with counts for migrated, backfilled, skipped, and conflict records

Work within the existing **.NET modular monolith** and **PostgreSQL** setup. Favor tenant-safe, idempotent, FK-safe migration/backfill behavior over broad refactors.

# Scope
In scope:
- Inspect the current finance/accounting persistence model and identify existing entities/tables for:
  - payments
  - bank transactions
  - journal entries / journal lines
  - posting state / settlement / reconciliation-related records
- Add schema support for:
  - linkage from payments to bank transactions and/or posting artifacts
  - linkage from bank transactions to journal entries and/or posting artifacts
  - mapping/bridge tables where many-to-many or traceability history is required
  - explicit unmatched status/state for bank transactions
  - uniqueness constraints/indexes to prevent duplicate posting links
- Implement a backfill process that:
  - runs safely against existing onboarded company datasets
  - creates missing traceability/mapping/unmatched-state records
  - respects tenant boundaries
  - avoids FK violations by ordering inserts/updates correctly
  - is idempotent and conflict-aware
- Add operational logging/telemetry for per-company counts:
  - migrated records
  - backfilled records
  - skipped records
  - posting conflicts
- Add or update tests covering migration/backfill behavior and unmatched transaction rules

Out of scope unless required by existing code structure:
- UI changes
- broad redesign of finance domain behavior unrelated to migration/backfill
- introducing new infrastructure platforms
- changing unrelated posting workflows beyond what is necessary to enforce unmatched behavior

Implementation constraints:
- Preserve existing data.
- Do not create AR/AP settlement journal entries for unmatched bank transactions.
- Prefer additive schema changes and backward-compatible rollout.
- Ensure rerunning the backfill does not create duplicate links or duplicate unmatched-state rows.

# Files to touch
The agent should first discover the actual finance module layout, then modify the relevant files. Likely areas:

- `src/VirtualCompany.Domain/**`
  - finance/accounting domain entities, enums, value objects
  - status enums for bank transaction matching/posting state
- `src/VirtualCompany.Application/**`
  - backfill job command/handler/service
  - orchestration for per-company execution
  - logging/result DTOs
- `src/VirtualCompany.Infrastructure/**`
  - EF Core entity configurations or SQL persistence mappings
  - migrations
  - repositories/query services used by the backfill
  - background job registration/execution
- `src/VirtualCompany.Api/**`
  - startup/DI registration if the backfill is exposed as hosted job, admin endpoint, or startup task
- `tests/**`
  - migration/integration tests
  - application tests for idempotent backfill behavior
  - unmatched transaction posting tests

Also inspect:
- `README.md`
- `docs/postgresql-migrations-archive/README.md`

If present, prioritize touching files related to:
- finance ledger/posting
- bank transaction ingestion/reconciliation
- payment persistence
- EF Core `DbContext`
- migration folders under Infrastructure

# Implementation plan
1. **Discover the current finance model before coding**
   - Search the solution for:
     - `Payment`
     - `BankTransaction`
     - `JournalEntry`
     - `Posting`
     - `Settlement`
     - `Reconciliation`
     - `Match`
   - Identify:
     - current table names
     - tenant/company key conventions
     - existing foreign keys
     - current posting flow for AR/AP settlement entries
     - how migrations are authored in this repo
   - Summarize the discovered model in code comments or PR notes through clear naming, not extra docs unless needed.

2. **Design the additive schema changes**
   - Add the minimum schema required to satisfy acceptance criteria. Depending on current model, this will likely include:
     - nullable FK/link columns on payments and/or bank transactions
     - one or more mapping tables for traceability, such as:
       - payment-to-bank-transaction link
       - payment/bank-transaction to journal-entry posting link
       - posting traceability/history table if current model lacks one
     - explicit status column for bank transaction matching/posting state, with an `Unmatched` value
   - Add indexes and uniqueness constraints to prevent duplicate posting links, for example:
     - unique per `(company_id, payment_id, bank_transaction_id)` where appropriate
     - unique per `(company_id, source_type, source_id, journal_entry_id)` if using a generic posting-link table
   - Keep nullability/backward compatibility in mind so migration can apply to existing data before backfill runs.

3. **Create the database migration**
   - Implement the migration in the project’s standard migration mechanism.
   - Ensure migration order is FK-safe:
     - create new tables first
     - add indexes/constraints
     - add FK columns
     - only then add foreign keys if data shape allows
   - If needed, use staged migration patterns:
     - add nullable columns/tables
     - backfill
     - optionally tighten constraints only if safe and already consistent
   - Do not make assumptions that all existing records have valid matches.

4. **Implement explicit unmatched-state handling**
   - Introduce or extend a domain/application enum/status for bank transactions to include explicit unmatched state.
   - Ensure existing posting logic treats unmatched bank transactions as:
     - persisted records
     - not eligible for AR/AP settlement journal entry generation
   - Add guard clauses in posting/settlement services so unmatched records are skipped and counted/logged rather than posted.

5. **Implement the backfill routine/job**
   - Build a dedicated application service/background job, e.g.:
     - `BackfillCashPostingTraceabilityJob`
     - `InitializePaymentBankTransactionPostingStateHandler`
   - Process data **per company** to preserve tenant isolation and produce per-company logs.
   - For each company:
     - load existing payments, bank transactions, and relevant journal entries in batches
     - infer existing links from current data where possible
     - create missing mapping/traceability rows
     - initialize unmatched-state rows/status for bank transactions with no valid match
     - skip records that are already linked/backfilled
     - detect ambiguous/conflicting cases and log them as conflicts without creating duplicate or invalid links
   - Make the routine idempotent:
     - check for existing links before insert
     - use unique constraints plus conflict-safe insert patterns where appropriate
     - avoid generating duplicate journal/posting links on rerun
   - Ensure FK-safe sequencing:
     - only create link rows when both referenced records exist
     - if a referenced record is missing or ambiguous, skip and log conflict/skipped reason

6. **Add operational logging**
   - Emit structured logs with company context and counts:
     - migrated records
     - backfilled records
     - skipped records
     - posting conflicts
   - If the codebase has an audit/operations pattern, follow it.
   - At minimum, log:
     - company id
     - job correlation/run id if available
     - counts by entity/link type
     - notable conflict reasons
   - Keep logs operational and concise.

7. **Protect against duplicate posting links**
   - Enforce this in both schema and code:
     - DB unique constraints/indexes
     - application-level existence checks
   - Handle duplicate attempts gracefully:
     - treat as already processed/skipped where possible
     - count true data inconsistencies as conflicts

8. **Add tests**
   - Add tests that prove:
     - migration applies to an existing-style dataset
     - backfill creates required links/state without FK violations
     - unmatched bank transactions are marked unmatched
     - unmatched bank transactions do not generate AR/AP settlement journal entries
     - rerunning backfill is idempotent and does not create duplicate posting links
     - per-company processing/logging paths work as expected
   - Prefer integration tests around persistence if the repo supports them; otherwise combine application tests with repository-level tests.

9. **Keep implementation aligned with architecture**
   - Respect modular boundaries:
     - domain types in Domain
     - orchestration/use cases in Application
     - persistence/migrations in Infrastructure
   - Do not let API/UI own business logic.
   - Keep tenant scoping explicit in all queries and writes.

# Validation steps
1. **Inspect and build**
   - Run:
     - `dotnet build`
   - Fix compile issues across Domain/Application/Infrastructure/API/tests.

2. **Run tests**
   - Run:
     - `dotnet test`
   - Ensure all existing tests pass plus new tests for this task.

3. **Migration validation**
   - Apply the new migration against a local/dev PostgreSQL database or the project’s test database setup.
   - Verify:
     - new columns/tables exist
     - foreign keys are valid
     - unique constraints/indexes exist
     - migration succeeds on a schema representing an already onboarded company

4. **Backfill validation on seeded/existing-style data**
   - Prepare or reuse a dataset containing:
     - existing payments
     - existing bank transactions
     - existing journal entries
     - some matched cases
     - some unmatched bank transactions
     - some ambiguous/conflicting cases if possible
   - Execute the backfill routine.
   - Verify:
     - required traceability/mapping rows are created
     - unmatched bank transactions are explicitly marked unmatched
     - no FK violations occur
     - no duplicate posting links are created

5. **Idempotency validation**
   - Run the backfill a second time on the same dataset.
   - Verify:
     - no duplicate link rows
     - counts reflect skipped/already-processed records appropriately
     - no extra settlement journal entries are created

6. **Posting behavior validation**
   - Execute or simulate the posting flow for unmatched bank transactions.
   - Verify:
     - unmatched transactions remain persisted
     - AR/AP settlement journal entries are not generated until matched or manually classified

7. **Logging validation**
   - Confirm structured logs include per-company counts for:
     - migrated
     - backfilled
     - skipped
     - conflicts

# Risks and follow-ups
- **Unknown current finance schema**
  - The exact entities/table names may differ from assumptions. Discover first and adapt the design to the existing model rather than forcing generic names.

- **Ambiguous historical data**
  - Existing onboarded datasets may not contain enough information to infer deterministic links between payments, bank transactions, and journal entries.
  - In such cases, prefer explicit unmatched/conflict records and logging over risky auto-linking.

- **Constraint timing**
  - Tight non-null or FK constraints added too early can break migration on existing data.
  - Use staged additive migration/backfill patterns.

- **Duplicate legacy records**
  - Historical duplicates may surface when adding uniqueness constraints.
  - If encountered, handle by:
    - detecting duplicates during backfill
    - logging conflicts
    - avoiding destructive cleanup unless clearly safe and already supported

- **Performance on large tenants**
  - Backfill should batch by company and by record ranges to avoid loading all finance data into memory.
  - If needed, add pagination/chunking and transaction boundaries per batch.

- **Operational execution model**
  - If there is no existing job runner/admin trigger for one-off backfills, implement the smallest consistent mechanism with the current architecture.
  - Follow-up may be needed for a reusable migration/backfill operations framework.

- **Follow-up recommendation**
  - After implementation, consider a separate task for:
    - admin observability/reporting UI for backfill runs
    - manual conflict resolution workflow for ambiguous historical matches
    - stronger reconciliation domain modeling if current finance structures are still immature