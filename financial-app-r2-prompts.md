# Financial App Release 2 Implementation Prompts

Release: Native Receivables  
Source roadmap: [financial-roadmap.md](financial-roadmap.md)  
Prompt order: execute Prompts 1–10 in order. Each prompt delivers a usable bounded capability and preserves existing imported/Fortnox invoice behavior.

## Shared execution contract

Every prompt in this document is an implementation prompt, not an analysis or scaffolding task.

- Read and follow `production-implementation.md`, `docs/architecture-rules.md`, the repository `AGENTS.md`, and the relevant current code before editing.
- `architecture-inst.md` is required by the workspace instructions when present, but it was not present when this pack was generated. If it exists at execution time, read and follow it; do not invent a substitute. `docs/architecture-rules.md` remains mandatory.
- For UI work, read and follow `ui-instructions.md` and `docs/design.md`. Complete the mandatory screenshot-first workflow before implementing a new or materially redesigned screen, save references under `docs/design/references/`, and visually compare the implementation with them.
- Existing repository implementation wins over older planning text. Preserve provider-synced invoices, Fortnox actions, customer-invoice accounting, payments/allocations, invoice reviews, simulation, routes, wire values, and migrations unless a prompt explicitly changes them.
- Keep capability ownership narrow: Domain entities/value rules, Application commands/queries/policies, Finance infrastructure implementations/adapters/workers, Persistence configurations/migrations, transport-only controller partials, and typed Web API clients.
- Every tenant-owned record, relationship, query, command, outbox message, worker claim, rendered object, delivery attempt, audit event, and cache key must enforce company scope. Add cross-company read/write tests.
- Issued documents and posted journals are immutable. Corrections use credit notes, reversals, write-off journals, linked replacements, and retained source/version evidence. All postings continue through `IAccountingPostingService`.
- Customer email, e-invoice transport, provider writes, and refunds use durable outbox/background execution with stable business idempotency, bounded retry, provider acknowledgement, reconciliation-required state for ambiguity, safe failures, audit, and telemetry.
- Approval requirements and allowed actions are authoritative backend policy. Recheck current approval and source version immediately before issue, delivery, external submission, refund, or write-off execution.
- Use SQL Server EF migrations for schema changes, preserve local and Docker SQL Server restore/run compatibility, and finish with no pending model changes.
- Release 1 Swedish policy/document/VAT rules are authoritative when a Swedish pack is selected. Do not duplicate or weaken them in the AR module. Unsupported multi-currency/tax/e-invoice cases must stop explicitly.
- Keep user-facing language plain English with complete English/Swedish localization. Do not expose internal statuses, policy names, provider payloads, or technical identifiers.
- No prompt may ship mock production records, fake delivery/provider success, silent fallback, unbounded request work, weakened tests, unhandled states, or deferred in-scope TODOs.

---

## Prompt 1 — Production customer billing master and duplicate governance

### 1. Title and outcome

Implement a complete customer billing master so native receivables have validated legal, tax, address, currency, payment, delivery, and credit-control facts with duplicate governance and retained change history.

### 2. Current context

- `FinanceCounterparty` currently stores name, type, email, free-form payment terms, tax ID, credit limit, preferred payment method, and default account mapping.
- Counterparty list/create/update APIs and the Finance counterparties page already exist.
- Fortnox sync creates/updates counterparties and stores `FinanceExternalReference` identities.
- Release 1 adds company statutory identity and authoritative Swedish document/tax policy.
- Known gap: customer billing facts are too shallow for native invoice issue and delivery; addresses, legal identity classification, default currency/language, structured payment terms, delivery channels, verification, and duplicate decisions are not modeled durably.

### 3. Dependencies

- Release 0 Accounting Core GA.
- Release 1 completed for Swedish launch; country-neutral customers remain supported with explicit limitations.

### 4. Implementation requirements

- Extend or add a narrowly owned customer billing profile with legal/display name, organization/person classification, tax/VAT identifiers and validation state, billing/delivery addresses, country, language, default currency, structured payment terms, payment method, invoice delivery preferences, buyer reference, e-invoice identifier/type, credit limit/status, account/dimension defaults, and effective dates.
- Preserve `FinanceCounterparty` identity and current API compatibility. Avoid stuffing important structured state into one JSON field.
- Add deterministic normalization and format validation, separating format validity, user attestation, provider-sourced data, and externally verified state.
- Add source provenance and field-level or version-level change history sufficient to explain whether a value came from user entry, provider sync, migration, or approved merge.
- Implement company-scoped duplicate detection using normalized legal/tax/e-invoice/email/address facts. Persist candidate evidence and require an authorized merge/keep-separate decision; never auto-merge on a heuristic.
- Implement safe customer merge semantics: preserve external references, documents, invoices, payments, communications, audit links, and a redirect/tombstone; reject cross-company merges and unsafe cycles.
- Add commands/queries, typed reason codes, authorization, optimistic concurrency, audit before/after evidence, and telemetry.
- Add an additive EF migration with tenant-scoped alternate keys/indexes that do not incorrectly reject legitimate shared contact details.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Do not overwrite provider-authoritative fields silently during sync; use explicit source precedence/conflict state.
- Existing invoices must retain the historical customer snapshot/source needed to explain them even after master-data changes.
- Do not turn duplicate scoring into an LLM authority decision. AI may recommend; deterministic policy and human authorization decide.
- No major UI redesign is required until Prompt 9; keep current counterparties pages contract-compatible.

### 6. Acceptance criteria

