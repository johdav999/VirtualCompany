# Financial App P2 Implementation Prompts

Priority: P2 — Connected Banking and Treasury  
Source roadmap: [financial-roadmap.md](financial-roadmap.md) Release 4  
Prompt order: execute Prompts 1–9 in order. Prompts 1–5 may be developed before a payment-initiation provider is selected; Prompts 6–9 require the provider and operational choices stated in their dependencies.

## Shared execution contract

Every prompt in this package is an implementation prompt, not an analysis or scaffolding task.

- Read and follow `production-implementation.md`, `docs/architecture-rules.md`, repository `AGENTS.md` files, and the current implementation before editing.
- Preserve the native ledger, bank statement imports, bank transactions, payment allocations, reconciliation suggestions/results, suspense correction, cash settlement posting, Finance source modes, Fortnox integration, and Releases 0–2 behavior.
- Keep bank and payment providers behind Application-owned contracts and Finance infrastructure adapters. Provider payloads, consent secrets, tokens, certificates, and transport errors must not leak into domain entities, controllers, UI, logs, or user-facing errors.
- `IAccountingPostingService` remains the only native journal boundary. Imported rows, matches, fees, transfers, settlements, and payment outcomes must retain stable company/source/version identities.
- Money movement and other important provider writes require current backend authorization and approval, durable outbox/background execution, business idempotency, bounded retry, acknowledgement tracking, reconciliation for ambiguity, audit, and telemetry.
- Every company-owned connection, consent, account mapping, cursor, raw object, import row, payment instruction, attempt, acknowledgement, reconciliation record, task, audit event, cache key, and object-storage key must enforce company scope. Add cross-company read/write tests.
- Use additive SQL Server EF migrations and follow the `Database and EF Core` rules in `docs/architecture-rules.md`; finish with no pending model changes and preserve local/Docker restore compatibility.
- UI work must follow `ui-instructions.md` and the mandatory screenshot-first workflow in `docs/design.md`, retain English/Swedish localization, and use typed Web clients through `ICompanyApiTransport`.
- Do not claim a production bank, payment, recipient, settlement, or balance outcome from a mock, sandbox response, request acceptance, or deterministic test. Unsupported providers and formats must stop explicitly.
- Finish each prompt with production code, focused tests, broader affected-area validation, documentation, and no deferred in-scope TODOs.

---

## Prompt 1 — Bank connectivity, consent, and account ownership foundation

### 1. Title and outcome

Implement a provider-neutral bank connectivity foundation so each company can connect, authorize, map, suspend, renew, and disconnect real bank accounts without weakening tenant or credential boundaries.

### 2. Current context

- `CompanyBankAccount`, `ExternalAccountConnection`, `FinanceIntegrationConnection`, Fortnox OAuth/token patterns, provider registry abstractions, and accounting authority controls already exist.
- `CompanyBankTransactionService` and the current cash/reconciliation pages consume bank accounts and imported transactions, but there is no production open-banking consent lifecycle or bank-feed provider contract.
- Existing connection UI under Finance settings/accounting connections must remain compatible.

### 3. Dependencies

- Releases 0–2 implemented.
- A documented provider-selection decision is not required for the provider-neutral contracts, but one is required before Prompt 2 can be production-complete.

### 4. Implementation requirements

- Add Application contracts for bank providers, institutions, consent sessions, consent status, account discovery, account ownership, capabilities, health, renewal, revocation, and safe provider errors.
- Persist company-scoped bank connections, consent versions, discovered accounts, explicit internal-account mappings, granted capabilities, expiry, health, and immutable audit history; store secrets only through the established protected token/credential boundary.
- Add commands/queries and thin authorized APIs for connect, callback completion, account mapping, refresh, suspend, renew, disconnect, and status. Reject callback replay and cross-company state.
- Define stable reason codes and operator-visible states for missing consent, expired consent, scope loss, account ownership mismatch, provider outage, and reconciliation-required setup.
- Add telemetry and a runbook covering credential rotation, consent renewal, disconnect, compromised consent, and provider outage.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- A provider account must never become an accounting account merely by discovery; an authorized explicit mapping is required.
- Revocation stops future provider access but retains imported facts, source identity, acknowledgements, journals, and audit evidence.

