# Lead Generation Agent Implementation Prompts

These prompts implement SME lead generation as a CRM-compatible but CRM-independent capability owned by Alex, the Sales Manager agent. The internal sales workspace is the system of record when a company has no CRM; when a CRM is connected, provider adapters synchronize normalized records while Virtual Company retains research evidence, agent decisions, approvals, and audit history.

The prompts are ordered by dependency. Implement areas in this order: ICP and search strategy, source ingestion, account discovery, contact enrichment, buying signals and research, then lead quality and delivery. Within each area, execute prompts 1 through 5 in order. Do not begin automated outbound messaging as part of this pack; approved leads may be handed to the existing sales campaign workflow.

## Instructions shared by every prompt

The following instructions are part of every prompt in this document:

- Implement production-ready behavior, not scaffolding, mock production data, or a proof of concept.
- Read and follow `AGENTS.md` and `production-implementation.md`. For backend, data, workflow, agent, approval, integration, and orchestration changes, also read and follow `architecture-inst.md` when present and `/docs/architecture-rules.md`. For UI work, also read and follow `ui-instructions.md` and `/docs/design.md`, including screenshot-first requirements.
- Inspect the repository before editing. Extend the existing Sales entities, services, policies, events, outbox workers, integration adapters, AI orchestration, audit, authorization, and UI patterns rather than creating a parallel CRM or agent stack.
- Preserve tenant isolation and server-side authorization. Never trust client-supplied company, agent, source, CRM, contact, account, approval, or policy identifiers without tenant-scoped validation.
- Preserve local SQL Server and Docker SQL Server compatibility. Add an EF Core migration only when persisted models change, update the model snapshot, and keep restore and migration flows valid in both environments.
- Normalize provider data into internal contracts. Keep credentials and provider-specific payloads inside Integration, record source and collection time, and never treat an integration as the owner of agent rationale or audit history.
- Route all AI work through the shared AI orchestration subsystem. Require validated structured outputs, bounded execution, source-grounded summaries, safe fallback behavior, and no storage of hidden reasoning.
- Use background workers and outbox processing for recurring discovery, enrichment, signal monitoring, CRM synchronization, and other long-running or reliable side effects. Make work tenant-aware, idempotent, retryable, observable, and cancellable where appropriate.
- Start Alex at conservative autonomy. Research and recommendations may run automatically within configured budgets; paid data access, material record changes, CRM exports, and outreach require policy enforcement and approval where configured.
- Add focused automated coverage and run relevant builds and tests. For UI work, verify real rendered desktop and mobile behavior against a saved reference screenshot.
- Do not stop with in-scope TODOs, placeholder connectors, disconnected endpoints, unused persisted state, or UI controls without real backend behavior. Preserve unrelated user changes in the worktree.

## Area 1: ICP and Search Strategy

### Prompt 1.1: Persist versioned ideal customer profiles

**Outcome**

Give each company one or more versioned ideal customer profiles that Alex can use as explicit, auditable prospecting criteria.

**Current context**

Sales already persists leads, contacts, customer companies, qualification fields, automation policy, recommendations, activities, and audit events. There is no first-class ICP aggregate.

**Dependencies**

None.

**Implementation requirements**

- Add tenant-owned ICP profile and version models using relational fields for name, status, target geography, industries, employee/revenue ranges, buyer roles, technologies, maturity indicators, pain hypotheses, positive criteria, and disqualifiers.
- Support draft, active, superseded, and archived lifecycle states with domain transition methods, optimistic concurrency, author, timestamps, and immutable activated-version snapshots.
- Add application commands, queries, validation, authenticated endpoints, and audit events for create, edit draft, activate, clone, and archive.
- Allow multiple active profiles while requiring one profile per prospecting run. Reject contradictory ranges, empty targeting, unsupported criteria, and cross-tenant references.
- Keep flexible provider/search metadata in JSON only where it is not core queryable business state.

**Acceptance criteria**

- An authorized sales user can create and activate a valid ICP version and later explain exactly which version governed a lead.
- Editing an active profile creates or requires a new version; historical leads retain their original version reference.
- Unauthorized, stale, invalid, and cross-tenant operations make no mutation.

**Verification**

Add domain, validation, lifecycle, concurrency, authorization, tenant-isolation, API contract, migration, and persistence tests; build affected projects.

### Prompt 1.2: Build the ICP settings experience

**Outcome**

Let an SME define useful targeting criteria without needing sales-operations expertise or editing raw configuration.

**Dependencies**

Prompt 1.1.

**Implementation requirements**

- Follow the screenshot-first workflow and add an ICP list/editor to the Sales settings surface using existing navigation, form, validation, and agent-presence patterns.
- Organize the editor into target companies, buyer roles, business problems, buying indicators, and exclusions; use plain English and appropriate structured controls.
- Show draft versus active state, version history, last editor, validation issues, and the number of leads/runs governed by each version.
- Add a server-backed preview that summarizes included and excluded example organizations without duplicating matching logic in Razor.
- Handle empty, loading, unauthorized, stale edit, validation, narrow-screen, and unsaved-change states.

