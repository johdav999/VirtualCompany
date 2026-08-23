# Financial App Release 1 Implementation Prompts

Release: Sweden Statutory Foundation  
Source roadmap: [financial-roadmap.md](financial-roadmap.md)  
Prompt order: execute Prompts 1–7 in order. Do not stop at intermediate checkpoints unless genuinely blocked by the external reviewer dependency identified in Prompt 7.

## Shared execution contract

Every prompt in this document is an implementation prompt, not an analysis or scaffolding task.

- Read and follow `production-implementation.md`, `docs/architecture-rules.md`, the repository `AGENTS.md`, and the relevant current code before editing.
- `architecture-inst.md` is required by the workspace instructions when present, but it was not present when this pack was generated. If it exists at execution time, read and follow it; do not invent a substitute. `docs/architecture-rules.md` remains mandatory.
- For UI work, read and follow `ui-instructions.md` and `docs/design.md`. Complete the mandatory screenshot-first workflow before implementing a new or materially redesigned screen, save the reference under `docs/design/references/`, and visually compare the implementation with it.
- Existing repository behavior wins over older planning text. Preserve existing country-neutral companies, Fortnox flows, provider migration, simulation behavior, routes, wire values, and migrations unless the prompt explicitly changes them.
- Keep Domain/Application/Infrastructure/Persistence/API/Web ownership boundaries. Put Finance contracts under `VirtualCompany.Application/Finance/Contracts`, Finance implementations and adapters under `VirtualCompany.Infrastructure.Finance`, EF configurations under `VirtualCompany.Persistence/Persistence/Configurations`, migrations under `VirtualCompany.Persistence.Migrations`, transport-only actions in capability partial controllers, and endpoint knowledge in typed Web clients.
- Every tenant-owned record, relationship, query, command, worker claim, cache key, audit event, export, and object-storage reference must carry and enforce company scope. Add cross-company read and write tests.
- Posted journals are immutable. Corrections use reversals, linked replacements, effective dates, and versioned policy facts. All postings continue through `IAccountingPostingService`.
- Important external effects use durable outbox/background execution with stable business idempotency, bounded retry, acknowledgement/reconciliation state, safe failures, audit, and telemetry. Never report an ambiguous external result as success.
- Use SQL Server EF migrations for schema changes. Preserve both local SQL Server and Docker SQL Server restore/run compatibility and finish with no pending model change.
- Do not fabricate Swedish law, tax treatment, statutory formats, reviewer sign-off, provider responses, production data, or compliance claims. Persist explicit unsupported/unverified states. A pack must remain `IsStatutoryComplianceValidated = false` until Prompt 7 has real review evidence.
- Keep user-facing text plain English and localized in English and Swedish. Do not expose storage values, reason codes, or provider internals.
- Each prompt must deliver production implementation with no in-scope TODOs, mock production paths, silent failures, or unhandled intermediate states. Run focused tests, affected API/Web builds, migration checks where applicable, and broader accounting regressions before completing it.

---

## Prompt 1 — Swedish statutory company profile and versioned policy-pack foundation

### 1. Title and outcome

Implement the durable Swedish statutory identity and policy-pack foundation so a company can configure the legal facts and versioned accounting rules needed by later VAT, invoice, export, and filing workflows without falsely claiming compliance.

### 2. Current context

- `Company` in `VirtualCompany.Domain/Entities/TenantEntities.cs` stores workspace-level name, currency, language, timezone, and `ComplianceRegion`, but not a complete legal/statutory identity.
- `AccountingConfiguration`, `AccountingPolicyPackSelection`, `IAccountingPolicyPack`, and `AccountingPolicyPackResolver` already provide effective-dated, hashed pack selection and historical provenance.
- `CountryNeutralAccountingPolicyPack` and `CountryNeutralBankingAccountingPolicyPack` are the only registered packs. They correctly set `IsStatutoryComplianceValidated = false`.
- `AccountingAdministrationService` already previews and completes setup, creates chart accounts, fiscal periods, and voucher series.
- Accounting setup UI and typed clients exist under `VirtualCompany.Web/Pages/Finance/AccountingSetupPage.razor` and `VirtualCompany.Web/Services/FinanceApiClient.AccountingConfiguration.cs` / `AccountingAdministration.cs`.
- Known gap: there is no durable Swedish legal profile or Swedish policy pack.