- Given a valid customer profile, when an accounting admin saves it, then structured facts, source, version, audit history, and delivery defaults are retained company-safely.
- Given the same tax identity in another company, when duplicate detection runs, then no cross-company candidate or data is exposed.
- Given likely duplicates in one company, when reviewed, then merge and keep-separate decisions are explicit, audited, idempotent, and do not lose linked records.
- Given provider and user updates to the same field, when precedence cannot be decided safely, then a visible conflict is created rather than silently overwriting data.
- Given historical issued invoices, when customer master data changes, then their retained customer snapshot remains unchanged.

### 7. Verification

- Domain normalization, duplicate scoring, merge, provenance, and concurrency tests.
- API authorization, tenant-isolation, stale-version, source-conflict, and problem-contract tests.
- Fortnox counterparty sync and provider-switch migration regressions.
- EF migration, index, upgrade, and no-pending-model tests on SQL Server.
- Existing counterparties and invoice read UI/client tests.

### 8. Definition of done

- Customer billing profile, provenance, duplicate governance, merge behavior, contracts, services, APIs, persistence, migration, audit, telemetry, tests, and operator guidance are complete.
- Existing counterparty/provider behavior remains compatible.
- No mock verification, heuristic auto-merge, silent overwrite, or in-scope TODO remains.

---

## Prompt 2 — Native invoice drafts, lines, totals, tax preview, and approval

### 1. Title and outcome

Implement a durable native customer-invoice draft lifecycle so users can create and revise itemized invoices, calculate totals and tax through the selected policy pack, attach evidence, and obtain approval before issue without consuming a legal invoice number.

### 2. Current context

- `FinanceInvoice` is currently a document/read aggregate with one total amount, supplied invoice number, approval/settlement/posting statuses, and optional source document.
- There is no native invoice line aggregate or complete draft editor contract.
- `CustomerInvoiceAccountingProfile` and `CustomerInvoiceAccountingPolicy` already preview and post accounting for an existing invoice.
- `ManualJournalDraft` demonstrates versioned draft, line, evidence, approval, and idempotency patterns.
- Release 1 supplies statutory document policy, number-series controls, and Swedish VAT policy.

### 3. Dependencies

- Release 2 Prompt 1.
- Release 1 Prompts 2–3.

### 4. Implementation requirements

- Add a native customer-invoice draft aggregate with company/customer identity, draft status, document type, issue/supply/due dates, currency, payment terms, buyer/seller references, notes, delivery intent, version, source, and timestamps.
- Add normalized line records with stable sequence, description, quantity, unit, unit price, discount, net amount, selected tax input/classification, dimension facts, optional source/order reference, and deterministic calculated tax/gross amounts.
- Keep draft calculation in authoritative application policy using decimal precision/rounding from accounting configuration and VAT decisions from Release 1. Persist calculation inputs, result hash, pack/version, warnings, and blockers.
- Support create/update/copy/preview/discard with optimistic concurrency and stable idempotency. Draft edits must invalidate stale preview/approval evidence.
- Attach existing company documents as evidence through authorized document references; do not duplicate binaries or expose inaccessible documents.
- Add a backend issuance-readiness policy returning allowed, stable reason codes, explanation, required approval, customer/credit/tax/statutory blockers, and the evidence used.
- Route approval through the existing approval subsystem with amount/risk/policy thresholds. Store the approved draft version/hash; a later edit invalidates approval.
- Do not create a `FinanceInvoice`, allocate an invoice number, post a journal, render a PDF, or send externally in this prompt.
- Add APIs in a focused invoice controller partial and typed Web client methods while preserving existing invoice reads.
- Add EF migration, tenant keys, draft/version/idempotency uniqueness, and bounded indexes for lists/status/customer.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Draft totals and allowed actions are backend decisions; UI consumes them later.
- Do not reuse manual-journal entities for invoice drafts; reuse patterns, not unrelated aggregates.
- Do not mutate provider-synced invoices into native drafts.
- Reject unsupported foreign currency or tax combinations explicitly until Release 5 capability exists.

### 6. Acceptance criteria

- Given valid customer and line input, when previewed, then net, discount, VAT, gross, rounding, accounts/box facts, warnings, and result hash are deterministic.
- Given a stale draft version or idempotency key with different payload, when updated, then the request conflicts and no partial lines survive.
- Given an approved draft that is edited, when issuance readiness is queried, then approval is stale and issue is blocked.
- Given an inaccessible evidence document or another company's customer, when referenced, then the command fails without exposing the record.
- Given duplicate retries, when create/submit is replayed, then one draft/approval chain exists.

### 7. Verification

- Domain/policy tests for line calculations, discounts, rounding, dates, tax, dimensions, totals, transitions, and unsupported cases.
- Approval, stale evidence, idempotency, concurrency, and transaction rollback tests.
- API authorization, tenant-isolation, validation, pagination, and contract tests.
- EF migration/upgrade and no-pending-model tests.
- Regressions for existing invoice reads/reviews, customer accounting, manual journals, and Fortnox sync.

### 8. Definition of done

- Native draft aggregate, line calculations, preview, readiness, approvals, evidence, APIs, typed clients, persistence, migration, audit, telemetry, tests, and documentation are production-complete.
- Drafts do not allocate numbers, post, render, or send.
- No client-side authority, mock totals, or deferred in-scope TODO remains.

---

## Prompt 3 — Atomic native invoice issue, numbering, and ledger posting

### 1. Title and outcome

Implement the atomic issue boundary that converts a current approved draft into an immutable numbered invoice and posts its receivable/revenue/VAT journal exactly once.

### 2. Current context