**Acceptance criteria**

- A first-time SME user can activate a minimally valid ICP and understand who Alex will seek and avoid.
- Existing active profiles cannot be silently overwritten, and usage history remains visible.
- Preview and validation results come from the same backend rules used during prospecting.

**Verification**

Add API-client and component tests, accessibility checks, concurrency/error cases, and desktop/mobile browser verification against the saved reference.

### Prompt 1.3: Compile ICP versions into deterministic match rules

**Outcome**

Translate each active ICP into a reusable server-side matcher that can evaluate normalized accounts consistently.

**Dependencies**

Prompts 1.1 and 1.2.

**Implementation requirements**

- Add an application-level ICP compiler and matcher returning criterion-level matched, not matched, unknown, or disqualified results.
- Normalize industries, countries, employee/revenue bands, job roles, and technology aliases through explicit taxonomies or existing normalization services.
- Separate hard exclusions from positive fit; unknown data must not be treated as a positive match.
- Version the matcher and persist an explainable evaluation snapshot containing inputs, profile version, matched criteria, unknowns, exclusions, and evaluated time.
- Keep deterministic matching independent from AI. AI may classify ambiguous source text only through shared orchestration and must return confidence and evidence.

**Acceptance criteria**

- The same normalized account and profile version produce the same result.
- Hard exclusions always prevent qualification regardless of positive criteria.
- Every result can be explained criterion by criterion without exposing internal codes.

**Verification**

Add table-driven tests for ranges, aliases, unknowns, exclusions, conflicting evidence, profile versions, and tenant boundaries.

### Prompt 1.4: Create prospecting run plans

**Outcome**

Convert an ICP and a business target into a bounded, reviewable plan before external searches begin.

**Dependencies**

Prompts 1.1 through 1.3.

**Implementation requirements**

- Add a tenant-owned prospecting run aggregate with ICP version, requested account/contact counts, geography, source selection, freshness requirement, budget, schedule, owner, status, progress, and cancellation metadata.
- Generate a structured plan through shared AI orchestration only where useful; validate source choices, filters, volume, cost estimate, and expected steps server-side.
- Support manual, scheduled, and recurring runs through existing workflow and background-worker conventions.
- Require approval before paid-source usage or a configurable budget threshold, and persist approval links and policy decisions.
- Expose commands and read models for plan, approve, start, pause/cancel, retry, and inspect progress without performing long-running work in HTTP requests.

**Acceptance criteria**

- A run cannot start without an active ICP, valid limits, permitted sources, and required approval.
- Duplicate start/retry requests do not duplicate provider work.
- Users can see what will be searched, estimated cost, current step, and failure/retry state.

**Verification**

Test lifecycle transitions, approval thresholds, schedules, idempotency, cancellation, authorization, tenant isolation, audit, and worker dispatch.

### Prompt 1.5: Learn from ICP outcomes safely

**Outcome**

Use accepted, rejected, converted, and meeting-booked outcomes to recommend ICP improvements without silently changing targeting.

**Dependencies**

Prompts 1.1 through 1.4 and existing lead lifecycle events.

**Implementation requirements**

- Aggregate outcome metrics by ICP version and criterion using Sales events/read models rather than transactional queries in UI components.
- Capture structured rejection reasons and distinguish poor fit, bad data, timing, duplicate, compliance exclusion, and user preference.
- Generate source-backed change recommendations through shared AI orchestration with sample size, uncertainty, expected effect, and affected criteria.
- Never auto-edit an active ICP. Let authorized users accept a recommendation into a new draft version or dismiss it with rationale.
- Add minimum sample thresholds and suppress recommendations based on sparse or biased evidence.

**Acceptance criteria**

- Recommendations cite measured outcomes and create a reviewable draft rather than changing production criteria.
- Rejected or dismissed recommendations do not repeatedly reappear without materially new evidence.
- Metrics remain tenant-scoped and historical versions remain comparable.

**Verification**

Test aggregation correctness, minimum samples, deduplication, draft creation, audit, permissions, and misleading-data edge cases.

## Area 2: Source Ingestion and Connectors

### Prompt 2.1: Define the prospect data provider boundary

**Outcome**

Create one normalized contract for account, contact, and signal discovery across approved external and internal sources.

**Dependencies**

Area 1.

**Implementation requirements**

- Define Integration-owned provider interfaces and capabilities for account search, contact search, enrichment, signal retrieval, paging, rate limits, cost estimates, and freshness.
- Return normalized candidate contracts with stable provider references, source URL where allowed, observed time, confidence, licensing/retention restrictions, and raw-payload reference rather than provider schemas in Sales.
- Add provider registry, configuration validation, capability discovery, health status, and structured error taxonomy.
- Ensure credentials use existing secure integration storage and never appear in logs, audit records, API responses, or prompts.
- Provide deterministic test providers only in test infrastructure; do not expose fake providers as production options.

