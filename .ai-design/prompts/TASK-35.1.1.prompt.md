# Goal
Implement backlog task **TASK-35.1.1** for story **US-35.1 Implement outbound campaigns and multi-step sales sequence execution** by adding the foundational **tenant-scoped database schema and indexes** for:

- `SalesCampaigns`
- `SalesSequences`
- `SalesSequenceSteps`
- `SalesCampaignContacts`

This task is strictly about **persistence foundation** in the existing .NET modular monolith using PostgreSQL and the project’s shared-schema multi-tenant strategy (`company_id` on tenant-owned tables). The implementation must prepare the system for later application, worker, and UI work required by the acceptance criteria, without overreaching into full campaign execution behavior.

# Scope
In scope:

- Add new domain/persistence models and EF Core configuration for the four sales entities.
- Add a migration that creates the tables with:
  - UUID primary keys
  - `company_id` tenant scoping
  - foreign keys to related entities where appropriate
  - timestamps
  - status/state fields needed for future campaign lifecycle and scheduling
- Add indexes optimized for:
  - tenant-scoped reads
  - campaign listing/filtering
  - sequence step ordering
  - campaign contact eligibility/state queries
- Ensure naming and schema conventions match the existing codebase.
- Ensure migration is safe, deterministic, and compatible with PostgreSQL.
- If the project uses a central DbContext registration/configuration pattern, wire the new entities into it.

Out of scope:

- UI pages or forms at `/app/sales/campaigns`
- command/query handlers
- campaign launch logic
- scheduling workers
- reply/deal cancellation automation
- email integration
- approval/policy enforcement logic
- real-time UI updates

Important framing:

- The acceptance criteria describe the broader story, but this task only delivers the **schema substrate** needed by future tasks.
- Do not implement speculative features beyond what is necessary to support the story’s data model and future execution flow.

# Files to touch
Inspect the repository first and then touch only the relevant files. Likely areas include:

- `src/VirtualCompany.Domain/...`
  - add entity classes/value objects/enums if domain entities are modeled here
- `src/VirtualCompany.Infrastructure/...`
  - persistence entities/configurations
  - DbContext
  - EF Core mapping classes
  - migrations folder
- Any shared constants or schema naming helpers already used by persistence

Likely concrete file categories:

- `src/VirtualCompany.Infrastructure/**/DbContext*.cs`
- `src/VirtualCompany.Infrastructure/**/Configurations/*`
- `src/VirtualCompany.Infrastructure/**/Migrations/*`
- `src/VirtualCompany.Domain/**/Sales*.*` or equivalent module folders

Before coding, identify the existing patterns for:

- tenant-owned entities (`company_id`)
- table naming conventions
- timestamp conventions (`created_at`, `updated_at`, optional nullable lifecycle timestamps)
- enum storage strategy (string vs int)
- migration naming and placement
- index naming conventions

# Implementation plan
1. **Discover existing persistence conventions**
   - Inspect the current DbContext, entity configurations, and recent migrations.
   - Confirm:
     - whether entities live in Domain or Infrastructure
     - whether snake_case naming is handled globally or explicitly
     - how UUIDs are generated
     - how audit timestamps are represented
     - how foreign keys to contacts/customers are modeled in the current schema
   - Reuse existing patterns exactly.

2. **Design the minimal schema for outbound campaigns**
   Create a schema that supports the story’s future needs without implementing execution logic now.

   Recommended table responsibilities:

   - **SalesSequences**
     - tenant-owned reusable sequence definition
     - fields such as:
       - `id`
       - `company_id`
       - `name`
       - `description` nullable
       - `status` or `is_active` depending on project conventions
       - `created_at`
       - `updated_at`

   - **SalesSequenceSteps**
     - ordered steps belonging to a sequence
     - fields such as:
       - `id`
       - `company_id`
       - `sales_sequence_id`
       - `step_order`
       - `delay_days`
       - `channel` or `step_type` if the design expects future extensibility; if not, keep focused on email-oriented sequence content
       - `template_subject` nullable if email-specific
       - `template_content`
       - `ai_personalization_enabled`
       - `created_at`
       - `updated_at`
     - enforce uniqueness of `(company_id, sales_sequence_id, step_order)`

   - **SalesCampaigns**
     - tenant-owned campaign instance referencing a sequence
     - fields such as:
       - `id`
       - `company_id`
       - `name`
       - `audience_type` or equivalent discriminator for existing contacts / past customers / imported lists
       - `sales_sequence_id`
       - `status` for draft/active/paused/stopped/completed or equivalent
       - `launch_requested_at` nullable
       - `started_at` nullable
       - `paused_at` nullable
       - `stopped_at` nullable
       - `completed_at` nullable
       - `created_at`
       - `updated_at`
     - include only fields needed for future lifecycle/state transitions and audience provenance

   - **SalesCampaignContacts**
     - join/execution-tracking table for contacts enrolled in a campaign
     - fields such as:
       - `id`
       - `company_id`
       - `sales_campaign_id`
       - `contact_id`
       - `status` for pending/active/completed/cancelled/replied/bounced/etc. as appropriate to current conventions
       - `current_step_order` nullable
       - `enrolled_at`
       - `last_scheduled_at` nullable
       - `last_sent_at` nullable
       - `cancelled_at` nullable
       - `completed_at` nullable
       - `created_at`
       - `updated_at`
     - enforce uniqueness of `(company_id, sales_campaign_id, contact_id)`

   Notes:
   - If there is already a canonical `contacts` table/entity, use a foreign key to it.
   - If “past customers” are represented through another table, do **not** over-model that here; keep campaign audience source as metadata/discriminator unless an existing relation pattern clearly exists.
   - Prefer explicit status columns over opaque JSON for core workflow state.