### 3. Dependencies

- Release 0 Accounting Core GA must be complete or its unresolved release blockers must be explicitly tracked and must not be worsened.
- None of the later Release 1 prompts.
- Qualified reviewer sign-off is not required for this prompt; the new pack must remain unvalidated until Prompt 7.

### 4. Implementation requirements

- Add a company-scoped statutory profile aggregate for legal name, Swedish organisation number, VAT registration number/status, registered and correspondence addresses, country code, accounting currency, fiscal-year basis, bookkeeping method where applicable, registration effective dates, and verification/source metadata.
- Normalize and validate structure deterministically without pretending to validate government registration. Separate format validity, user attestation, and externally verified state.
- Add application commands/queries and finance-admin/view authorization for create/update/read, with optimistic concurrency, audit before/after evidence, safe problem responses, and plain explanations.
- Add a versioned Swedish policy pack with an initial reviewed-data candidate: Swedish region metadata, chart-template identifier, account roles, invoice-policy hooks, retention/lock metadata, supported capability flags, and explicit placeholders only as `unsupported`/`unverified` data—not code TODOs or compliance claims.
- Use a production data resource or focused class structure for the chart/policy definition; do not create a giant catch-all service or embed important queryable company state only in JSON.
- Register the pack through `FinanceModuleRegistration`, validate duplicate pack keys/versions/hashes at startup, and preserve the country-neutral packs unchanged.
- Extend setup preview/status contracts to show statutory profile completeness, pack validation state, missing legal facts, and safe next actions.
- Add an additive EF migration and tenant-scoped keys/indexes. Document local and Docker migration/restore implications.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Do not put jurisdiction-specific rules into `AccountingPostingService`; resolve them through the selected pack/policies and persist pack/version facts on resulting journals.
- Do not overload the general `Company` entity with the entire statutory aggregate if a company-owned Finance entity is the narrower owner.
- Existing country-neutral setup and companies must continue to run without Swedish fields.
- Do not mark the Swedish pack statutorily validated and do not claim BAS/Swedish legal certification before Prompt 7.

### 6. Acceptance criteria

- Given a company with no statutory profile, when setup status is queried, then the API returns explicit missing Swedish legal facts without changing the company.
- Given valid formatted statutory-profile input from an accounting admin, when it is saved, then the profile is company-scoped, versioned, audited, and returned without leaking another company's data.
- Given a country-neutral company, when the migration is applied, then its accounting configuration, postings, reports, and setup behavior remain unchanged.
- Given the Swedish candidate pack, when the catalog starts, then its definition hash is deterministic and it reports `IsStatutoryComplianceValidated = false`.
- Given stale profile or configuration versions, when an update is attempted, then the server returns a conflict and creates no partial update.

### 7. Verification

- Domain validation and pack-hash tests in `VirtualCompany.Finance.Tests`.
- API authorization, stale-version, and cross-tenant tests in `VirtualCompany.Api.Tests`.
- EF migration metadata and SQL Server apply/upgrade tests; verify no pending model changes.
- Regression tests for country-neutral setup, posting, reports, and provider switching.
- API and Web builds; no UI redesign is required in this prompt beyond contract-compatible status data.

### 8. Definition of done

- The statutory profile, candidate Swedish pack, contracts, services, authorization, persistence, migration, audit, telemetry, tests, and operator documentation are production-complete.
- No Swedish compliance claim is enabled; the UI/API explain that reviewer validation is pending.
- No scaffolding, mock legal data, silent fallback, unhandled state, or deferred in-scope TODO remains.

---

## Prompt 2 — Swedish chart, account roles, and deterministic VAT posting policy

### 1. Title and outcome

Implement a production Swedish chart/account-role definition and deterministic effective-dated VAT policy so supported customer and supplier transactions generate balanced, explainable journals with retained VAT facts.

### 2. Current context