**Acceptance criteria**

- Sales can request normalized discovery without knowing provider-specific authentication or payload types.
- Unsupported capabilities, missing credentials, quota exhaustion, and provider errors return actionable normalized states.
- Every result retains enough provenance to enforce licensing and explain its origin.

**Verification**

Add contract, registry, configuration, redaction, capability, failure-mapping, and tenant-isolation tests.

### Prompt 2.2: Ingest company-owned lead sources

**Outcome**

Let SMEs generate candidates from existing first-party data before requiring paid prospect databases.

**Dependencies**

Prompt 2.1.

**Implementation requirements**

- Add adapters for existing website lead submissions, permitted mailbox signals, uploaded CSV/XLSX imports, and existing contacts/customer companies.
- Validate file types, size, columns, encoding, formulas, and malicious content; stage imports for mapping and review before committing records.
- Track source, consent/communication status when supplied, row-level errors, duplicates, import actor, and import batch.
- Process large imports in a tenant-aware background job with progress, cancellation, retry, and downloadable error report.
- Reuse current website lead capture and sales email ingestion rather than duplicating their paths.

**Acceptance criteria**

- A valid first-party import creates normalized candidates exactly once and reports invalid rows without losing valid ones.
- Reimporting the same file or provider item does not create duplicate active leads.
- Formula injection, cross-tenant references, and unsupported data are rejected or neutralized safely.

**Verification**

Test each source, file security, mapping, partial failures, idempotency, concurrency, consent fields, and tenant isolation.

### Prompt 2.3: Implement configurable external discovery connectors

**Outcome**

Allow administrators to connect approved prospect-data providers without coupling lead generation to one vendor.

**Dependencies**

Prompts 2.1 and 2.2.

**Implementation requirements**

- Implement at least one real provider adapter only when credentials and provider terms are available; otherwise complete the provider-neutral production boundary without shipping a placeholder connector.
- Add authenticated connection setup, validation, status, disconnect, quota/cost display, allowed capabilities, retention terms, and last successful use.
- Translate compiled ICP criteria into provider-supported filters and record unsupported criteria for local post-filtering.
- Use outbox/background execution, rate-limit handling, cursor persistence, retries with backoff, and reconciliation after uncertain outcomes.
- Require explicit admin configuration of permitted fields, regions, budgets, and retention before use.

**Acceptance criteria**

- A configured connector can execute a bounded search and return normalized candidates with accurate cost/quota and provenance.
- Disconnecting prevents new calls without deleting required historical audit evidence.
- Partial pages and retries do not duplicate candidates or charges where provider idempotency is available.

**Verification**

Add adapter contract tests, integration tests against provider sandbox when available, retry/cursor tests, secret-redaction checks, and an opt-in live test path.

### Prompt 2.4: Build source governance and budget policies

**Outcome**

Give SMEs control over which sources Alex may use, how much they may cost, and how collected data may be retained.

**Dependencies**

Prompts 2.1 through 2.3.

**Implementation requirements**

- Add tenant-scoped policies for enabled sources, geographic restrictions, field allowlists, per-run/monthly budgets, retention periods, refresh limits, and approval thresholds.
- Enforce policy before every provider call and again before persistence; do not rely on agent instructions or UI visibility.
- Reserve and reconcile expected provider cost atomically enough to prevent concurrent runs exceeding limits.
- Persist policy version, decision, estimated/actual usage, approval, rationale summary, and audit correlation.
- Provide clear blocked, approval-required, quota-exhausted, and retention-expired states.

**Acceptance criteria**

- A disallowed source or over-budget run makes no provider call.
- Concurrent runs cannot exceed the configured budget through a race condition.
- Every provider result can be traced to the policy and approval that allowed collection.

**Verification**

Test thresholds, concurrency, reservation reconciliation, policy versioning, authorization, retention enforcement, and audit.

### Prompt 2.5: Create source operations and health UI

**Outcome**

Make connector status, usage, costs, failures, and allowed actions understandable to a non-technical SME administrator.

**Dependencies**

Prompts 2.1 through 2.4.

**Implementation requirements**

- Follow screenshot-first workflow and add a Sales data sources settings page using existing integration and settings patterns.
- Show connection state, capabilities, last success, recent failures, monthly usage/cost, quota, retention, and current policy in plain English.
- Support connect, validate, configure, pause, and disconnect only where server-provided allowed actions permit them.
- Link failures to affected prospecting runs and expose safe retry/reconnect guidance without raw provider payloads.
- Include no-source guidance emphasizing first-party import, plus loading, unauthorized, stale, and mobile states.

**Acceptance criteria**

- Administrators can understand whether Alex can search now, what it may cost, and what needs attention.
- Non-admins cannot change connection or policy state through UI or direct API calls.
- Displayed usage reconciles with persisted provider usage records.

**Verification**

