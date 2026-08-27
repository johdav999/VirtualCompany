# Swedish accounting production-readiness implementation prompts

Execute these prompts in order. Every prompt must follow `/production-implementation.md`,
`/docs/architecture-rules.md`, and all applicable `AGENTS.md` files. UI work must also
follow `/docs/design.md` and `/ui-instructions.md`.

## Prompt 1 — Enforce accountant confirmation for BAS-derived account semantics

### Title and outcome

Require attributable confirmation of accounting classification and company suitability
before a BAS catalogue account can be created. This prevents application-generated
suggestions or missing legal-form metadata from being treated as BAS-supplied facts.

### Current context

`AccountingAdministrationService.CreateAccountFromCatalogAsync` currently falls back to
suggested class and normal balance for classes 1–7. The free BAS workbook contains no
account-class, normal-balance, or organization-type applicability fields. The catalogue
contracts and API endpoint already support controlled account creation and record the
catalogue key/version in audit metadata.

### Dependencies

None.

### Implementation requirements

- Extend the application command, API request, and Web request contracts with explicit
  confirmation fields for accounting semantics and company/legal-form suitability.
- Enforce both confirmations in the Finance application service before any account is
  created, regardless of whether suggested or overridden semantic values are used.
- Return stable, actionable Finance reason codes for missing confirmations.
- Preserve the existing requirement to supply class and normal balance when no suggestion
  exists and the existing source-name selection rule for duplicate BAS codes.
- Include confirmation state, selected semantic values, catalogue identity, and source hash
  in the account-created audit evidence without adding a database schema change.
- Update focused Finance and API tests for success, missing confirmations, duplicate names,
  authorization, and cross-company protection.

### Constraints and preservation rules

Keep the backend authoritative. Do not infer organization applicability from account codes,
names, or company names. Preserve the custom-account creation route and all existing tenant
and authorization boundaries.

### Acceptance criteria

- Given a valid BAS account and either confirmation is false, when creation is requested,
  then no account is created and an actionable validation reason is returned.
- Given explicit semantic values and both confirmations, when creation is requested, then
  the account is created once and audit evidence records the confirmed decisions.
- Given a class-8 account, when semantic values are missing, then creation remains blocked.

### Verification

Run focused Finance service tests and accounting-administration API integration tests.

### Definition of done

Production code and tests are complete with no TODOs, mock production data, or UI-only
enforcement.

## Prompt 2 — Deliver an accountant-facing BAS catalogue workflow

### Title and outcome

Add a practical, non-technical UI where an accountant can search the complete BAS 2026
catalogue, review source limitations, explicitly confirm decisions, and add an account to
the company's chart.

### Current context

The backend and typed Web client already expose BAS catalogue search and creation, while the
existing Chart of accounts page supports custom account administration. No user-facing BAS
catalogue screen currently calls those APIs. The required visual reference is
`/docs/design/references/bas-account-catalogue-reference.png`.

### Dependencies

Prompt 1.

### Implementation requirements

- Extend catalogue queries to expose whether an account already exists for the company and
  support an authoritative `exclude existing` filter before pagination.
- Add a Finance/Accounting BAS catalogue page linked from Chart of accounts.
- Show catalogue identity, source hash, limitations, code/name search, group and K2 filters,
  paging, existing-account state, hierarchy, duplicate source-name selection, semantic
  fields, and both required confirmations.
- Disable creation for read-only users and already-added accounts; enforce authorization on
  the backend as today.
- Handle loading, empty, error, unauthorized, validation, and successful creation states.
- Use localized English and Swedish user-facing text and responsive styling matching the
  reference screenshot and existing Finance components.
- Add component/surface and typed-client tests. Exercise the strongest safe user-flow check
  available locally.

### Constraints and preservation rules

The mandatory screenshot-first workflow in `/docs/design.md` applies and has been completed.
Do not present inferred semantics or organization suitability as BAS facts. Do not ship the
reference screenshot as a UI asset.

### Acceptance criteria

- Given an accountant with Accounting Admin permission, when they search `1510`, confirm
  both decisions, and submit, then the account is created and shown as already added.