3. **Model tenant scoping correctly**
   - Every new table must include `company_id`.
   - Add foreign keys that preserve tenant-safe query patterns.
   - If the codebase uses base entity types/interfaces for tenant ownership, implement them.
   - Ensure all indexes begin with or include `company_id` where appropriate for shared-schema multi-tenancy.

4. **Add EF Core entity configurations**
   - Configure table names, keys, required fields, lengths, booleans, timestamps, and relationships.
   - Add constraints:
     - required `name` on campaigns and sequences
     - required `template_content` on steps
     - non-negative `delay_days`
     - positive `step_order`
   - Add unique indexes:
     - sequence step order uniqueness per sequence
     - campaign contact uniqueness per campaign/contact
   - Add non-unique indexes for expected reads:
     - campaigns by tenant + status + updated/created date
     - sequences by tenant + status/name
     - steps by tenant + sequence + step order
     - campaign contacts by tenant + campaign + status
     - campaign contacts by tenant + contact + status
   - Use PostgreSQL-friendly types and index definitions.

5. **Create the migration**
   - Generate or hand-author the EF Core migration according to repo norms.
   - Verify the migration:
     - creates tables in dependency-safe order
     - creates indexes after tables
     - defines foreign keys with sensible delete behavior
   - Be conservative with cascade deletes:
     - likely cascade from sequence to steps
     - likely restrict or carefully choose behavior for campaign to contacts depending on existing conventions
   - Ensure the migration has a clear name tied to TASK-35.1.1.

6. **Keep the implementation aligned with future acceptance criteria**
   Even though this task is schema-only, ensure the schema can support:
   - draft/save validation flows
   - at least 4 sequence steps
   - campaign lifecycle transitions: start/pause/stop
   - per-contact enrollment and cancellation
   - future reply/deal-triggered cancellation
   - future email delivery/reply correlation

   Do this by choosing durable, explicit columns now rather than requiring destructive schema changes later.

7. **Avoid overengineering**
   - Do not add scheduling/execution tables unless they already exist as part of a broader pattern and are clearly required by this task.
   - Do not add JSON blobs for everything.
   - Do not implement repositories/services/handlers unless required to compile due to architectural patterns.

8. **Document assumptions in code comments only where necessary**
   - Keep comments minimal.
   - If a relationship is intentionally deferred to a later task, make that clear in naming and migration structure rather than verbose comments.

# Validation steps
1. **Build the solution**
   - Run:
     - `dotnet build`

2. **Run tests**
   - Run:
     - `dotnet test`

3. **Review migration output**
   - Confirm the migration includes:
     - all four tables
     - `company_id` on each table
     - primary keys
     - foreign keys
     - required constraints
     - unique indexes
     - tenant-scoped query indexes

4. **Sanity check schema design**
   - Verify the schema supports these future queries efficiently:
     - list campaigns for a tenant by status
     - load a sequence and its ordered steps
     - find all contacts enrolled in a campaign
     - find active/pending campaign contacts for a contact
     - update campaign state quickly by tenant and id

5. **If the repo supports local database migration verification**
   - Apply the migration locally against PostgreSQL.
   - Confirm tables and indexes are created as expected.
   - If a migration script generation workflow exists, run it too.

6. **Final code review checklist**
   - No cross-tenant table lacks `company_id`
   - No index for tenant-owned reads omits tenant scope where it matters
   - No required field is accidentally nullable
   - No enum/string lengths are unconstrained if the codebase normally constrains them
   - No delete behavior introduces unsafe data loss

# Risks and follow-ups
- **Risk: unclear existing contact/customer schema**
  - Follow the existing canonical contact model. If there is ambiguity, prefer linking `SalesCampaignContacts` to the existing contacts entity only and represent audience source on the campaign as metadata/discriminator.

- **Risk: status modeling drift**
  - Reuse existing enum/string conventions. If the codebase stores statuses as strings, keep them consistent and constrained.

- **Risk: future execution needs may require additional tables**
  - This task should not invent full execution scheduling tables unless already established elsewhere. A later task can add execution/job tables if needed.

- **Risk: delete behavior**
  - Be careful with cascade rules in a multi-tenant system. Prefer conservative delete behavior unless the codebase has a strong established pattern.

Follow-up work likely needed after this task:
- application commands/queries for create/edit/list campaign and sequence
- validation rules for required fields and minimum step count
- launch workflow and per-contact scheduling
- policy enforcement for outbound settings and approvals
- inbox/reply/deal event handling to cancel pending steps
- email integration and delivery/reply persistence
- campaigns UI with live state updates