Add read-model, API-client, authorization, component, accessibility, and responsive browser tests against the saved reference.

## Area 3: Account Discovery and Qualification

### Prompt 3.1: Persist normalized prospect accounts

**Outcome**

Represent discovered organizations independently from qualified leads while reusing existing customer-company records where identity matches.

**Dependencies**

Areas 1 and 2.

**Implementation requirements**

- Add or extend the sales model with a tenant-owned prospect-account lifecycle linked optionally to `CustomerCompany`, ICP version, prospecting run, and provider references.
- Store normalized legal/display name, domains, locations, industry, size bands, revenue band, technologies, source observations, freshness, and confidence in queryable fields where operationally relevant.
- Model candidate, researching, qualified, rejected, merged, stale, and converted states through domain methods.
- Preserve conflicting source observations rather than silently overwriting them; maintain a selected canonical value with provenance.
- Add indexes and constraints for tenant/domain/provider identity only where supported by actual query patterns.

**Acceptance criteria**

- Discovery can persist an account candidate without prematurely creating a lead or duplicate customer company.
- Canonical fields are explainable from source observations, including conflicts and freshness.
- Lifecycle, ownership, and queries are tenant-safe and concurrency-aware.

**Verification**

Add entity, persistence, migration, canonicalization, lifecycle, concurrency, index, and tenant-filter tests.

### Prompt 3.2: Execute account-first discovery runs

**Outcome**

Run the approved search plan across enabled sources and produce a bounded set of organization candidates before finding people.

**Dependencies**

Prompt 3.1 and Area 2.

**Implementation requirements**

- Implement a persisted, resumable workflow that searches sources, normalizes pages, applies hard exclusions, matches the ICP, and stores candidate accounts.
- Enforce maximum candidates, pages, runtime, cost, retries, and provider calls from the approved run plan.
- Check existing customers, prospects, blocked organizations, competitors, partners, and prior provider references before candidate creation.
- Persist step progress, cursors, counts, cost, source failures, cancellation, and correlation across tool execution and audit.
- Continue safely after partial provider failure when policy allows and mark incomplete coverage explicitly.

**Acceptance criteria**

- One run creates no more than its approved limit and can resume without duplicate accounts or repeated completed pages.
- Excluded organizations never enter the review queue as qualified candidates.
- Users can distinguish complete, partial, cancelled, failed, and budget-stopped runs.

**Verification**

Test multi-source paging, limits, cancellation, retries, partial failures, duplicate delivery, exclusion enforcement, costs, and tenant isolation.

### Prompt 3.3: Resolve organization identity and duplicates

**Outcome**

Prevent the same business from appearing repeatedly under different names, domains, subsidiaries, or provider identifiers.

**Dependencies**

Prompts 3.1 and 3.2.

**Implementation requirements**

- Implement deterministic identity candidates using normalized domain, registration number where permitted, provider reference, website redirects, phone, and normalized name/location.
- Return exact, probable, ambiguous, or distinct decisions with evidence and confidence; never auto-merge ambiguous records.
- Add an authorized merge workflow that reassigns source observations, contacts, signals, run links, and lead references transactionally while retaining aliases and audit history.
- Use shared AI orchestration only to assist ambiguous name/entity interpretation, with structured evidence and mandatory human review.
- Prevent merge cycles, cross-tenant merges, customer/prospect data loss, and concurrent duplicate merges.

**Acceptance criteria**

- Exact identities merge or reuse idempotently; ambiguous pairs enter a review queue.
- A completed merge preserves every business relationship and leaves a durable redirect/alias.
- Undo is available only when safe or the UI clearly explains why a merge cannot be reversed.

**Verification**

Add matching, false-positive, subsidiary, alias, merge transaction, concurrency, redirect, authorization, and tenant-isolation tests.

### Prompt 3.4: Qualify accounts against the ICP

**Outcome**

Produce an evidence-backed account qualification before spending effort on contact enrichment.

**Dependencies**

Prompts 3.1 through 3.3 and Prompt 1.3.

**Implementation requirements**

- Evaluate each canonical account through the deterministic ICP matcher and store the evaluation snapshot.
- Resolve missing classification fields through approved sources or bounded AI extraction only when policy, budget, and source terms permit.
- Apply hard exclusions first, then minimum fit requirements; record unknown criteria and confidence separately from match strength.
- Support automatic qualification only above configurable evidence and confidence thresholds; route borderline and conflicting cases to review.
- Emit deduplicated `sales.lead.detected`-compatible events only after account qualification, without creating outreach actions.

**Acceptance criteria**

- Qualified accounts show matching, nonmatching, unknown, and disqualifying criteria with evidence.
- Low-confidence or contradictory data cannot produce an auto-qualified account.
- Re-evaluation after fresher data preserves prior snapshots and emits events only for meaningful state changes.

**Verification**

Test match outcomes, thresholds, missing data, source conflicts, reevaluation, event deduplication, and tenant isolation.