- Prompt 1 adds the Swedish candidate pack and statutory company profile.
- `AccountingPolicyPackDefinition` already contains chart templates, account roles, tax rules, invoice policy, reporting mappings, retention policy, exports, and capabilities.
- `CustomerInvoiceAccountingPolicy` and `SupplierBillAccountingPolicy` already preview source-document journals and reject unavailable tax rules.
- `ProposedAccountingLine` carries `TaxFacts`; `AccountingPostingService` persists `TaxFactsJson`, policy-pack key/version, and immutable journal evidence.
- `AccountingReportingService.GetTaxSummaryAsync` currently groups generic tax facts but is not a Swedish VAT return engine.
- Country-neutral posting and banking packs must remain operational.

### 3. Dependencies

- Release 1 Prompt 1.
- An approved, repository-stored source specification for the Swedish chart subset and VAT cases being implemented. If authoritative rule inputs are missing, implement no invented rates/treatments and stop with explicit unsupported cases.

### 4. Implementation requirements

- Complete the first Swedish chart template with stable BAS-style account codes/names, account classes, normal balances, reporting placement, required control roles, VAT control roles, rounding, exchange, suspense, revenue, expense, equity, and bank roles needed by supported flows.
- Define effective-dated VAT rule records for the launch scope, including rate, direction/treatment, taxable basis method, output/input accounts, box mapping, recoverability, reverse-charge counterpart facts where supported, and explicit jurisdiction/transaction applicability.
- Introduce a deterministic application policy that resolves a tax rule from selected pack/version, accounting date, company registration state, counterparty jurisdiction/status, document type, line classification, and supplied evidence. It must return allowed/blocked, stable reason codes, explanation, required evidence, calculated tax, accounts, and VAT boxes.
- Update customer- and supplier-document accounting policies to consume the authoritative VAT decision rather than duplicating Swedish rules.
- Persist sufficient immutable tax facts on each posted journal line to reproduce rate, basis, amount, treatment, box mapping, rule key/version, and evidence classification without querying today's pack.
- Support corrections/credit notes by reversing original VAT facts and applying a linked replacement where required; never edit original tax facts.
- Explicitly block unsupported domestic/EU/non-EU, import, reverse-charge, partial-recovery, cash-method, or mixed-use cases not present in the approved launch specification.
- Add configuration validation for all referenced accounts, balanced VAT postings, mutually exclusive applicability, effective-date continuity, and duplicate rule keys.
- Add audit/telemetry for blocked tax decisions without logging source-document contents or tax identifiers unnecessarily.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Tax authority belongs in deterministic backend policy, never UI or an LLM.
- Do not make `AccountingPostingService` jurisdiction-aware; it validates and persists the already-authorized proposal.
- Preserve original source amounts and evidence. Rounding must use the selected accounting configuration and be explicit in the journal.
- The pack remains unvalidated until Prompt 7 even when implementation tests pass.

### 6. Acceptance criteria

- Given each approved Swedish launch fixture, when customer or supplier accounting is previewed, then the expected taxable basis, VAT amount, accounts, box mappings, rounding, and balanced journal lines are returned.
- Given an unsupported or insufficiently evidenced tax scenario, when preview or submission is attempted, then posting is blocked with an actionable reason and no tax treatment is invented.
- Given a posted document and later pack upgrade, when historical tax facts are queried, then the original rule key/version and calculated facts remain unchanged.
- Given a credit note, when it is posted, then original VAT is corrected through linked accounting entries and the audit trail remains complete.
- Given another company or stale source version, when tax accounting is attempted, then access/version checks fail before any journal is created.

### 7. Verification

- Golden policy fixtures for every supported VAT case and negative fixtures for every declared unsupported boundary.
- Customer/supplier policy, posting, correction, rounding, effective-date, and pack-upgrade tests in `VirtualCompany.Finance.Tests`.
- API authorization, tenant isolation, idempotency, stale source, and approval tests.
- SQL Server atomic posting/rollback and migration compatibility tests if schema changes.
- Regression tests for country-neutral tax-free posting, banking, manual journals, reports, and provider sync.

### 8. Definition of done

- The Swedish chart and VAT policy are complete for the explicitly supported launch scope, deterministic, versioned, evidence-backed, and wired through both AR/AP accounting paths.
- All unsupported cases fail visibly and safely.
- No duplicate jurisdiction logic, mock tax result, silent generic fallback, or in-scope TODO remains.

---

## Prompt 3 — Swedish statutory document rules and numbering controls