- Given an ambiguous code such as `2087`, then a source name must be selected.
- Given a read-only user, then catalogue data is visible but creation is unavailable.
- Given K2-only or exclude-existing filters, then results and counts are filtered on the
  server before paging.

### Verification

Run Web tests, Finance tests, API integration tests, a Web build, and a browser/UAT check if
the application can be started safely.

### Definition of done

The accountant can complete the workflow without reading source code or JSON, with no
scaffolding, mock catalogue data, silent failures, or deferred in-scope TODOs.

## Prompt 3 — Bind the BAS catalogue into a new immutable policy-pack version

### Title and outcome

Introduce a new Swedish candidate policy-pack version whose deterministic definition hash
includes the exact BAS catalogue identity and hashes, while preserving the existing 1.1.0
version for compatibility.

### Current context

The current `sweden-statutory-candidate` 1.1.0 definition hash excludes the supplementary
BAS catalogue. Runtime catalogue loading checks fixed source and JSON hashes, but qualified
policy-pack evidence cannot currently prove which catalogue was reviewed.

### Dependencies

Prompts 1 and 2.

### Implementation requirements

- Preserve and register the existing immutable 1.1.0 definition and its hash.
- Introduce the next candidate version and include catalogue key, catalogue version,
  catalogue SHA-256, workbook SHA-256, and catalogue scope in deterministic policy metadata.
- Make the next version the default for new Swedish candidate selections without breaking
  resolution of existing 1.1.0 configurations.
- Add startup and unit tests proving the new definition hash changes when any bound catalogue
  identity changes and that both versions resolve independently.
- Update the artifact manifest, approval template, research inventory, and deterministic
  verification script expectations to the new frozen definition/hash.
- Do not add qualified reviewer evidence or mark statutory validation true.

### Constraints and preservation rules

Never mutate 1.1.0 in place. The catalogue remains a read-only selection source and must not
automatically assign every BAS account or application role.

### Acceptance criteria

- Given the new policy pack, its metadata contains the exact checked-in catalogue and source
  hashes and those values participate in its definition hash.
- Given an existing 1.1.0 configuration, the resolver still returns the original definition.
- Given new Swedish candidate setup, the latest version is selected.
- Human review state remains pending and statutory validation remains false.

### Verification

Run policy-pack, VAT, startup-validation, manifest-hash, and configuration tests.

### Definition of done

The catalogue-to-pack immutability gap is closed without weakening historical compatibility
or inventing human approval.

## Prompt 4 — Re-run technical readiness and refresh the accountant evidence

### Title and outcome

Produce a reproducible, accountant-readable release candidate whose automated evidence
reflects the implemented controls and exact reviewed artifacts.

### Current context

The Swedish evidence package contains deterministic source, rule, fixture, unsupported-case,
and approval-template artifacts. Qualified reviewer metadata remains intentionally absent.

### Dependencies

Prompts 1–3.

### Implementation requirements

- Update the accountant checklist to describe the BAS UI confirmation workflow and bound
  catalogue metadata.
- Recompute every changed artifact hash and keep the manifest internally consistent.
- Run the Swedish accountant verifier, independent workbook reconciliation, focused tests,
  Web tests, and a broader solution build.
- Record technical findings separately from the pending human Approved/Rejected/N/A decision.
- Leave reviewer identity, signature, approval date, expiry, and final decision blank/pending.

### Constraints and preservation rules

Do not claim statutory approval. Do not register fabricated validation evidence. Do not hide
unsupported VAT scenarios or the limitations of the free BAS workbook.

### Acceptance criteria

- All deterministic checks and relevant tests pass, or every remaining failure is reported
  with an actionable blocker.
- The evidence package identifies the exact new policy-pack and BAS catalogue hashes.
- The human decision remains pending for a qualified Swedish accountant.

### Verification

Run the complete commands named above and inspect the final Git diff for accidental or
unrelated changes.

### Definition of done

All four prompts have been implemented and verified as far as local technical evidence
permits, with no unfinished in-scope code.