### Prompt 3.5: Build the account discovery review workspace

**Outcome**

Let sales users review discovered organizations, resolve duplicates, and approve which accounts proceed to contact search.

**Dependencies**

Prompts 3.1 through 3.4.

**Implementation requirements**

- Follow screenshot-first workflow and add an operational account discovery list/detail page using existing Sales layout and Alex agent context.
- Provide filters for run, ICP, status, fit, geography, source, freshness, confidence, duplicates, and review need with server-side paging/sorting.
- In detail, show company summary, ICP evaluation, evidence, source conflicts, duplicate candidates, exclusions checked, and allowed actions.
- Support accept for contact discovery, reject with structured reason, request more research, and merge review through server-enforced commands.
- Include bulk actions only for homogeneous low-risk records and require confirmation plus per-record policy validation.

**Acceptance criteria**

- Reviewers can understand why an account appeared and what data is uncertain before approving it.
- Rejection reasons feed ICP learning and prevent immediate rediscovery under the configured suppression rule.
- Bulk, stale, unauthorized, and concurrent actions fail safely without partial hidden mutations.

**Verification**

Add read-model, filtering, paging, bulk-action, concurrency, authorization, component, accessibility, and desktop/mobile browser tests.

## Area 4: Contact Discovery and Enrichment

### Prompt 4.1: Model buying roles and contact candidacy

**Outcome**

Represent why a person matters to an account before turning that person into a sales lead contact.

**Dependencies**

Area 3.

**Implementation requirements**

- Add tenant-owned contact-candidate and account-role models linked to a qualified prospect account and optionally to the existing `Contact` entity.
- Support decision-maker, operational owner, technical evaluator, financial approver, influencer, and possible champion as configurable business roles with confidence and evidence.
- Store normalized name, title, department, seniority, location, profile references, employment status, source observations, and freshness.
- Distinguish verified facts from inferred roles and make unknown values explicit.
- Add lifecycle states for discovered, enriching, ready for review, accepted, rejected, stale, merged, and converted.

**Acceptance criteria**

- A candidate can hold multiple evidence-backed buying roles without being mislabeled as a confirmed decision-maker.
- Existing contacts are reused when identity matches, with source observations retained.
- All relations and lifecycle changes remain tenant-scoped and auditable.

**Verification**

Add model, role, provenance, lifecycle, identity-link, migration, and tenant-isolation tests.

### Prompt 4.2: Discover relevant people account by account

**Outcome**

Find a small, relevant buying group for each approved account rather than collecting broad lists of employees.

**Dependencies**

Prompt 4.1 and Area 2 provider contracts.

**Implementation requirements**

- Translate ICP buyer roles and account context into provider contact searches with configurable maximum candidates per account and role.
- Run searches in a resumable background workflow respecting provider budgets, quotas, rate limits, geography, and licensing.
- Normalize titles and departments, infer potential buying roles only through validated deterministic mappings or shared AI orchestration with evidence.
- Stop when required role coverage is achieved, limits are reached, or the account becomes excluded/stale.
- Persist search queries, result provenance, progress, costs, failures, and why each candidate was retained or discarded.

**Acceptance criteria**

- Contact discovery remains bounded and focused on configured roles.
- Unsupported or low-confidence role inference is clearly marked and cannot imply authority as fact.
- Retry and resume do not duplicate candidates or repeat completed provider pages.

**Verification**

Test role translation, limits, stopping rules, pagination, retries, costs, stale accounts, inference validation, and tenant isolation.

### Prompt 4.3: Enrich and verify contact details

**Outcome**

Collect only permitted, useful professional details and expose their quality before any outreach decision.

**Dependencies**

Prompts 4.1 and 4.2.

**Implementation requirements**

- Implement provider-neutral enrichment for current employer/title, professional profile, business email, business phone where permitted, location, language, and verification status.
- Track every field's source, observed/verified time, confidence, retention rule, and conflict history; do not overwrite stronger fresh evidence silently.
- Separate discovered, inferred, provider-verified, and user-confirmed states.
- Enforce source policy, geographic/privacy restrictions, purpose limitation, field allowlists, and deletion/expiry rules before persistence.
- Never generate or guess email addresses and present them as verified; inferred addresses must remain clearly labeled and excluded from automated outreach by default.

**Acceptance criteria**

- Users can tell which fields are verified, inferred, stale, conflicting, or unavailable.
- Disallowed fields are neither persisted nor exposed to AI context.
- Re-enrichment is idempotent and retains an explainable observation history.

**Verification**

Add field-provenance, conflict, expiry, policy, privacy, verification, redaction, retry, and tenant-isolation tests.

### Prompt 4.4: Resolve people identity and employment changes

**Outcome**

Avoid duplicate people and prevent outreach based on outdated employment information.

**Dependencies**

Prompts 4.1 through 4.3.

**Implementation requirements**