### 1. Title and outcome

Enforce Swedish launch-scope invoice and credit-note content, dates, sequencing, retention metadata, and correction links at the authoritative backend boundary so incomplete or incorrectly numbered documents cannot be issued or posted.

### 2. Current context

- `AccountingInvoicePolicyDefinition` supports required fields, sequential numbering, and supported document types.
- `VoucherSeries`/`VoucherSequence` already allocate governed journal numbers, but `FinanceInvoice.InvoiceNumber` is a supplied string and there is no complete native issuance sequence.
- Customer and supplier accounting profiles already retain source versions, line inputs, approval links, and resulting ledger entry IDs.
- Existing Fortnox-synced documents have provider-issued numbers and must remain readable.
- Prompt 1 adds statutory company identity; Prompt 2 adds Swedish VAT policy and facts.

### 3. Dependencies

- Release 1 Prompts 1–2.
- Approved launch-scope statutory document-field and numbering specification.

### 4. Implementation requirements

- Add a deterministic statutory document policy for customer invoices, customer credits, supplier invoices, and supplier credits, distinguishing imported/provider-issued documents from future native-issued documents.
- Validate seller/buyer identity, addresses, VAT identifiers where applicable, issue/supply/accounting/due dates, currency, line descriptions, quantities/prices where represented, net/VAT/gross totals, payment terms, original-document reference for credits, and required explanatory text.
- Add durable, company-scoped, fiscal-year-aware native customer document series and transactional number allocation. Preserve gaps with an operator-visible reason; never reuse an allocated/issued number.
- Separate draft identity from issued number. Preview must not consume a number; the issue transaction must atomically allocate the number, freeze the issued snapshot, and create the source/version identity used by posting.
- Store a bounded immutable issued-document snapshot/hash and links to statutory profile version, policy-pack version, tax facts, approvals, and later rendered/delivery evidence.
- Enforce corrections through linked credit/correction documents and ledger reversals; prohibit destructive edits to issued facts.
- Add admin APIs for series configuration and read APIs for allocation history/gaps, with authorization, audit, optimistic concurrency, and plain errors.
- Add migration/indexes enforcing tenant-scoped uniqueness for series/year/number and issued document identity.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Do not renumber existing Fortnox/provider documents. Record their number authority and source explicitly.
- Do not combine customer document numbering with journal voucher numbering; they have distinct legal and operational meaning.
- Keep native issuance disabled until the company has complete statutory identity and a compatible selected pack.
- No UI implementation beyond API contracts is required here; Release 1 Prompt 6 adds configuration/review surfaces.

### 6. Acceptance criteria

- Given a valid native issue command, when two concurrent requests execute, then each issued document receives one unique sequential number and no partial issued snapshot exists.
- Given an idempotent replay, when the same business key and source version are submitted, then the original issued result is returned without consuming another number.
- Given missing statutory fields or mismatched totals, when issue/post is attempted, then the policy blocks it before number allocation or posting.
- Given an issued document, when an edit is attempted, then immutable facts are unchanged and the API directs the operator to a correction/credit workflow.
- Given provider-issued historical documents, when they are read or migrated, then their original numbers and authority remain unchanged.

### 7. Verification

- Domain and policy tests for all required fields, date/totals rules, imported/native distinctions, and correction links.
- SQL Server concurrency tests for numbering allocation, rollback, uniqueness, and idempotent replay.
- API authorization, tenant-isolation, stale-version, and problem-contract tests.
- EF migration apply/upgrade plus pending-model check on local and Docker-compatible SQL Server paths.
- Regression tests for voucher sequences, Fortnox sync, provider migration, customer/supplier accounting, and credit notes.

### 8. Definition of done

- Statutory document policy, native number series, immutable issue snapshots, APIs, persistence, migration, audit, telemetry, tests, and operator guidance are complete.
- No issued document can be silently edited, duplicated, or renumbered.
- No fake statutory validation or deferred in-scope TODO remains.

---

## Prompt 4 — Durable Swedish VAT return calculation, review, and correction workflow

### 1. Title and outcome

Implement a durable VAT return workspace that derives return boxes from immutable posted tax facts, reconciles to the ledger, supports review/approval/locking, and handles corrections without pretending to submit to an unconfigured authority.

