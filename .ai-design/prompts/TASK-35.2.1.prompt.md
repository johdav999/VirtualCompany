# Goal
Implement backlog task **TASK-35.2.1** for story **US-35.2 Deliver customer memory profiles and personalized message generation** by adding the foundational persistence layer for **CustomerMemoryProfiles** in the .NET modular monolith.

This task should deliver the **database model, EF Core entity/configuration, and migration(s)** needed to persist tenant-scoped customer memory profiles and their links to:
- contacts
- deals
- conversations
- engagement attributes

The implementation should be production-ready, align with the existing architecture, and create a clean base for later application/query/UI work required by the acceptance criteria.

# Scope
In scope:
- Add a new persistence model for `CustomerMemoryProfile`
- Add any required supporting child/link entities needed to represent:
  - past conversations
  - previous deals
  - preferences
  - price sensitivity indicators
  - industry signals
  - last outreach summary
  - engagement attributes / score inputs
- Ensure all new tables are **tenant/company scoped**
- Add EF Core configurations, relationships, indexes, and constraints
- Create a migration for PostgreSQL
- Keep naming and schema conventions consistent with the existing solution
- Add minimal domain model updates required for persistence compilation
- If the codebase already has contact/deal/conversation entities, link to them via foreign keys where appropriate
- If some referenced modules are not yet fully implemented, use nullable FK patterns or link tables that preserve forward compatibility without inventing unrelated application behavior

Out of scope:
- UI/profile view implementation
- message generation orchestration
- scheduling/send-prevention logic
- draft editing audit persistence beyond what is strictly necessary for this task
- retrieval/query handlers unless required to make the persistence model usable
- seeding nonessential data

Important implementation intent:
- This task is the **persistence foundation only**, but the schema must clearly support the listed acceptance criteria and future stories.
- Do **not** implement fake/demo data paths.
- Prefer normalized relational design with selective JSONB only where flexibility is clearly beneficial.

# Files to touch
Inspect the solution structure first and then update the appropriate files in the existing conventions. Likely areas:

- `src/VirtualCompany.Domain/...`
  - add `CustomerMemoryProfile` aggregate/entity
  - add supporting entities/value objects if the domain layer owns them
- `src/VirtualCompany.Infrastructure/...`
  - EF Core entity configurations
  - DbContext / model registration
  - migration files
- Existing persistence folders for:
  - contacts
  - deals
  - conversations
  - shared company/tenant-owned entities
- Migration output location used by the project today
- Potentially:
  - `src/VirtualCompany.Application/...` only if compile-time contracts require exposure of the new entity
  - test project(s) if there are migration/model tests already following a pattern

Before coding, locate:
- the main `DbContext`
- current entity configuration pattern
- migration command/process used in repo
- existing `Contact`, `Deal`, and `Conversation` entities/tables
- whether tenant key is named `company_id`, `tenant_id`, or both in code vs DB

# Implementation plan
1. **Inspect existing persistence conventions**
   - Find the primary EF Core `DbContext`
   - Identify naming conventions for:
     - table names
     - primary keys
     - FK naming
     - timestamps
     - soft delete if present
     - tenant/company scoping
   - Confirm whether the system uses `company_id` in DB and `CompanyId` in code
   - Find existing entities for contacts, deals, and conversations so links match real tables rather than guessed names

2. **Design the persistence model**
   Create a schema that supports one persistent memory profile per tenant contact and future personalization workflows.

   Recommended model:
   - `customer_memory_profiles`
     - `id` uuid pk
     - `company_id` uuid fk
     - `contact_id` uuid fk
     - `ai_summary` text null
     - `relationship_memory` text null
     - `last_outreach_summary` text null
     - `engagement_score` numeric(...) null
     - `preferences_json` jsonb null or normalized child table if repo conventions strongly prefer relational
     - `price_sensitivity_json` jsonb null or normalized child table
     - `industry_signals_json` jsonb null or normalized child table
     - `created_at`
     - `updated_at`
     - unique index on `(company_id, contact_id)`

   Supporting tables, preferred if consistent with current modeling:
   - `customer_memory_profile_conversations`
     - link profile to conversation records
     - include optional metadata such as `summary`, `last_message_at`, `relevance`, `created_at`
   - `customer_memory_profile_deals`
     - link profile to deal records
     - include optional metadata such as `deal_role`, `outcome`, `closed_at`, `created_at`
   - `customer_memory_profile_engagement_attributes`
     - structured engagement signals/attributes
     - e.g. `attribute_type`, `attribute_key`, `attribute_value`, `score_impact`, `observed_at`, `metadata_json`
   - Optional normalized tables if appropriate:
     - `customer_memory_profile_preferences`
     - `customer_memory_profile_price_signals`
     - `customer_memory_profile_industry_signals`

   Guidance:
   - Prefer normalized child tables for repeated/time-based signals
   - Prefer JSONB for compact summary blobs only if the codebase already uses JSONB for flexible profile attributes
   - Ensure the model can support future “same offer lookback” checks by preserving links to prior deals/conversations/campaign-related history, even if campaign tables are not part of this task