- Prompt 2 provides versioned approved drafts and issuance readiness.
- Release 1 Prompt 3 provides company/fiscal-year document series and immutable issue snapshot requirements.
- `IAccountingPostingService` already handles accounting authority, periods, balance, voucher allocation, source/version idempotency, approvals, immutable journals, and rollback.
- `CustomerInvoiceAccountingService` already maps supported existing invoices to accounting proposals and retains profile/journal state.
- `FinanceInvoice` currently conflates provider/imported snapshots with future native-issued documents and must remain backward compatible.

### 3. Dependencies

- Release 2 Prompts 1–2.
- Release 1 Prompts 1–3.

### 4. Implementation requirements

- Add an issue command requiring company, draft, expected draft version/hash, current approval, selected native document series, accounting period/date, actor, stable idempotency key, and correlation ID.
- Recheck customer state, statutory profile, tax decisions, document policy, number series, accounting authority/period, approval, evidence access, and source version immediately before issue.
- In one SQL transaction: claim/allocate the next document number, create the immutable issued `FinanceInvoice` and issued snapshot/lines, create/link the customer accounting profile, call the governed posting service, link the resulting journal, record number allocation outcome, and enqueue any downstream render request only after durable state exists.
- Preserve one source/version/idempotency identity across issued document and journal. Different payload with the same key must conflict.
- Model number allocation rollback/gap behavior explicitly. A failed transaction must not leave a visible issued invoice or journal; if a number is irreversibly reserved by chosen policy, retain a documented void/gap reason rather than reuse it.
- Separate imported/provider-issued and native-issued authority in persistence/read models without breaking historical data.
- Return the issued invoice, immutable totals/tax snapshot, accounting journal link, delivery state `not_queued`, and allowed next actions.
- Add safe retry behavior for transient SQL concurrency without allocating duplicate numbers or journals.
- Audit issue success/failure with snapshot hash, number series/reference, journal ID, actor, approval, and source evidence—never full sensitive payloads.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- `IAccountingPostingService` remains the single journal boundary; do not copy posting validation or voucher logic into invoice services.
- Do not issue from an unapproved/stale draft or through a controller transaction.
- Do not send email/e-invoice inline; Prompt 4 and Prompt 8 own delivery transports.
- Issued snapshots, numbers, and posted journals are immutable.

### 6. Acceptance criteria

- Given a current approved draft, when issue executes, then one number, invoice, snapshot, journal, and audit chain commit atomically.
- Given concurrent issue requests for the same/different drafts, when they execute, then numbers are unique/sequential under configured policy and each draft issues at most once.
- Given a forced posting or persistence failure, when the transaction rolls back, then no partial invoice/journal remains and number handling follows documented gap policy.
- Given an idempotent replay, when the same command returns, then the original invoice/journal is returned; a changed payload conflicts.
- Given stale approval, locked period, authority mismatch, unsupported tax, or cross-company ID, when issue is attempted, then nothing is allocated or posted.

### 7. Verification

- SQL Server concurrency, atomic rollback, number allocation, source identity, and duplicate replay tests.
- Finance policy/service tests for readiness recheck, imported/native authority, and immutable snapshots.
- API authorization, tenant isolation, stale version, idempotency, and safe error tests.
- Migration/upgrade and restore tests if schema changes from Prompt 2 are extended.
- Regressions for customer accounting, voucher sequences, reports, close, Fortnox actions, and provider migration.

### 8. Definition of done

- Atomic issuance, numbering, immutable snapshot, governed posting, read models, APIs, audit, telemetry, tests, migration if needed, and runbook are complete.
- No direct external send occurs and no partial issued/accounting state is possible.
- No duplicate posting path, fake success, or in-scope TODO remains.

---

## Prompt 4 — Deterministic invoice PDF and durable email delivery

### 1. Title and outcome

Generate an accessible immutable invoice PDF from the issued snapshot and deliver it by email through a durable, auditable, retryable workflow with honest acknowledgement and failure states.

### 2. Current context

- Prompt 3 creates immutable issued invoice snapshots and can enqueue downstream work.
- Company documents/object storage, hashes, access scopes, mailbox connections, `CompanyOutboxMessage`, and reliable delivery patterns already exist.
- Support reply and invitation delivery demonstrate outbox dispatch, retry, reconciliation-required, and operator-visible failure behavior.
- There is no native customer invoice PDF renderer or invoice-specific email delivery aggregate.

### 3. Dependencies

- Release 2 Prompt 3.
- A selected, license-approved deterministic .NET PDF rendering approach and a configured outbound mailbox provider for real delivery tests.

### 4. Implementation requirements

- Add a deterministic renderer that consumes only the immutable issued snapshot, statutory profile snapshot, customer snapshot, policy/tax facts, payment details, branding, and supported locale.
- Produce accessible PDF output with exact invoice/credit-note identity, dates, parties, line/tax totals, payment/OCR/reference data, original credit reference, legal text, page numbering, and overflow handling. Document fonts, library/license, encoding, and reproducibility limits.
- Persist a company-owned rendered artifact record with invoice/snapshot hash, template version, locale, media type, file name, content hash/length, object key, generation status/attempts/failure, and timestamps. Reuse existing document/object abstractions where ownership and retention fit.
- Render in a bounded background worker. Stable idempotency must prevent duplicate artifacts for the same snapshot/template/locale; a template change creates a new explicit version.
- Add email delivery request/attempt/acknowledgement records linked to the exact artifact and recipient snapshot. Recheck current issue state, delivery address, authorization/approval policy, and artifact hash before enqueue/dispatch.
- Send through the established mailbox/provider boundary, not directly from HTTP. Classify delivered, bounced/rejected where known, retryable failure, permanent failure, and ambiguous/reconciliation-required.
- Add resend as a distinct authorized action with reason and stable idempotency; preserve every prior attempt.
- Add read/download/request/resend APIs, typed Web clients, business audit, technical telemetry, operator retry/reconcile actions, and retention/recovery coverage.
- Never regenerate an old invoice from current customer/company master data; use the issued snapshots.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- No fake sent/delivered state. Provider acceptance is not recipient delivery unless the provider contract proves it.
- Do not log invoice content, email body, access tokens, or full recipient identity in technical logs.
- Object storage and email side effects cannot share the SQL transaction; design durable intermediate and reconciliation states.
- A rendering/delivery failure must not undo issuance or posting.