### 6. Acceptance criteria

- Given two companies, when one completes or replays the other company's callback state, then the request is rejected without exposing connection facts.
- Given discovered accounts, when an authorized finance admin maps one, then the mapping is versioned, audited, company-scoped, and visible in connection status.
- Given expired or revoked consent, when synchronization is requested, then no provider call proceeds and the user receives an actionable renewal state.

### 7. Verification

- Domain and service tests for state transitions, callback replay, ownership mapping, expiry, revocation, and concurrency.
- API authorization and tenant-isolation tests; credential redaction and provider-error translation tests.
- EF migration/upgrade/no-pending-model checks and focused connection UI/client contract tests.

### 8. Definition of done

- Provider-neutral contracts, persistence, lifecycle services, APIs, UI integration points, audit, telemetry, migration, tests, and runbook are complete without storing credentials in business tables or claiming a provider connection that was not acknowledged.

---

## Prompt 2 — Continuous bank-feed ingestion and gap-free recovery

### 1. Title and outcome

Implement scheduled and manual bank-feed synchronization with stable provider identity, cursor checkpoints, duplicate prevention, and missing-range recovery so bank rows arrive exactly once and gaps remain visible.

### 2. Current context

- `BankStatementImport`, `BankStatementImportRow`, `BankTransaction`, external references, leased background execution, worker recovery, and bounded manual imports already exist.
- Fortnox synchronization demonstrates paged provider reads, but it is not a bank-feed implementation.
- Current bank transactions can be reconciled after import; continuous provider ingestion and raw-source retention are absent.

### 3. Dependencies

- P2 Prompt 1.
- A selected production Swedish bank or aggregator, its sandbox/non-production credentials, scopes, rate limits, pagination contract, and retention terms.

### 4. Implementation requirements

- Implement the selected provider adapter for accounts, balances, booked/pending transactions, stable transaction identity, pagination/cursors, consent state, and safe diagnostics.
- Add leased synchronization work with per-account checkpoints, overlap windows, deterministic deduplication, pending-to-booked replacement, bounded retry, rate-limit handling, and process-death recovery.
- Retain encrypted or protected raw-source objects plus checksums and normalized mappings without using provider JSON as authoritative queryable state.
- Detect missing date/sequence ranges and cursor regression; add explicit backfill/replay commands that cannot duplicate transactions or overwrite a different payload under the same identity.
- Surface feed health, last successful coverage, lag, missing ranges, failures, and remediation through APIs, operator UI, audit, logs, and metrics.

### 5. Constraints and preservation rules

- Follow the Shared execution contract and provider data-retention terms.
- Never synthesize a booked transaction, infer provider success, or advance a durable checkpoint before normalized data and source evidence commit atomically.
- Pending transactions must not create final ledger or reconciliation truth until provider rules establish a stable booked identity.

### 6. Acceptance criteria

- Given overlapping pages and repeated polling, when synchronization runs, then one normalized booked transaction exists per stable provider identity.
- Given worker death after page persistence but before completion, when the lease expires, then processing resumes without a gap or duplicate.
- Given a missing range or regressed cursor, when detected, then the feed becomes attention-required and an authorized bounded recovery can close the gap with retained evidence.

### 7. Verification

- Provider contract tests for pagination, pending/booked transitions, rate limits, replay, malformed payloads, and ambiguous responses.
- SQL Server concurrency/lease/checkpoint tests, authorization and cross-company tests, and failure-injection tests.
- Real-provider sandbox synchronization and interrupted-sync recovery evidence; migration and no-pending-model checks.

### 8. Definition of done

- One production adapter continuously imports traceable bank data with gap detection, recovery, operations visibility, and no silent or duplicate ingestion.

---

## Prompt 3 — ISO 20022 and governed statement import center

### 1. Title and outcome

Add safe ISO 20022 and reusable manual-import workflows so finance teams can preview, validate, map, and import bank statements when feeds are unavailable or supplementary files are required.

### 2. Current context