- Match contact candidates using normalized professional profile, verified email, provider identifiers, and cautious name/employer evidence.
- Treat shared inboxes, role accounts, common names, consultants, and people with multiple current roles explicitly.
- Add merge review and transactional merge behavior preserving activities, account roles, source observations, suppression state, and existing sales links.
- Detect employment changes from fresher observations, close stale account-role relationships, and require review before moving a contact to a new account.
- Cancel or block pending campaign steps through existing campaign stop rules when contact eligibility or employment becomes invalid.

**Acceptance criteria**

- Strong identities reuse existing contacts; ambiguous people are never auto-merged.
- A confirmed job change prevents further outreach to the old employer and remains historically explainable.
- Merge and employment updates cannot cross tenants or orphan sales records.

**Verification**

Test shared addresses, common names, multiple jobs, job changes, merge transactions, campaign cancellation, concurrency, and tenant isolation.

### Prompt 4.5: Build contact review and buying-group UI

**Outcome**

Let sales users inspect an account's likely buying group and approve appropriate people for lead creation.

**Dependencies**

Prompts 4.1 through 4.4.

**Implementation requirements**

- Follow screenshot-first workflow and add a buying-group section to prospect account detail using existing Sales components and responsive patterns.
- Show role coverage, person, current title/employer, evidence, verification/freshness, conflicts, suppression status, and rationale in plain English.
- Support accept, reject with reason, correct data, request enrichment, resolve duplicate, and mark employment changed through server-enforced actions.
- Warn when required buying roles are missing or all available contact channels are unverified.
- Do not expose private/provider-restricted fields, raw confidence codes, or controls for actions the server does not allow.

**Acceptance criteria**

- Reviewers can select relevant contacts without mistaking inference for verified authority or contactability.
- Accepted contacts link cleanly to existing `Contact` records and remain connected to source evidence.
- Invalid, stale, and unauthorized actions are rejected consistently in UI and API.

**Verification**

Add read-model, role-coverage, command, authorization, component, accessibility, and desktop/mobile browser tests.

## Area 5: Buying Signals and Account Research

### Prompt 5.1: Define a normalized buying-signal model

**Outcome**

Represent time-bound events that may change an account's priority without treating weak signals as proof of intent.

**Dependencies**

Areas 2 and 3.

**Implementation requirements**

- Add tenant-owned signal definitions and observations for hiring, funding, expansion, leadership change, product launch, regulatory change, technology adoption, website intent, and configurable categories.
- Store account, signal type, observed/event time, source, excerpt/structured facts, confidence, freshness window, strength, status, and licensing metadata.
- Distinguish confirmed event, inferred indicator, contradictory evidence, expired, dismissed, and superseded states.
- Deduplicate the same real-world event across providers while retaining all source observations.
- Do not encode that a signal equals purchase intent; store its relevance to an ICP separately.

**Acceptance criteria**

- The same event from multiple sources appears once with multiple provenance records.
- Expired or contradicted signals stop affecting priority but remain in history.
- Signal data is tenant-scoped, source-grounded, and queryable by account/type/freshness.

**Verification**

Add model, deduplication, freshness, contradiction, migration, query-index, and tenant-isolation tests.

### Prompt 5.2: Ingest and monitor approved signal sources

**Outcome**

Collect relevant signals on a schedule without uncontrolled web browsing or repeated paid calls.

**Dependencies**

Prompt 5.1 and Area 2.

**Implementation requirements**

- Extend provider capabilities for signal search and monitoring, including cursor/watermark, supported types, cost, and source terms.
- Add scheduled, tenant-aware monitoring for qualified accounts with configurable cadence, account limits, signal types, and freshness.
- Use reliable workers with leases/locking, idempotency, backoff, rate-limit handling, cancellation, and stale cursor recovery.
- Apply source, budget, privacy, and retention policy before calls and persistence.
- Record run coverage, accounts checked, observations found, costs, failures, and next scheduled check.

**Acceptance criteria**

- Repeated monitoring does not duplicate observations or exceed configured cadence and budget.
- Provider outages preserve cursor state and recover without losing or replaying unbounded data.
- Paused/excluded accounts and disconnected sources are not queried.

**Verification**

Test schedules, locking, cursors, retries, deduplication, budgets, pauses, provider failure, and tenant isolation.

### Prompt 5.3: Produce source-grounded account research briefs

**Outcome**

Give Alex and sales users a concise, current account brief built only from permitted evidence.

**Dependencies**

Prompts 5.1 and 5.2 plus Areas 3 and 4.

**Implementation requirements**

- Retrieve tenant-scoped account, contact, signal, activity, and approved knowledge context through existing context-retrieval boundaries.
- Generate a versioned structured brief through shared AI orchestration containing confirmed facts, hypotheses, recent signals, likely problems, buying group, opportunity angles, risks, unknowns, and source identifiers.
- Validate every source identifier server-side and reject unsupported factual claims or cross-tenant references.
- Label hypotheses explicitly, include evidence freshness, and persist a source snapshot and generation metadata without hidden reasoning.
- Provide deterministic fallback summaries when AI is unavailable and avoid generating legal, financial, or competitive claims without evidence.