### 6. Acceptance criteria

- Given an issued invoice, when rendering runs, then the PDF content reflects the immutable snapshot and its stored hash/length are reproducible for the same renderer version.
- Given master-data changes after issue, when the PDF is regenerated for the same template version, then historical customer/seller facts do not change.
- Given duplicate worker delivery or process death, when work resumes, then at most one logical artifact/send attempt per idempotency key exists.
- Given provider timeout after possible send, when classified, then the delivery becomes reconciliation-required and is not blindly resent.
- Given another company or inaccessible artifact, when requested, then neither PDF metadata nor content is disclosed.

### 7. Verification

- Golden/render tests for layout content, multipage invoices, localization, credits, special characters, totals, accessibility metadata, and deterministic artifact hashes where supported.
- Outbox/worker tests for duplicate, retry, permanent, ambiguous, process-death, resend, and acknowledgement paths.
- Mailbox provider contract tests without real network by default, plus separately categorized real integration tests when credentials exist.
- API authorization/tenant/download tests and object-storage recovery checksum tests.
- Existing document, mailbox, support delivery, invoice, and accounting regressions.

### 8. Definition of done

- Versioned PDF rendering, object persistence, email delivery workflow, APIs, typed clients, worker, audit, telemetry, recovery, tests, and operator documentation are complete.
- Real production delivery uses configured mailbox credentials; absence of a connection is an actionable blocked state.
- No mock PDF, inline send, invented acknowledgement, or deferred in-scope TODO remains.

---

## Prompt 5 — Recurring invoice schedules and safe generation

### 1. Title and outcome

Implement recurring customer invoicing so authorized users can schedule, preview, approve, pause, and safely generate native invoice drafts without duplicates or hidden autonomous issuance.

### 2. Current context

- `SupplierSubscription` provides recurring supplier-side cadence/lifecycle patterns.
- Company simulation has recurring expense generation, but simulation is not production billing authority.
- Prompts 1–4 provide customer profiles, native invoice drafts, atomic issue, and delivery.
- Existing background execution and task/approval systems support leased scheduled work.
- There is no production customer recurring-invoice aggregate.

### 3. Dependencies

- Release 2 Prompts 1–4.

### 4. Implementation requirements

- Add company-owned recurring invoice schedule/template records with customer, effective range, cadence, timezone, next occurrence, invoice/due-date rules, line template, currency, tax inputs, dimensions, delivery intent, approval/auto-issue policy, pause/end state, and version.
- Support launch cadences through explicit typed rules; define month-end, leap-day, business-day, proration, and timezone behavior deterministically.
- Add create/update/preview/activate/pause/resume/end commands with optimistic concurrency, authorization, audit, and stable idempotency.
- Implement a leased bounded worker that claims due occurrences, records occurrence identity, creates exactly one native draft through Prompt 2, and advances the schedule only after durable success.
- Default to draft generation. Auto-issue may be enabled only through explicit company policy, low-risk scope, current approval/authority rules, and the same Prompt 3 issue boundary; never issue directly inside schedule logic.
- Revalidate customer, tax, statutory, currency, dates, credit policy, and delivery readiness for each occurrence. Permanent business failures create a visible task/blocker; transient failures retry boundedly.
- Support preview of future occurrences without persistence/number allocation and show expected amounts/dates with rule explanation.
- Preserve schedule/template version on every generated draft and prevent backdated surprise generation after long pauses unless explicitly authorized.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Do not reuse supplier-subscription entities for customer billing; share cadence primitives only if ownership is genuinely common.
- Scheduling never bypasses draft approval, issue numbering, posting, or delivery boundaries.
- No unbounded catch-up loop in one worker claim or HTTP request.
- Simulation generators must not create production recurring schedules.

### 6. Acceptance criteria

- Given a monthly schedule, when the due worker runs repeatedly or concurrently, then one draft is created for each occurrence and next occurrence advances deterministically.
- Given process death after draft commit, when the lease expires, then replay finds the occurrence/draft and does not duplicate it.
- Given a paused/ended schedule, when its occurrence becomes due, then no draft is generated.
- Given stale customer/tax/policy facts, when generation runs, then the occurrence blocks visibly and creates no invalid draft or issued invoice.
- Given auto-issue disabled, when a draft is generated, then it remains ready for review and no number/posting/delivery occurs.

### 7. Verification

- Cadence/timezone/month-end/leap/proration deterministic tests.
- Worker lease, concurrency, duplicate delivery, catch-up bound, retry, process-death, and blocked-state tests.
- API authorization, tenant isolation, stale-version, and idempotency tests.
- Regression tests for supplier subscriptions, simulation generation, invoice drafts/issue, approvals, and close.
- EF migration/upgrade and no-pending-model tests.

### 8. Definition of done