### 2. Current context

- `AccountingReportingService` currently exposes a country-neutral tax summary and `AccountingTaxReview` records one review snapshot.
- Period close already checks `tax_review_incomplete` and supports close/lock/reopen history.
- Prompt 2 persists Swedish VAT box facts and Prompt 3 enforces statutory source-document rules.
- The approval, task, audit, background worker, and document/object-storage systems already exist.
- There is no durable VAT-return aggregate, filing period lifecycle, box-level reconciliation, correction chain, or filing package.

### 3. Dependencies

- Release 1 Prompts 1–3.
- Approved mapping of supported VAT facts to launch-scope return boxes and rounding rules.

### 4. Implementation requirements

- Add company-owned VAT return, box result, source contribution, validation issue, review/approval, and correction-link records with clear draft/calculated/needs-review/approved/locked/corrected states.
- Define filing periods independently but consistently with fiscal periods; support only deterministic supported overlaps and block ambiguous period configuration.
- Calculate from posted immutable tax facts as of a stable cutoff. Persist input hash, pack versions, source counts, per-box amounts, reconciliation totals, and included/excluded source references.
- Validate required boxes, sign conventions, currency, rounding, duplicate source inclusion, VAT control-account reconciliation, period locks, unresolved tax conflicts, and pack-version compatibility.
- Make recalculation idempotent. Any source change after calculation must mark the return stale and invalidate prior approval; locked/previously filed evidence must remain immutable.
- Route review/approval through existing approval infrastructure with current-evidence recheck immediately before lock/finalization.
- Produce a real, documented human-filing package for the selected launch process (for example a reviewed PDF/CSV/JSON package if no authority API exists), with checksum and source manifest. Do not implement a fake submit button or provider success.
- Support correction returns as linked new versions with reason/evidence; never mutate the finalized original.
- Extend close readiness so an applicable period cannot close with a missing, stale, blocking, or unreviewed VAT return according to configured policy.
- Add background recalculation only if needed for bounded volume; otherwise keep the command transactional and bounded. Never calculate unbounded work inline.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- A VAT return is a read-derived statutory artifact; it must not post journals by itself.
- Do not infer missing tax facts from account names or amounts. Surface missing classification as blocking review work.
- Do not call a tax authority unless a verified provider and credentials are explicitly configured in a later authorized prompt.
- Historical finalized returns and their source manifests are immutable.

### 6. Acceptance criteria

- Given approved VAT fixtures and posted journals, when a return is calculated, then every box and reconciliation total matches the golden expected result and source drill-down.
- Given a source journal change after calculation, when status is queried, then the return is stale, approval is no longer executable, and recalculation is the next action.
- Given unresolved or unsupported tax facts, when approval is requested, then it is blocked with specific source references and no values are invented.
- Given a current approved return, when it is finalized, then its package, checksum, input hash, actor, approval, pack versions, and source manifest are retained.
- Given a finalized return requiring correction, when corrected, then a linked replacement version is created and the original remains unchanged.

### 7. Verification

- Golden box-calculation, rounding, sign, currency, reconciliation, stale-evidence, approval, lock, and correction tests.
- Tenant-isolation and authorization tests for all return reads/writes/downloads.
- Migration, uniqueness, concurrency, idempotency, and SQL Server upgrade tests.
- Close-readiness regressions and country-neutral behavior tests.
- Export/package checksum and object-storage recovery tests.

### 8. Definition of done

- VAT return persistence, deterministic calculation, validation, approval, finalization, correction, package generation, close integration, APIs, audit, telemetry, migration, tests, and runbook are production-complete.
- Submission capability is described honestly; no fake authority integration or silent omission exists.
- No in-scope TODO or unhandled return state remains.

---

## Prompt 5 — SIE export and statutory accounting archive

### 1. Title and outcome

Implement a standards-conformant, reproducible SIE export and durable statutory archive so Swedish books can be transferred and audited with exact source, policy, and checksum evidence.

### 2. Current context

- `AccountingReportingService` already creates durable background accounting exports containing country-neutral JSON and attachment manifests.
- `AccountingExportJob`, `AccountingExportBackgroundService`, object storage, hashes, and download authorization already exist.
- General ledger, accounts, fiscal periods, journals, tax facts, dimensions, evidence, and policy-pack history are queryable.
- Prompt 4 adds finalized VAT packages.
- Current supported exports are `generic_csv` and `generic_json`; there is no SIE implementation.