- Structured `ImportBankStatementCommand` and CSV-shaped row imports exist, with source identity and reconciliation state.
- There is no CAMT parser, PAIN status handling, reusable mapping profile, import preview, file-level validation report, or resumable import workspace.

### 3. Dependencies

- P2 Prompt 1.
- Confirm supported ISO 20022 message versions and initial CSV bank formats.

### 4. Implementation requirements

- Add streaming parsers and validators for selected CAMT.052/.053/.054 variants and relevant payment-status messages; reject unsupported namespaces/versions explicitly.
- Add versioned company-scoped CSV mapping profiles, locale-aware amount/date parsing, account/currency checks, preview, validation issues, duplicate detection, and dry-run totals.
- Persist import jobs, file checksum/object reference, parser/profile version, row outcomes, resumable checkpoints, conflict decisions, and audit evidence.
- Map accepted rows through the existing bank transaction/import model; never bypass its company keys, stable identities, or reconciliation states.
- Build authorized typed APIs and an import-center UI with file, preview, error, partial/resume, and completion states.

### 5. Constraints and preservation rules

- Follow the Shared execution contract; the mandatory `docs/design.md` screenshot-first workflow applies to the new import-center screen.
- Uploaded XML/CSV is untrusted input: enforce size limits, secure parsing, malware/content checks where established, and formula-injection-safe exports.
- A dry run or syntactically accepted file is not an imported or reconciled statement.

### 6. Acceptance criteria

- Given a supported CAMT statement, when previewed and committed, then balances, transaction identities, dates, references, currencies, and checksums match the source.
- Given an unsupported version, duplicate file, malformed row, or wrong-account currency, when processed, then the issue is explicit and no partial authoritative import is hidden.
- Given interruption during a large import, when resumed, then completed rows are not replayed and final totals reconcile to the source control totals.

### 7. Verification

- Golden-file parser tests, hostile XML/CSV tests, locale and rounding tests, resumability/idempotency tests, and import-total fixtures.
- API authorization/tenant tests, migration checks, object-storage recovery tests, and browser UAT in English and Swedish.

### 8. Definition of done

- Supported bank files can be previewed and imported with reproducible mappings, bounded failures, retained source evidence, and no ad hoc parser path.

---

## Prompt 4 — Explainable advanced reconciliation and settlement matching

### 1. Title and outcome

Extend reconciliation to support split, partial, one-to-many, and many-to-one settlement while retaining explainable, versioned matching evidence and explicit human decisions.

### 2. Current context

- Reconciliation scoring, suggestions, accept/reject records, payment links, allocations, suspense states, and AR/AP control reconciliation exist.
- Current matching is centered on bounded payment candidates and simpler relationships; learned counterparty/reference rules and complex settlement groups are incomplete.

### 3. Dependencies

- P2 Prompts 2–3.
- Native receivables and the applicable native payables/payment records.

### 4. Implementation requirements

- Add deterministic reconciliation groups containing bank rows, payments, invoices/bills, adjustments, residuals, currency, and expected control totals.
- Support split/partial, one-to-many, many-to-one, batch settlement, fees, rounding, and explicit unmatched residual/suspense outcomes.
- Add versioned company rules for normalized references, counterparties, amounts, timing, provider patterns, confidence thresholds, and safe recommendations; retain feature/reason contributions.
- Require authorized acceptance for material/low-confidence matches, enforce optimistic concurrency, and post cash effects only through existing settlement/posting services.
- Add reversal/correction semantics, audit before/after evidence, quality metrics, and operator queues for stale/conflicting suggestions.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Learned behavior may propose a rule or score but may not become an unexplained authority decision; deterministic policy and current approvals decide execution.
- Accepted historical reconciliation evidence is immutable; corrections append linked reversals/results.

### 6. Acceptance criteria

- Given one batch bank deposit covering several invoices, when accepted, then allocations balance to the bank row and AR reconciliation remains exact.
- Given a partial payment with a fee, when reconciled, then payment, fee journal, residual, source evidence, and control totals remain traceable.
- Given a changed rule version or stale candidate, when acceptance is attempted, then the backend rejects it without partial posting.

### 7. Verification