- Schedule domain, occurrence records, services, worker, policies, APIs, typed clients, persistence, migration, audit, telemetry, tasks/failures, tests, and runbook are complete.
- Routine generation is safe and visible; issuance remains governed.
- No duplicate occurrence, hidden auto-issue, mock schedule, or in-scope TODO remains.

---

## Prompt 6 — Customer credits, cancellations, refunds, write-offs, and bad debt

### 1. Title and outcome

Complete native receivables corrections so invoices can be credited, cancelled where legally permitted, refunded, written off, or treated as bad debt through explicit approvals, linked accounting, and retained evidence.

### 2. Current context

- `CustomerInvoiceAccountingService.CreateCreditNoteAsync` and the invoices UI already support a basic customer credit-note path for existing invoices.
- `AccountingPostingService` supports linked reversals/corrections; payments and allocations track settlements.
- Fortnox customer invoice actions and supplier correction workflows provide provider/outbox patterns.
- Release 1 defines Swedish statutory credit-note and correction requirements.
- Prompts 2–4 add native drafts, issue/posting, rendered artifacts, and delivery.
- There is no unified native AR correction/refund/write-off policy across partial payments, tax, bank, and delivery states.

### 3. Dependencies

- Release 2 Prompts 1–4.
- Release 1 statutory document and VAT correction rules.

### 4. Implementation requirements

- Introduce an authoritative AR correction policy covering pre-issue discard, issued-unposted failure recovery, legally allowed cancellation, full/partial credit note, price/quantity/tax correction, refund, small-balance write-off, bad debt, and recovery of previously written-off debt.
- Base allowed actions on document authority, issue/post/delivery state, settlement/allocation state, period/authority lock, tax filing state, materiality thresholds, approval policy, provider ownership, and evidence.
- Reuse the native draft/issue boundary for credit notes with original-invoice reference, negative/offsetting lines, statutory snapshot, separate number, delivery, and linked accounting.
- Post corrections through `IAccountingPostingService`; use original journal/tax facts as evidence and create linked reversal/replacement or explicit write-off/bad-debt journals. Never mutate the original journal or return.
- Model refund proposals separately from executed money movement. Require current approval, beneficiary/payment evidence, durable provider/bank execution, acknowledgement, and reconciliation; if no payment provider exists, produce an approved manual instruction state rather than fake payment.
- Integrate payment allocations: release/reallocate credits/refunds safely, prevent over-refund/write-off, and maintain AR control reconciliation.
- Handle closed/filed periods through current-period corrections and VAT correction-return work when policy requires; never reopen silently.
- Preserve current Fortnox correction behavior and route provider-authoritative actions through its adapter, while native-authority actions stay internal.
- Add tasks, audit, telemetry, APIs, typed clients, and operator-visible retry/reconciliation paths.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Cancellation, credit, refund, and write-off are distinct accounting/business actions and must not be one generic status update.
- Refund execution is an external money movement and cannot occur directly in HTTP or from an LLM recommendation.
- Historical documents, delivery evidence, journals, VAT returns, and allocations remain traceable.
- Unsupported tax/period/provider combinations block explicitly.

### 6. Acceptance criteria

- Given an unpaid issued invoice, when a full/partial credit is approved and issued, then linked document/journal/tax facts reconcile and the original remains unchanged.
- Given partial payment, when credit/refund/write-off is proposed, then the policy prevents amounts exceeding the remaining economically valid balance.
- Given a closed or finalized VAT period, when correction is required, then current-period/correction-return actions are created according to policy and no historical fact changes.
- Given an ambiguous refund provider outcome, when execution returns, then state is reconciliation-required and no duplicate retry is automatic.
- Given stale approval/source version or another company, when correction executes, then no document, journal, allocation, or payment instruction changes.

### 7. Verification

- Policy matrix tests across issue/post/delivery/payment/period/tax/provider states.
- Credit, reversal, write-off, bad-debt recovery, refund, and allocation/control-account reconciliation tests.
- External execution duplicate/retry/ambiguous outcome and approval recheck tests.
- API authorization, tenant isolation, idempotency, concurrency, and problem-contract tests.
- Regression tests for existing credit notes, Fortnox actions, payments, bank reconciliation, tax return, and close.

### 8. Definition of done

- AR correction policy and all supported correction/refund/write-off flows, accounting, provider boundary, APIs, audit, telemetry, tasks, tests, migrations if needed, and runbook are complete.
- Every monetary/tax effect is linked and reconcilable.
- No generic destructive status change, fake refund, silent reopen, or in-scope TODO remains.

---

## Prompt 7 — Customer statements, aging, reminders, disputes, and collection workflow

### 1. Title and outcome

Implement a complete collections workspace so finance users can reconcile customer statements, prioritize overdue debt, manage reminders/disputes/promises, and send governed communications with measurable outcomes.

### 2. Current context

- Invoice read models expose due/settlement state, open receivables, overdue recommendations, and related transactions.
- `FinanceIntelligenceHeuristics` recommends reminder actions, but there is no durable reminder/dunning lifecycle.
- Payments/allocations and `GetCustomerInvoiceReceivableReconciliationQuery` provide open-item/control-account evidence.
- Notification/mailbox/outbox systems and Laura's finance insights/tasks already exist.
- Prompts 1–6 complete native invoice/customer/correction states.

### 3. Dependencies

- Release 2 Prompts 1–6.

### 4. Implementation requirements