### 3. Dependencies

- Release 1 Prompts 1–4.
- A checked-in, versioned SIE format specification and representative accountant-validated fixtures for the chosen SIE version.

### 4. Implementation requirements

- Add a Swedish statutory export type and deterministic SIE serializer for the selected version, including required program/file metadata, company identity, fiscal years, chart/accounts, opening/closing balances, dimensions where supported, vouchers, lines, dates, text encoding, signs, and stable ordering.
- Validate source completeness before generation: selected pack/version, statutory profile, period boundaries, balanced journals, voucher identity, account mapping, supported currency/dimensions, and required metadata.
- Generate through the existing durable export worker, with stable business idempotency, lease/retry behavior, safe permanent/transient classification, expiry policy, and operator-visible failures.
- Persist export specification version, input checksum, output checksum, content length, file name, encoding, source counts/totals, actor, correlation, and attachment/evidence manifest.
- Add a statutory archive bundle containing the SIE file, finalized VAT package references, financial statements, close history, policy-pack definitions/hashes, source/evidence manifest, and machine-readable checksum manifest. Avoid duplicating document binaries when stable immutable object references are sufficient and recoverable.
- Add import/round-trip validation for test fixtures without making imported SIE a production ingestion feature in this prompt.
- Extend export list/download APIs and capability decisions; keep generic exports backward compatible.
- Update recovery verification and both local/Docker restore runbooks to cover statutory archive metadata and object hashes.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- Do not label a generic CSV as SIE. Encoding, ordering, required fields, and version must follow the selected specification.
- Export generation is read-only with respect to journals and returns.
- Never expose another company's exports or object keys; downloads require accounting-view authorization and current company scope.
- If source facts cannot be represented in the selected SIE version, fail with an explicit capability gap rather than dropping them.

### 6. Acceptance criteria

- Given each golden fixture, when SIE is generated, then an independent parser reads it and all accounts, periods, vouchers, lines, balances, and totals match expected values.
- Given the same immutable inputs, when export is replayed, then the same logical content/checksum is returned without duplicate jobs or objects.
- Given an unsupported dimension/currency or missing statutory identity, when export is requested, then it fails safely before publishing a downloadable artifact.
- Given restored SQL and object storage, when recovery verification runs, then archive manifests and every referenced object checksum match.
- Given a user from another company, when an export ID is requested, then no metadata or file is disclosed.

### 7. Verification

- Serializer unit/golden tests and independent parser/round-trip tests.
- Background lease, retry, duplicate, failure, expiry, and object-write ambiguity tests.
- API authorization, tenant-isolation, download, and problem-contract tests.
- SQL Server migration/recovery and local/Docker restore tests.
- Regressions for generic JSON/CSV exports, reports, close, and provider-switch archive evidence.

### 8. Definition of done

- SIE export, statutory archive, worker integration, contracts, APIs, persistence, migration if needed, recovery verification, tests, and runbook are production-complete.
- The implementation identifies its exact SIE version and validation fixtures.
- No silently dropped fact, mock artifact, or deferred in-scope TODO remains.

---

## Prompt 6 — Swedish accounting setup, VAT, and statutory export user experience

### 1. Title and outcome

Deliver a coherent Swedish accounting experience so authorized users can complete legal setup, understand compliance readiness, review VAT returns, generate statutory exports, and act on blockers without seeing internal implementation terminology.

### 2. Current context

- Accounting setup/accounts/periods/reports pages and `AccountingNavigation` already exist.
- `AccountingReportsPage` shows trial balance, general ledger, P&L, balance sheet, a country-neutral tax summary, close checklist, and export jobs.
- Prompts 1–5 add statutory profile, Swedish pack readiness, VAT return APIs, SIE/archive exports, and safe allowed actions.
- `FinanceApiClient.AccountingConfiguration`, `.AccountingAdministration`, `.AccountingReports`, and the shared company transport are the established typed-client boundaries.
- English and Swedish resources and finance route patterns already exist.

### 3. Dependencies