- Domain/scoring tests for every cardinality, residual, fee, rounding, stale-version, replay, rejection, and reversal path.
- SQL atomicity and concurrency tests, tenant/authorization tests, performance tests for candidate queues, and UI/client tests.

### 8. Definition of done

- Complex settlements reconcile deterministically and explainably with one auditable result graph and no client-side matching authority.

---

## Prompt 5 — Governed transfers, fees, interest, cards, and payout settlement

### 1. Title and outcome

Model common bank movements as first-class accounting sources so transfers, fees, interest, card settlements, and payout batches post and reconcile correctly.

### 2. Current context

- Bank transactions, cash settlement posting, account roles, suspense correction, payments, and immutable journals exist.
- Common treasury movements are not represented by dedicated source aggregates and risk being treated as generic categorized transactions.

### 3. Dependencies

- P2 Prompt 4.
- P3 currency work is required before enabling cross-currency transfers; until then they must be blocked explicitly.

### 4. Implementation requirements

- Add narrowly owned transfer, bank-charge/interest, card-settlement, and payout-settlement aggregates with source identities, lifecycle, evidence, approvals where material, and accounting policy inputs.
- Support two-sided account transfers, in-transit state, provider timing differences, fees, settlement batches, payout gross/net breakdown, and linked merchant/card evidence.
- Add deterministic posting previews and route all final journals through `IAccountingPostingService`; retain bank-row and settlement reconciliation links.
- Add correction/reversal flows, typed reason codes, queries/APIs, audit, telemetry, and reconciliation/control-account checks.
- Extend transaction/reconciliation detail UI without creating a competing treasury ledger.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- A transfer must never be recorded as income/expense merely because only one bank leg has arrived; keep an explicit in-transit/unmatched state.
- Do not represent card authorization as settled cash or provider payout acceptance as bank settlement.

### 6. Acceptance criteria

- Given both legs of an internal transfer, when matched, then cash accounts and any in-transit account reconcile without income or expense.
- Given a net card payout, when reconciled, then gross receivable, fees, cash, and evidence balance to the provider settlement.
- Given only one leg or an ambiguous payout, when processed, then the item remains visible and no invented counterpart is posted.

### 7. Verification

- Policy/posting tests, delayed/missing-leg tests, batch and fee fixtures, reversals, idempotency, tenant isolation, and SQL atomicity.
- Report/control-reconciliation regressions and focused UI/browser tests.

### 8. Definition of done

- Supported treasury movements have dedicated, reconcilable source lifecycles and deterministic postings without generic categorization shortcuts.

---

## Prompt 6 — Native payment batches and approval-bound instructions

### 1. Title and outcome

Create native payment batches that turn eligible supplier/refund obligations into reviewed, approved, immutable payment instructions without yet claiming bank execution.

### 2. Current context

- Supplier payment proposals, customer refund workflows, approvals, payment records, allocations, and Fortnox payment export exist.
- There is no provider-neutral native batch with beneficiary evidence, due-date optimization, segregation of duties, instruction versions, or batch-level reconciliation.

### 3. Dependencies

- P2 Prompts 1 and 5.
- P1 native payables supplier master/payment-detail verification must be complete for supplier instructions; Release 2 refund obligations remain supported.
- Decide supported payment rails, cut-off/calendar policy, and approval thresholds.

### 4. Implementation requirements

- Add payment batch, obligation link, beneficiary snapshot, instruction, approval binding, validation result, export artifact, and operation/idempotency records.
- Build eligibility and optimization policy for due dates, discounts, holds, disputes, currency, cash availability, duplicate obligations, verified payment details, and segregation of duties.
- Support create, add/remove, preview, validate, submit, approve/reject, cancel-before-send, and regenerate-before-approval with optimistic concurrency.
- Snapshot beneficiary and source versions; any material obligation or payment-detail change invalidates approval and generated artifacts.
- Add authorized commands/queries/APIs, typed Web clients, audit, telemetry, and a payment-batch workspace entry point.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- This prompt creates no bank side effect. `approved` means internally approved, never submitted or paid.
- No instruction may include unverified, cross-company, held, disputed, already-settled, or stale beneficiary/source data.

### 6. Acceptance criteria