- Add aged-receivables projections by customer, invoice, due date, aging bucket, currency, dispute/hold, promise-to-pay, reminder stage, credit exposure, and control-account difference, with stable cutoff/timezone semantics.
- Add durable customer statement snapshots with opening items, activity, allocations, credits, closing items, checksum, source manifest, locale, and rendered/downloadable artifact.
- Add reminder/dunning policy with configurable stages, grace periods, channels, templates, fee/interest capability flags, materiality, customer exceptions, disputes/holds, and approval requirements. Unsupported statutory fees/interest remain disabled.
- Model reminder cases/actions/attempts, customer responses, disputes, promises to pay, owner, due follow-up, resolution, write-off/credit escalation, and linked tasks.
- Generate reminder drafts from immutable invoice/statement facts. Send email through the durable delivery boundary; recheck current balance, dispute/hold, recipient, approval, and source version immediately before send.
- Prevent reminders for paid/credited/disputed/held or stale invoices and deduplicate repeated scheduled/manual actions.
- Add a bounded scheduled worker for due collection actions that prepares tasks/drafts by default; do not auto-send unless explicitly authorized by conservative policy.
- Integrate Laura as recommendation/prepare support with cited invoice/payment facts, not autonomous collection authority.
- Add outcome metrics: overdue value, days sales outstanding inputs, reminder-to-payment conversion, promises kept/broken, dispute aging, overrides, and communication failures.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- A recommendation is not a sent reminder. External communication uses outbox, approval policy, and reconciliation states.
- Do not calculate unsupported fees/interest or expose private customer data in agent prompts/logs beyond scoped need.
- Customer statements are evidence snapshots, not a substitute for the ledger/open-item system of record.
- Projection queries must be bounded/paginated and company-scoped.

### 6. Acceptance criteria

- Given posted invoices, credits, payments, and allocations, when aging/statement is generated for a cutoff, then open items and totals reconcile to AR control evidence.
- Given a payment or dispute after reminder preparation, when send executes, then current evidence is rechecked and stale communication is blocked.
- Given repeated worker runs, when a collection stage is due, then one logical task/draft exists for that stage/version.
- Given a promise or dispute, when recorded/resolved, then future actions and responsible owner change explicitly and audit history is retained.
- Given another company, when statement/reminder records are queried or sent, then no data or action crosses tenant scope.

### 7. Verification

- Aging cutoff/bucket/currency, statement checksum, reconciliation, reminder-policy, dispute, promise, and metrics tests.
- Worker schedule/idempotency/lease/retry and stale-before-send tests.
- Email delivery ambiguous/permanent/retry tests and approval recheck tests.
- API authorization, tenant isolation, pagination, download, and problem-contract tests.
- Regression tests for invoices, allocations, AR reconciliation, dashboards, tasks, and mailbox delivery.

### 8. Definition of done

- Aging, statements, collections policy/cases, reminder drafts/delivery, disputes/promises, worker, tasks, metrics, APIs, audit, telemetry, persistence, migration, tests, and runbook are complete.
- No customer is contacted from stale or unsupported evidence.
- No mock communication, hidden auto-send, or deferred in-scope TODO remains.

---

## Prompt 8 — Production e-invoice/Peppol delivery adapter

### 1. Title and outcome

Add one real production e-invoice/Peppol delivery path so eligible native invoices can be validated, submitted, acknowledged, rejected, retried, and reconciled through the existing integration architecture.

### 2. Current context

- Prompt 1 adds structured customer e-invoice identifiers/preferences.
- Prompts 2–4 provide immutable issued snapshots, tax/statutory facts, rendered artifacts, and durable delivery patterns.
- Finance integration registry, provider configuration, OAuth/token patterns, external references, write-command approvals, audit events, and operator diagnostics already exist.
- No production e-invoice provider adapter is currently registered.

### 3. Dependencies

- Release 2 Prompts 1–4 and Release 1 statutory document rules.
- A selected production e-invoice/Peppol provider, verified API/specification, contractual access, sandbox/production credentials, supported document/profile versions, and known acknowledgement semantics. If these are absent, do not invent an adapter or mark this prompt complete.

### 4. Implementation requirements

- Define Application-owned e-invoice contracts for capability/profile discovery, participant validation, document validation, submit, status/acknowledgement query, cancellation only if provider/spec supports it, attachment handling, and safe error classification.
- Implement the selected provider entirely in Infrastructure. Do not leak provider payload schemas into Domain, Application, API responses, or core invoice entities.
- Build the exact supported structured invoice/credit format from the immutable issued snapshot and retained statutory/tax/customer facts. Validate locally against the selected schema/profile before enqueue.
- Add connection/configuration lifecycle with secrets in approved secret storage, least scopes, diagnostics, health, consent/credential renewal, and safe admin status.
- Submit through durable outbox/background execution with stable business idempotency, bounded retry, rate-limit handling, provider reference, raw-response minimization, and acknowledgement polling/webhook reconciliation.
- Distinguish accepted-for-processing, delivered where proven, rejected, permanent validation failure, retryable transport failure, and ambiguous/reconciliation-required. Never map HTTP success directly to final delivery unless the contract guarantees it.
- Verify participant/recipient routing immediately before submit and retain the exact participant identifier/profile/version used.
- Add provider webhooks only if supported: signed verification, replay protection, company/connection resolution, idempotency, event history, and no cross-tenant lookup by untrusted payload alone.
- Add operator retry/reconcile actions, audit, telemetry, support runbook, sandbox contract fixtures, and separately categorized real-provider tests.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Provider credentials and payloads must never enter logs, source control, audit summaries, or user-facing errors.
- Email and e-invoice are separate delivery channels with explicit independent states.
- Do not resubmit on ambiguous outcome until provider reconciliation proves it safe.
- No fake provider, mock production data, or generic “Peppol compatible” claim without a verified profile.