- Release 1 Prompts 1–5.

### 4. Implementation requirements

- Before UI code, write the exact screenshot prompts and generate reference images for the materially changed Swedish setup and VAT/statutory reporting surfaces. Save them under `docs/design/references/` and record them in the relevant reference inventory.
- Extend the existing accounting setup flow rather than creating a parallel admin product. Add grouped steps for legal identity, fiscal/accounting settings, Swedish pack/chart, required account roles, VAT registration, document series, preview, warnings, and final confirmation.
- Show three distinct states plainly: format complete, user-attested, and externally/reviewer validated. Never imply government verification or statutory validation that is not present.
- Add a VAT workspace integrated into accounting reports: period selector, readiness summary, box results, source drill-down, reconciliation, blocking issues, stale state, review/approval, finalize/package download, correction history, and next action.
- Add SIE/statutory archive request, progress, failure, retry, expiry, and download UI using existing export patterns.
- Consume backend allowed-action/policy decisions; do not recreate statutory, tax, approval, or close rules in Blazor.
- Keep Laura visible as Finance Manager with grounded explanations and navigation to source records, but do not allow agent suggestions to finalize returns or issue compliance claims.
- Preserve current routes or add compatibility redirects through `FinanceRoutes`; use typed clients and `ICompanyApiTransport`.
- Add complete English and Swedish localization, accessible names/errors/statuses, keyboard operation, responsive behavior, and production-shaped empty/loading/error states without mock data.
- Compare rendered pages against reference images and refine layout, spacing, typography, hierarchy, table behavior, empty states, and narrow layouts.

### 5. Constraints and preservation rules

- Follow the Shared execution contract and mandatory screenshot-first workflow.
- Reuse the current app shell, accounting navigation, cards, tables, page layout, tokens, and typed API client registration.
- Do not add raw tax-rule keys, storage states, pack hashes, or internal reason codes to ordinary user-facing copy; expose technical evidence only in an intentional audit detail.
- Disabled buttons are not authorization; server policies remain authoritative.
- Do not mix Simulation Lab controls or seeded example data into production accounting pages.

### 6. Acceptance criteria

- Given an incomplete Swedish profile, when setup opens, then the user sees missing business facts and one safe next action without a false compliance badge.
- Given a calculated VAT return, when an authorized reviewer opens it, then boxes, totals, reconciliation, sources, status, approval requirements, and package actions are understandable and navigable.
- Given a stale or blocked return/export, when viewed, then the cause and remediation are visible and finalization/download controls reflect backend allowed actions.
- Given an accounting viewer versus admin/approver, when the same page loads, then read visibility and mutations match server authorization.
- Given English, Swedish, keyboard-only, and narrow-screen use, when accounting flows are exercised, then content remains complete and actionable.

### 7. Verification

- bUnit/component tests for setup states, VAT lifecycle, exports, authorization-driven actions, errors, empty states, localization, and responsive class/layout behavior.
- Web/API contract tests for every new typed client method and route.
- Authenticated browser verification against realistic persisted data, including screenshot comparison and accessibility checks.
- API and Web Release builds plus existing accounting surface regressions.
- Confirm reference images exist and are not shipped as UI assets.

### 8. Definition of done

- The screenshot-first references, setup/VAT/export UI, typed clients, routing, localization, accessibility, tests, and visual verification are complete.
- Every screen answers what is happening, what needs attention, and what the user should do next.
- No placeholder screen, mock production data, raw internal language, or in-scope TODO remains.

---

## Prompt 7 — Swedish accounting validation evidence and production release controls

### 1. Title and outcome

Complete the Swedish statutory release with qualified review evidence, executable golden scenarios, production operations controls, and an honest validation gate that enables only the exact reviewed pack version.

### 2. Current context

- Prompts 1–6 implement the candidate Swedish pack, VAT policy, document controls, VAT return workflow, SIE/archive exports, and user experience.
- `accounting-release-evidence.md`, `accounting-provider-switch-monitoring-release-evidence.md`, and `accounting-operations-runbook.md` establish existing evidence/runbook patterns.
- Recovery scripts support local and Docker SQL Server plus object-checksum verification.
- The candidate pack is intentionally `IsStatutoryComplianceValidated = false` until real external review exists.
- Latest checked-in monitoring evidence records unrelated repository-wide test failures that remain a release stop until resolved or governed under Release 0.