- Given eligible obligations, when a batch is approved, then exact source and beneficiary versions are retained and totals reconcile by currency.
- Given changed bank details or obligation state, when send readiness is checked, then approval is stale and execution is blocked.
- Given replayed create/submit commands, when payloads match, then one batch chain exists; changed payloads conflict.

### 7. Verification

- Eligibility, optimization, totals, approval invalidation, concurrency, idempotency, and segregation-of-duties tests.
- API authorization/tenant tests, migration/index tests, and payment workspace client/component tests.

### 8. Definition of done

- Native batches are production-complete through internal approval, with immutable instructions and evidence, but no false bank-submission state.

---

## Prompt 7 — Durable payment submission, acknowledgement, and settlement

### 1. Title and outcome

Execute approved payment batches through a selected provider with durable submission, polling/webhook acknowledgement, rejection/cancellation handling, and settlement reconciliation.

### 2. Current context

- Finance provider writes already use approval, idempotency, retries, acknowledgement/reconciliation patterns for Fortnox and invoice delivery.
- Prompt 6 supplies current approved native instructions. The selected bank/provider adapter from Prompt 1 supplies consent and capabilities.

### 3. Dependencies

- P2 Prompt 6.
- Selected provider payment-initiation or file-submission credentials, sandbox, webhook/security contract, status semantics, cut-offs, and operational ownership.

### 4. Implementation requirements

- Add provider-neutral submit/cancel/status contracts and implement the selected production adapter, including signed webhook/replay protection when supported.
- Queue submission through durable outbox/background work, recheck current approval, batch/instruction versions, consent, capabilities, beneficiary evidence, and cash/account authority immediately before send.
- Persist attempts, provider instruction/batch IDs, request hashes, acknowledgements, status history, rejection details, retry classification, cancellation boundaries, and ambiguous reconciliation state.
- Poll or ingest statuses until terminal/reconciliation-required; create payments/allocations and ledger effects only from authoritative supported events, then match bank settlement evidence.
- Add operator recovery, remittance generation/delivery, audit, telemetry, safe user messages, and runbook procedures.

### 5. Constraints and preservation rules

- Follow the Shared execution contract and the external-side-effect rules in `docs/architecture-rules.md`.
- HTTP acceptance is not bank acceptance; bank acceptance is not beneficiary receipt; neither is final settlement.
- Never blindly retry an ambiguous provider outcome or cancel after the provider's safe cancellation boundary.

### 6. Acceptance criteria

- Given a current approved batch, when submitted, then one provider business instruction exists across retries and every acknowledgement is retained.
- Given rejection, partial acceptance, timeout, webhook replay, or provider-success/local-failure, when handled, then the exact state and operator action are visible without duplicate money movement.
- Given settled bank rows, when reconciliation completes, then payments, allocations, journals, remittance, and source evidence agree.

### 7. Verification

- Provider contract, signature/replay, idempotency, retry classification, ambiguity, cancellation, partial-batch, and settlement tests.
- SQL Server transaction/worker-restart tests, authorization and tenant-isolation tests, and real-provider sandbox evidence.
- Runbook recovery drill and reporting/reconciliation regressions.

### 8. Definition of done

- Approved instructions progress honestly from queued through provider acknowledgement to settlement, with no direct request-handler money movement or unhandled intermediate state.

---

## Prompt 8 — Daily cash operations and treasury workspace

### 1. Title and outcome

Create one daily cash workspace that turns feed health, unreconciled items, expected flows, liquidity risk, and payment work into prioritized finance actions.

### 2. Current context

- Finance has cash position, transactions, payments, reconciliation, issues, insights, tasks, worker operations, and Laura analysis surfaces.
- These views are separate and do not yet provide a single operational treasury queue with feed coverage and instruction status.

### 3. Dependencies

- P2 Prompts 1–7.

### 4. Implementation requirements