### 6. Acceptance criteria

- Given an eligible issued invoice and valid participant, when submitted, then one provider request is made for the business idempotency key and every acknowledgement transition is retained.
- Given schema/profile validation failure, when submit is requested, then it stops before external transmission with exact safe remediation.
- Given timeout after possible provider acceptance, when handled, then state is reconciliation-required and polling/webhook reconciliation resolves it without duplicate submission.
- Given replayed or forged webhook events, when processed, then valid duplicates are idempotent and invalid signatures/tenant resolution are rejected.
- Given missing credentials or unsupported customer/profile, when delivery is requested, then the action is blocked honestly and email/native accounting remain unaffected.

### 7. Verification

- Provider contract/schema golden tests and participant/profile validation tests.
- Outbox, rate-limit, retry, ambiguity, duplicate, acknowledgement, webhook signature/replay, and credential-absence tests.
- Authorization, tenant isolation, provider configuration, and safe-error API tests.
- Separately opt-in sandbox/real API integration tests with no secrets in output.
- Regressions for invoice issue/posting, email delivery, Fortnox integration, provider registry, and provider switching.

### 8. Definition of done

- The selected provider connection, adapter, schema generation, durable submission, acknowledgements/webhooks, reconciliation, APIs, audit, telemetry, tests, and runbook are production-complete.
- Exact supported provider/profile versions and limitations are documented.
- If external prerequisites are absent, the task is explicitly blocked rather than completed with scaffolding or a fake adapter.

---

## Prompt 9 — Unified native receivables workspace

### 1. Title and outcome

Deliver a cohesive customer-to-cash Web experience for customer setup, invoice drafting/approval/issue, delivery, recurring billing, credits/refunds, statements, and collections while preserving established Finance navigation.

### 2. Current context

- Existing pages cover invoices, invoice details, reviews, counterparties, payments, transactions, accounting journals/reports, and finance overview.
- `InvoicesPage.razor` currently focuses on existing invoices, accounting state, provider actions, and basic credit notes; there is no complete native draft/issue/delivery workspace.
- Prompts 1–8 add all backend read models, allowed actions, and delivery/correction/collection states.
- `FinancePageLayout`, `FinanceModuleShell`, `FinanceSectionNav`, `FinanceDataState`, list/detail patterns, typed clients, localization, and Laura's contextual presence already exist.

### 3. Dependencies

- Release 2 Prompts 1–8. If e-invoice Prompt 8 is externally blocked, the UI must show the capability as unavailable and remain complete for native PDF/email delivery; it must not fake e-invoice support.

### 4. Implementation requirements

- Before UI changes, explicitly write screenshot prompts and generate reference images for the native invoice editor/issue flow and consolidated receivables/collections workspace. Save them under `docs/design/references/` and record them in the reference inventory.
- Preserve canonical Finance navigation. Extend existing Invoices and Counterparties pages or add focused detail routes only where the existing information architecture cannot support the workflow.
- Add customer billing profile and duplicate-review UI with source/conflict/verification explanations and safe merge controls.
- Add an invoice draft editor with customer selection, dates/terms, lines, discounts, tax decisions, dimensions where supported, evidence, live server preview, blockers, approval state, and explicit “Issue invoice” confirmation. Never calculate authoritative totals solely in Blazor.
- Add issued invoice detail with immutable snapshot, PDF, journal, approval, delivery channels/attempts, provider acknowledgements, payments/allocations, credit/refund/write-off actions, and timeline.
- Add recurring schedule management with next occurrences, preview, pause/resume/end, blocked occurrences, and links to generated drafts.
- Add collections workspace with aging KPIs, prioritized action queue, customer/invoice detail, statements, reminder drafts, disputes, promises, owner/tasks, and Laura recommendations with cited data.
- Render backend allowed actions and reason explanations; disabled controls are supplemented by plain remediation and server authorization.
- Add English and Swedish localization, accessibility, keyboard flow, responsive list/detail behavior, destructive-action confirmations, production empty/loading/error/retry states, and no simulation/mock production records.
- Implement typed clients through `ICompanyApiTransport`, preserve auth/correlation/not-found behavior, and register clients through `AddVirtualCompanyApiClients`.
- Compare rendered output to references and refine until visually close.

### 5. Constraints and preservation rules

- Follow the Shared execution contract and screenshot-first workflow.
- Use existing design tokens/components and calm operational language; do not create a new UI framework or generic admin console.
- Do not expose raw statuses, reason codes, tax keys, hashes, provider payloads, or tenant jargon in primary UI.
- Keep external send/refund/e-invoice actions approval-bound and asynchronously represented; never show request acceptance as final delivery/payment.
- Preserve legacy invoice routes and provider/simulation read behavior.

### 6. Acceptance criteria

- Given a valid customer and draft, when an authorized user edits/previews/submits/issues it, then the UI reflects server totals, approval, number, journal, PDF, and queued delivery without stale actions.
- Given blocked tax/customer/credit/period/delivery state, when viewed, then the user sees the specific issue, evidence, and safe next action.
- Given delivered, failed, or ambiguous email/e-invoice attempts, when invoice detail opens, then channel-specific states and operator actions are accurate.
- Given collections work, when a reminder/dispute/promise/statement is handled, then the timeline, task owner, current balance, and next action update from persisted state.
- Given viewer/admin/approver roles, English/Swedish locales, keyboard-only use, and narrow screens, when flows run, then visibility/actions/localization/layout remain correct.

### 7. Verification