3. **Add domain entities**
   - Add the new entity/entities in the domain layer using existing base classes/interfaces if present
   - Include navigation properties only where consistent with current style
   - Keep behavior minimal; this task is persistence-first
   - Ensure one-to-one or one-to-many cardinality is explicit:
     - one contact -> one customer memory profile per company
     - one profile -> many linked conversations
     - one profile -> many linked deals
     - one profile -> many engagement attributes/signals

4. **Add EF Core configurations**
   - Create configuration classes for each new entity
   - Map PostgreSQL types correctly:
     - `uuid`
     - `text`
     - `jsonb`
     - `timestamp with time zone` / existing timestamptz convention
     - numeric precision for engagement score
   - Add:
     - PKs
     - FKs
     - unique constraint on `(company_id, contact_id)`
     - indexes on:
       - `company_id`
       - `contact_id`
       - `(company_id, contact_id)`
       - linked `conversation_id`
       - linked `deal_id`
       - engagement attribute lookup columns as appropriate
   - Configure delete behavior carefully:
     - avoid cascade chains that could accidentally remove historical memory
     - prefer `Restrict`/`NoAction` unless the repo has a clear standard
   - Ensure all child rows are tenant-safe; if child tables do not include `company_id`, justify via parent FK only. If repo standards require direct tenant scoping on every tenant-owned table, include `company_id` on child tables too.

5. **Register entities in DbContext**
   - Add `DbSet<>` entries if the project uses them
   - Register configurations in model builder
   - Verify no duplicate configuration or naming collisions

6. **Create migration**
   - Generate an EF Core migration with a clear name, e.g.:
     - `AddCustomerMemoryProfiles`
     - or `AddCustomerMemoryProfilesAndLinks`
   - Review generated migration manually
   - Ensure PostgreSQL-specific column types and indexes are correct
   - If needed, hand-edit migration for:
     - JSONB
     - filtered/unique indexes
     - FK delete behavior
   - Confirm migration ordering and snapshot updates are correct

7. **Preserve forward compatibility for acceptance criteria**
   Shape the schema so later tasks can implement:
   - customer profile view using production data
   - personalized message generation with memory context
   - duplicate-offer prevention via historical checks
   - audit persistence of generated vs edited drafts

   Concretely:
   - include durable summary fields
   - include historical linked records rather than only denormalized text
   - include timestamps for recency/lookback logic
   - include extensible metadata where future campaign/deal checks may need it

8. **Add or update tests if a pattern exists**
   If the repo already has persistence/model tests:
   - add tests for model constraints or migration application
   - verify unique profile per `(company, contact)`
   - verify required FKs and indexes exist if tested in current style

   If no such pattern exists, do not invent a large new test harness just for this task.

9. **Document assumptions in code comments only where necessary**
   - Keep comments sparse and useful
   - Do not add speculative architecture docs unless the repo already keeps migration notes nearby

# Validation steps
1. Restore and build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify migration compiles and is included in the infrastructure project:
   - inspect generated migration and model snapshot
   - ensure no broken references to missing entities/tables

4. Validate schema design manually against acceptance criteria:
   - one persistent profile per contact per tenant
   - stores summaries and relationship memory
   - links to conversations
   - links to deals
   - supports engagement attributes
   - supports future personalization and lookback checks

5. If local migration execution is supported in the repo, apply it to a dev database and verify:
   - tables created successfully
   - unique constraint on `(company_id, contact_id)`
   - FK relationships resolve correctly
   - indexes exist as expected

6. Sanity-check delete/update behavior:
   - deleting a conversation/deal/contact should not silently destroy audit-relevant memory unless that is an established repo rule
   - confirm FK behavior matches historical retention goals

# Risks and follow-ups
- **Unknown existing CRM schema:** Contacts and deals may not yet exist or may use different names. If so, adapt to actual entities/tables in the repo and avoid inventing parallel concepts unless absolutely necessary.
- **Tenant key mismatch:** Architecture references `company_id`; task language says tenant. Use the repo’s actual tenant-scoping convention consistently.
- **Over-modeling risk:** Do not build full campaign/message draft persistence here. Only ensure the schema can support those later tasks.
- **Cascade delete risk:** Historical customer memory is audit-relevant; careless cascade rules could erase important context.
- **JSONB vs normalized tradeoff:** Prefer the repo’s established pattern. If uncertain, use normalized tables for repeated signals and JSONB only for flexible summaries/metadata.
- **Future follow-up likely needed:** query handlers/API endpoints for profile view, message draft persistence, and duplicate-offer lookback enforcement will probably be separate tasks.

Deliverable expectation:
- clean compile
- migration checked in
- new persistence model integrated into the existing architecture
- no placeholder hacks
- no unrelated refactors