- Add a bounded backend read model for account balances/evidence timestamps, feed health/gaps, unreconciled aging, expected inflows/outflows, approved/queued/rejected payments, liquidity thresholds, short-horizon projections, and prioritized tasks.
- Extend backend policy to return allowed actions and stable reasons for reconnect, recover gap, reconcile, review payment, cancel where allowed, and investigate liquidity.
- Build the consolidated Finance Cash/Payments experience using existing routes or compatibility redirects; preserve deep links to source evidence and operator recovery.
- Integrate Laura's evidence-grounded cash/payables/receivables analysis in recommend-only mode with cited records and missing-evidence disclosure.
- Add responsive/loading/empty/error/stale-data states, keyboard/screen-reader support, English/Swedish localization, telemetry, and usage metrics.

### 5. Constraints and preservation rules

- Follow the Shared execution contract. The mandatory screenshot-first workflow in `docs/design.md` applies.
- UI does not recalculate balances, eligibility, reconciliation, or payment permissions.
- Keep administrative connection configuration separate from daily treasury work.

### 6. Acceptance criteria

- Given a finance user starting the day, when the workspace loads, then every connected account shows source freshness and the highest-priority actionable exceptions.
- Given an ambiguous submission or missing feed range, when displayed, then the state cannot be mistaken for success and links to the correct recovery flow.
- Given narrow viewport or Swedish locale, when used, then the full review/action flow remains accessible and understandable.

### 7. Verification

- Read-model, permissions, bounded-query, source-freshness, and tenant tests.
- Typed client/component tests plus authenticated desktop/narrow English/Swedish browser UAT and accessibility checks against the generated reference.
- Performance measurement using supported-volume profiles.

### 8. Definition of done

- Daily treasury work is operable from one evidence-backed workspace without duplicating backend policy or hiding provider/reconciliation failures.

---

## Prompt 9 — Connected banking production proof and operations

### 1. Title and outcome

Prove the connected-banking release under production-shaped failure, recovery, security, volume, and provider conditions and publish an evidence-backed go/no-go decision.

### 2. Current context

- Releases 0–2 provide the production test matrix, capacity profiles, worker operations, accounting readiness, backup/restore tooling, and release-evidence conventions.
- Prompts 1–8 add consent, feeds, files, advanced reconciliation, treasury sources, payment batches, execution, and daily operations.

### 3. Dependencies

- P2 Prompts 1–8.
- Dedicated SQL Server and Docker SQL Server, coordinated object storage, selected provider sandbox/non-production credentials, and an owned authenticated browser environment.

### 4. Implementation requirements

- Add connected-banking readiness checks for consent expiry, feed gaps/lag, duplicate identity, unreconciled aging, suspense, stale approvals, ambiguous submissions, rejected instructions, unsettled batches, worker backlog, and control-account differences.
- Extend capacity profiles and the test matrix for transaction/feed volume, matching candidates, payment batches, webhook throughput, cursor recovery, and treasury read models.
- Run fresh install, representative upgrade, backup/restore, process-death, expired lease, cursor regression, webhook replay, provider timeout, ambiguous success, and provider-success/local-failure scenarios.
- Complete authenticated English/Swedish browser UAT and real-provider sandbox ingestion/submission/acknowledgement evidence without claiming beneficiary settlement unless actually observed.
- Publish deployment, feature-control, credential rotation, incident, reconciliation, rollback/forward-fix, retention, and disaster-recovery runbooks plus a signed-off go/no-go evidence document.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Missing external evidence is a visible release stop, not a skipped pass.
- Rollback preserves imported rows, instructions, provider identities, acknowledgements, payments, journals, reconciliation decisions, audit, and object hashes; forward-fix instead of destructive cleanup.

### 6. Acceptance criteria

- Given an interrupted feed, when recovered, then source coverage is gap-free and every transaction is imported once.
- Given duplicate or ambiguous payment delivery, when workers restart, then no duplicate money movement occurs and operator reconciliation is explicit.
- Given a release candidate, when the complete matrix runs, then every prerequisite and result is recorded and the go/no-go decision follows the evidence.

### 7. Verification

- Full solution build; hermetic, SQL Server, Docker migration/restore, performance, browser, and approved real-provider lanes.
- Security review for tokens, callbacks, webhooks, certificates, logs, exports, and tenant boundaries.
- Recovery checksum comparison across database and object storage.

### 8. Definition of done

- Connected Banking and Treasury has measurable release evidence, operator ownership, and no unresolved critical/high defect or falsely green external lane.