- bUnit/component tests for editor calculations-from-server, stale preview, approval, issue, delivery, recurring, correction, collections, roles, errors, empty states, and localization.
- Web/API contract tests for every typed-client route and payload.
- Authenticated browser end-to-end checks with realistic persisted data, visual comparison, responsive sizes, and accessibility scan/manual keyboard verification.
- Web and API Release builds plus existing invoice/bill/payment/accounting surface regressions.
- Confirm reference images exist and are not served as product assets.

### 8. Definition of done

- Reference images, unified AR pages/components, typed clients, routes, localization, accessibility, tests, and browser evidence are complete.
- The experience communicates status, attention, and next action for every lifecycle state.
- No placeholder, mock data, client-owned business rule, raw internal language, or deferred in-scope TODO remains.

---

## Prompt 10 — Native receivables release proof and production operations

### 1. Title and outcome

Prove Native Receivables production readiness with end-to-end accounting, delivery, recovery, authorization, performance, and operational evidence across the complete customer-to-cash lifecycle.

### 2. Current context

- Prompts 1–9 implement customer master, invoice drafts, issue/posting, PDF/email, recurring schedules, corrections/refunds/write-offs, collections, optional real e-invoice, and the unified UI.
- Existing accounting release evidence/runbooks establish exact command/result, migration, restore, browser, and failure-injection expectations.
- AR control reconciliation, payments/allocations, bank reconciliation, reports, close, audit, tasks, approvals, provider switching, and object storage are already present and must remain compatible.
- Repository-wide test health is a Release 0 gate; focused success alone is not sufficient for a GA decision.

### 3. Dependencies

- Release 2 Prompts 1–9.
- Release 1 statutory validation for Swedish production claims.
- Real mailbox configuration for delivery proof and, if included in launch scope, the real e-invoice provider prerequisites from Prompt 8.

### 4. Implementation requirements

- Build production-shaped deterministic end-to-end scenarios covering customer creation/merge conflict, draft/approval/edit invalidation, issue/number/post, PDF/email, payment/allocation/bank reconciliation, partial/full credit, refund ambiguity, write-off/bad-debt recovery, recurring generation, statement/reminder/dispute/promise, close, export, and restore.
- Add health/readiness and operator views for failed/stale approvals, numbering conflicts/gaps, render failures, delivery ambiguity, recurring blockers, e-invoice rejection, unreconciled refunds, AR control differences, overdue collection tasks, and object/archive failures.
- Add supported-volume performance tests for invoice lists, draft preview, issue concurrency, document rendering, worker throughput, aging, statements, collections, and reports. Add needed bounded queries/indexes without weakening evidence.
- Run failure injection for SQL rollback, concurrent issue, duplicate outbox delivery, process death, expired leases, mailbox/provider timeout, ambiguous success, object persistence failure, webhook replay, stale customer/draft/approval, and cross-company IDs.
- Prove coordinated SQL/object backup and both local/Docker restores preserve invoice snapshots, journals, PDFs, delivery/acknowledgement history, statements, reminders, approvals, audits, external references, and checksums.
- Update finance/accounting runbooks with delivery reconciliation, number gaps, re-render/resend policy, refund recovery, recurring blockers, collection holds, e-invoice operations, retention, deployment order, feature flags, and forward-fix rollback.
- Produce checked-in Release 2 evidence with exact migrations, commands/results, real integration categorization, browser screenshots, supported volumes, residual risks, and explicit release decision.
- Repair in-scope regressions rather than weakening/deleting tests. Record unrelated blockers with owners only under the approved Release 0 test-health policy.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Do not claim recipient delivery, bank refund completion, e-invoice delivery, or statutory validity beyond retained external evidence.
- Do not use already-current databases as the only migration proof; test representative upgrade and fresh install.
- Application rollback preserves additive schema and accounting/delivery evidence; disable workers/features and forward-fix rather than deleting issued records.
- Any unresolved critical/high AR accounting, privacy, authorization, delivery-duplication, payment, restore, or control-reconciliation issue is a release stop.

### 6. Acceptance criteria

- Given the primary native customer-to-cash scenario, when executed end to end, then documents, journals, tax, payments, allocations, bank evidence, statements, communications, approvals, and reports reconcile exactly.
- Given duplicate/concurrent/restart failure injection, when operations recover, then no invoice number, issued document, journal, artifact, send, schedule occurrence, refund, or reminder is duplicated.
- Given ambiguous external outcomes, when operations review them, then state remains unresolved/reconciliation-required until provider evidence proves the result.
- Given local and Docker restores, when verification runs, then every SQL/object reference and checksum matches and the app can continue safely.
- Given the full agreed test matrix and supported volumes, when release evidence is generated, then results and residual risks support an explicit go/no-go decision.

### 7. Verification

- Full Finance, API, Web, Web contract, migration, and relevant Platform/Mailbox suites.
- SQL Server fresh-install, representative-upgrade, concurrency, rollback, and local/Docker restore tests.
- Authenticated browser E2E in English and Swedish across customer, draft, issue, delivery, correction, collections, payments, and accounting drill-down.
- Real mailbox/e-invoice integration tests run only in explicitly configured secure environments; sandbox/contract suites remain deterministic in normal CI.
- Final `dotnet ef migrations has-pending-model-changes`, solution builds, `git diff --check`, secret scan, and artifact/license review.

### 8. Definition of done

- Native Receivables has complete release evidence, operational controls, recovery proof, performance bounds, tests, runbooks, and an explicit release decision.
- All in-scope critical/high findings are resolved and every external result is represented honestly.
- No mock production path, unexplained failure, weakened test, secret, unhandled intermediate state, or deferred in-scope TODO remains.