**Acceptance criteria**

- Every material fact in a brief resolves to permitted persisted evidence.
- Facts, hypotheses, stale evidence, and unknowns are visibly distinct.
- Provider failure or invalid output produces a safe fallback and observable failure state.

**Verification**

Test structured parsing, source validation, grounding, prompt injection, stale evidence, fallback, authorization, and tenant isolation.

### Prompt 5.4: Evaluate signal relevance to each ICP

**Outcome**

Turn raw events into explainable timing evidence for a specific sales motion without overstating intent.

**Dependencies**

Prompts 5.1 through 5.3 and Area 1.

**Implementation requirements**

- Add a deterministic relevance policy based on signal type, recency, source quality, ICP pain hypotheses, and configured positive/negative indicators.
- Use shared AI orchestration only for semantic relevance that cannot be resolved deterministically; require structured confidence, evidence, and uncertainty.
- Store per-ICP relevance snapshots separately from the underlying signal so one event may matter differently to different profiles.
- Cap cumulative influence from correlated signals and prevent low-confidence events from creating high intent scores.
- Recalculate on material signal, ICP, or evidence changes and preserve prior evaluations.

**Acceptance criteria**

- Signal relevance is explainable and never presented as confirmed purchasing intent.
- Correlated duplicates cannot inflate account priority repeatedly.
- Low confidence, old, dismissed, and contradicted signals have appropriately limited or zero effect.

**Verification**

Add tests for recency decay, source quality, correlation caps, profile differences, AI validation/fallback, and reevaluation.

### Prompt 5.5: Build signal timeline and research UI

**Outcome**

Show what changed at an account, why it may matter, and which evidence needs review.

**Dependencies**

Prompts 5.1 through 5.4.

**Implementation requirements**

- Follow screenshot-first workflow and add a research tab to prospect account detail with brief, signal timeline, evidence sources, freshness, and open questions.
- Show confirmed facts and Alex's hypotheses in visually and linguistically distinct sections.
- Provide filters by signal type/status/time and server-provided actions to dismiss, confirm, mark irrelevant, request refresh, or regenerate a brief.
- Link each factual statement and signal to safe source detail where licensing allows, without exposing raw provider payloads.
- Include empty, incomplete-coverage, stale, provider-disconnected, generation-failed, unauthorized, and mobile states.

**Acceptance criteria**

- Users can understand what changed, how current it is, and why Alex believes it matters.
- Dismissed/irrelevant feedback stops affecting scoring and informs future evaluation.
- Brief and timeline stay consistent after refresh and concurrent updates.

**Verification**

Add read-model, filtering, action, concurrency, authorization, component, accessibility, and responsive browser tests.

## Area 6: Lead Quality, Review, and Delivery

### Prompt 6.1: Implement transparent multi-component lead scoring

**Outcome**

Rank account-contact combinations using separate fit, timing, role relevance, and data-confidence components rather than an opaque AI score.

**Dependencies**

Areas 1 through 5.

**Implementation requirements**

- Add versioned scoring policies with configurable weights, thresholds, component caps, hard exclusions, and minimum evidence requirements.
- Calculate deterministic component scores for ICP fit, signal/timing relevance, contact role coverage, and data confidence; store inputs, policy version, score, band, and explanation snapshot.
- Permit AI only to supply validated classifications already represented as evidence, never the final arithmetic or an untraceable score.
- Treat missing data as uncertainty rather than a positive; block eligibility on active suppression, compliance exclusion, or invalid employment.
- Recalculate idempotently on material changes and retain score history for later evaluation.

**Acceptance criteria**

- Every score decomposes into components and evidence that sum according to the governing policy.
- Hard exclusions override all positive scores.
- The same inputs and policy version always produce the same score.

**Verification**

Add table-driven arithmetic, thresholds, caps, exclusions, missing data, versioning, recalculation, and tenant-isolation tests.

### Prompt 6.2: Enforce suppression, compliance, and eligibility

**Outcome**

Ensure no prospect becomes outreach-ready when company policy, prior decisions, consent status, geography, or data quality forbids it.

**Dependencies**

Prompt 6.1 and existing sales campaign stop rules.

**Implementation requirements**

- Add a centralized Sales eligibility policy that evaluates account, contact, source terms, suppression records, customer/competitor/partner status, rejection cooling period, employment validity, and communication restrictions.
- Support durable suppression at organization, domain, person, email, phone, and provider-reference levels with reason, scope, source, effective/expiry time, and audit.
- Evaluate eligibility before lead creation, CRM export, campaign enrollment, and every outbound send; stale earlier decisions cannot authorize changed records.
- Import and synchronize suppression state from existing CRM/mail providers where supported without weakening internal restrictions.
- Allow authorized corrections and time-limited exceptions, but never overrides that violate hard tenant, security, or legal constraints.