### 3. Dependencies

- Release 1 Prompts 1–6.
- Release 0 exit criteria.
- Real signed/attributable review evidence from a qualified Swedish accounting professional covering the exact pack definition, VAT fixtures, document rules, and SIE version. This cannot be fabricated. If absent, complete all technical validation but leave the pack unvalidated and report the release blocked.

### 4. Implementation requirements

- Store bounded, non-secret validation metadata for the exact pack key/version/hash: reviewer identity/reference, scope, date, evidence document reference/hash, approved fixtures, limitations, and expiry/review trigger if applicable.
- Make pack validation a deterministic startup/configuration decision tied to the exact immutable definition hash; any code/data change creates a new unvalidated version.
- Build a production-shaped golden scenario suite covering setup, all supported VAT sales/purchase/credit cases, numbering, posting, payments/allocations where relevant, VAT return, close, SIE/archive export, restore, and historical pack upgrade.
- Add explicit negative scenarios for every documented unsupported tax/document/export boundary and prove no generic fallback posts or files it.
- Run authorization, tenant isolation, concurrency, idempotency, worker restart, provider ambiguity, object-write ambiguity, migration, upgrade, recovery, and performance-volume tests.
- Add health/readiness signals for pack validity, statutory profile completeness, stale VAT returns, failed/expired exports, missing validation evidence, and unsupported configured capabilities.
- Update operator and release runbooks with deployment order, feature enablement, rollback/forward-fix policy, data recovery, reviewer revalidation triggers, known limitations, and customer-facing compliance wording.
- Produce checked-in release evidence with exact commands/results, migration order, local/Docker restore proof, browser evidence, golden fixture version/hash, residual risks, and release decision.
- Enable `IsStatutoryComplianceValidated` only in a new reviewed pack version whose exact hash matches the supplied review evidence. Do not mutate an already selected historical definition in place.

### 5. Constraints and preservation rules

- Follow the Shared execution contract.
- A passing test suite is not a substitute for qualified accounting review; qualified review is not a substitute for automated evidence.
- Never edit or overwrite prior policy-pack versions, historical tax facts, finalized returns, exports, or evidence.
- Application rollback keeps additive schema/evidence and disables new capabilities; do not drop statutory tables or renumber accounting records.
- Any unresolved critical/high accounting finding, missing review evidence, failed restore, pending model change, or unexplained accounting regression is a release stop.

### 6. Acceptance criteria

- Given exact reviewer evidence matching the candidate definition hash, when the reviewed pack version loads, then it reports statutory validation with retained scope/limitations and passes all golden scenarios.
- Given a one-byte/rule change to the pack, when startup validation runs, then the prior sign-off no longer validates the changed definition.
- Given missing reviewer evidence, when release readiness is queried, then the release remains blocked and no UI/API claims Swedish statutory validation.
- Given local and Docker restores of the coordinated SQL/object backup, when verification runs, then journals, tax facts, returns, SIE/archive objects, approvals, audits, and checksums match.
- Given every supported and unsupported fixture, when the full flow executes, then supported cases succeed deterministically and unsupported cases stop safely without invented treatment.

### 7. Verification

- Full Finance, affected API, Web, Web contract, migration, and restoration suites—not only focused filters.
- SQL Server fresh-install and representative-upgrade tests on local and Docker paths.
- Authenticated browser verification of setup, VAT review/finalization/correction, and export/recovery actions in English and Swedish.
- Failure injection for worker lease expiry, duplicate messages, timeouts, object persistence ambiguity, stale approvals, and pack hash mismatch.
- Performance tests at documented supported data volumes and final `dotnet ef migrations has-pending-model-changes` check.

### 8. Definition of done

- The exact reviewed Swedish pack version, validation evidence, golden scenarios, readiness controls, runbooks, restore proof, browser evidence, and release record are complete.
- The release decision is explicit. If external sign-off is absent, the technical work may be complete but the pack remains unvalidated and Release 1 is reported blocked rather than falsely complete.
- No critical/high unresolved accounting finding, silent failure, fake evidence, weakened test, or deferred in-scope TODO remains.