**Acceptance criteria**

- Suppressed or ineligible prospects cannot be exported, enrolled, or contacted even through direct API calls.
- New suppressions cancel pending campaign steps idempotently.
- Eligibility decisions return stable reason codes and plain-English operator explanations.

**Verification**

Add policy, expiry, precedence, import, cancellation, stale-decision, authorization, audit, and tenant-isolation tests.

### Prompt 6.3: Convert approved prospects into canonical leads

**Outcome**

Create or update existing Sales leads only after account and contact review, preserving all research and identity links.

**Dependencies**

Prompts 6.1 and 6.2.

**Implementation requirements**

- Add an idempotent application command that converts an eligible prospect account/contact into the existing `CustomerCompany`, `Contact`, and `Lead` model as appropriate.
- Reuse existing canonical records and active leads when identity matches; never create duplicate active leads for the same sales motion.
- Link ICP version, prospecting run, qualification evaluation, score snapshot, source observations, signals, research brief, and acceptance actor.
- Set initial status, owner, priority, suggested next action, and pipeline stage using domain rules, not controller or UI logic.
- Emit existing sales lead events and audit records exactly once, without automatically launching outreach.

**Acceptance criteria**

- Approved eligible prospects produce one canonical lead with complete provenance.
- Retry/concurrency returns the existing lead and does not duplicate customer, contact, event, or activity records.
- Rejected, stale, suppressed, or unauthorized prospects cannot convert.

**Verification**

Add transaction, identity reuse, duplicate race, provenance, lifecycle, event, authorization, and tenant-isolation integration tests.

### Prompt 6.4: Synchronize with CRM and spreadsheet workflows

**Outcome**

Support SMEs with no CRM, spreadsheet-based operations, or an existing CRM without making provider systems authoritative for Alex's reasoning.

**Dependencies**

Prompt 6.3 and Area 2 provider boundaries.

**Implementation requirements**

- Define CRM adapter contracts for account/contact/lead upsert, external identity mapping, field capability, owner/stage mapping, suppression import, incremental sync, and conflict reporting.
- When no CRM is configured, keep the existing Sales workspace as the system of record and provide safe CSV/XLSX export/import using the same normalized contracts.
- Execute sync through outbox/background workers with idempotency keys, watermarks, retries, rate limits, and uncertain-outcome reconciliation.
- Configure field ownership per connection; never silently overwrite user-confirmed data or internal evidence with lower-quality provider values.
- Require approval before first export and optionally for bulk exports; audit fields shared, target system, actor/agent, and result without storing secrets.

**Acceptance criteria**

- No-CRM companies can complete the workflow entirely inside Virtual Company and export portable data.
- Connected CRM sync creates stable mappings and handles retries without duplicate remote records.
- Conflicts are surfaced for review and resolved according to explicit field-ownership policy.

**Verification**

Add adapter contract, mapping, idempotency, conflict, watermark, retry, export-security, approval, and tenant-isolation tests; use opt-in provider sandbox tests where available.

### Prompt 6.5: Build the prioritized lead review and performance loop

**Outcome**

Deliver a production lead workspace where users can review Alex's prioritized leads, hand them to outreach, and measure quality.

**Dependencies**

Prompts 6.1 through 6.4.

**Implementation requirements**

- Follow screenshot-first workflow and extend the existing Sales leads experience with server-side filtering, paging, sorting, saved views, and Alex's prioritized review queue.
- Show account/contact, score components, ICP evidence, signals, data confidence, source freshness, eligibility, CRM sync, owner, status, and next action in plain English.
- Provide allowed actions for accept, reject with structured reason, request research, assign owner, create/update lead, export/sync, and hand an approved lead to the existing campaign workflow.
- Add read models for qualified leads created, completeness, review acceptance, time to review, meeting conversion, source yield/cost, ICP performance, and false-positive/rejection reasons.
- Ensure every KPI drills into the exact governed records and uses historical policy/profile snapshots rather than current settings.

**Acceptance criteria**

- A user can understand why each lead is relevant, what is uncertain, and what action is permitted next.
- No action bypasses current eligibility, approval, authorization, or CRM field-ownership checks.
- Performance metrics reconcile to lead, activity, meeting, campaign, and conversion records and remain tenant-scoped.

**Verification**

Add read-model correctness/performance, filtering, paging, action, KPI reconciliation, authorization, component, accessibility, and desktop/mobile browser tests against the saved reference; run the complete lead-generation regression suite and affected builds.

## Full Implementation Outcome

After all 30 prompts are complete, Alex can turn a versioned ICP into an approved prospecting plan, search governed first-party and external sources, qualify and deduplicate accounts, identify and verify buying groups, monitor relevant signals, produce grounded research, score candidates transparently, enforce suppression and eligibility, create canonical leads, and deliver them to either the built-in Sales workspace or a connected CRM. Every material decision remains tenant-scoped, source-backed, policy-enforced, recoverable, and auditable.
