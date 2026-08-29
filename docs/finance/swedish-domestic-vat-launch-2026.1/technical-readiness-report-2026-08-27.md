# Swedish accounting technical-readiness report — 2026-08-27

Release classification: `technically_verified_for_human_review`  
Human decision: `pending`  
Statutory approval: **No**

This report records reproducible engineering evidence for the Swedish accounting release.
It does not replace review or approval by a qualified Swedish accountant or tax specialist.

## Frozen scope

| Field | Frozen value |
|---|---|
| Evaluation date | `2026-08-27` |
| Frozen repository revision | `6dbcb6ed1413630a50660e286b78dbf3c95645bc` (`Add BAS account catalogue integration and approval evidence`) |
| Specification | `sweden-domestic-vat-launch-2026.1` |
| Policy pack | `sweden-statutory-candidate` / `1.4.0` |
| Policy definition SHA-256 | `f7dd2403535ebd51e5e97137cff2aa629da09768cc45cc6a37fbf667d53b3eb6` |
| BAS catalogue | `bas-2026` / `1.1` |
| BAS catalogue SHA-256 | `2ed4f76eca5655bb62d77be4b30dbc6f511afa67d6470656b24a75b441672efc` |
| BAS workbook SHA-256 | `a86b39937fab280d4e5db895c04c2af6e145695863d4845ff14eea5d0302328a` |
| Fixture source | `golden-fixtures.json`, SHA-256 `9306be95d459db5e11478cf7a416173a0fc72fd36f28272f63ed9f1b6f8e30a7` |

The reviewed implementation and evidence package are frozen at the repository revision
above. The hashes identify the exact policy, catalogue, workbook, and fixture content.

## Findings

| Checkpoint | Evidence reviewed | Technical status | Human decision | Finding / action |
|---|---|---|---|---|
| Required evidence files | Approval template, manifest, provenance, mappings, VAT rules, fixtures, unsupported scenarios, workbook, and generated catalogue | `verified` | `pending` | All required technical evidence is present. Qualified reviewer fields intentionally remain blank. |
| BAS source identity | `BAS_kontoplan_2026_v2.xlsx`; generated catalogue `source.sha256` | `verified` | `pending` | Workbook and catalogue source hashes match exactly. |
| Catalogue identity and content | `bas-kontoplan-2026-v1.1.json`; runtime catalogue validation | `verified` | `pending` | Catalogue is `bas-2026` / `1.1`; its checked-in file hash matches the frozen runtime value. |
| Catalogue completeness | Generated catalogue and workbook reconciliation | `verified` | `pending` | 1,285 source records consolidate to 1,282 unique codes and 1,283 code/name variants. |
| Duplicate BAS codes | Catalogue entry `2087`; API and UI selection flow | `verified` | `pending` | Both Swedish source names are retained and an explicit source-name choice is required. |
| K2 restriction markers | Workbook `#` note; catalogue `isK2Allowed`; server-side filter | `verified` | `pending` | Exactly 26 restricted codes are preserved and excluded by the K2-only query. |
| Main/subaccount hierarchy | Catalogue groups and parent links | `verified` | `pending` | 810 subaccounts are represented and no parent link is broken. |
| Account class and normal balance | BAS catalogue UI, API request, Finance service, focused tests | `verified` | `pending` | Creation requires an explicit class/balance selection and a separate accounting-semantics confirmation. Suggested values are not treated as BAS facts. |
| Company/legal-form suitability | BAS catalogue warning and confirmation, Finance service, focused tests | `verified` | `pending` | Creation is rejected unless the operator explicitly confirms suitability for the company. The application does not infer suitability from the free workbook. |
| Controlled account creation | Accountant-facing BAS page, typed client, API integration test, audit metadata | `verified` | `pending` | Search, group/K2/existing filters, paging, duplicate-name choice, permission-aware creation, confirmation gates, and catalogue audit identity are implemented. |
| Account-role mappings | `chart-role-mappings.json`; setup policy tests | `verified` | `pending` | Reviewed operational roles remain deliberate mappings; the full BAS catalogue does not assign roles automatically. |
| Immutable catalogue-to-pack binding | Policy pack `1.4.0`, artifact manifest, policy tests | `verified` | `pending` | Catalogue key, version, catalogue hash, workbook hash, scope, and review state participate in the new policy definition hash. Historical versions `1.1.0`–`1.3.0` remain registered. |
| Enabled VAT rules | `vat-rules.json`; policy and golden-scenario tests | `verified` | `pending` | Only the scoped domestic 25% sale and fully recoverable purchase rules are enabled, with boxes 05/10 and 48 respectively. |
| Golden fixture coverage | `golden-fixtures.json`; focused and complete Finance test runs | `verified` | `pending` | Required positive, negative, credit-note, rounding, evidence, and unsupported-boundary cases are present and executable. |
| Artifact manifest hashes | `artifact-manifest.json` and every listed artifact | `verified` | `pending` | Every recorded artifact hash matches its current file. |
| Human review gate | Manifest and VAT-rule review states; empty reviewer/signature fields | `verified` | `pending` | `review_pending` remains enforced; no human identity, signature, approval date, or opinion was fabricated. |
| Accountant-facing UI flow | BAS Razor page, generated design reference, 6 Web tests, 4 API integration tests | `verified` using automated substitute | `pending` | Loading, empty/error, permissions, selection, confirmation, success, existing-account and localized surfaces are covered. An authenticated live-browser pass remains a release-environment check because no seeded signed-in company session was supplied. |

## Verification results

| Verification | Result |
|---|---|
| Swedish accountant deterministic verifier | 13 verified, 0 failed; classification `technically_verified_for_human_review` |
| Focused Finance tests | 35 passed, 0 failed |
| Complete Finance test project | 302 passed, 0 failed, 7 environment-dependent integration tests skipped |
| Accounting-administration API integration tests | 4 passed, 0 failed |
| Accountant-facing Web and typed-client tests | 6 passed, 0 failed |
| Complete solution build | Succeeded with 0 errors; 150 existing MAUI XAML compiled-binding warnings |
| Git whitespace validation | Passed; line-ending conversion notices only |

## Limitations and unsupported cases

- The free BAS workbook does not supply legal-form applicability, SRU mappings, `isBasic`,
  related or contra accounts, VAT mappings, English labels, or posting instructions.
- Reduced rates, EU/foreign trade, reverse charge, exemptions, partial VAT recovery, cash
  accounting, and statutory filing remain outside this frozen VAT scope unless explicitly
  described by another reviewed pack.
- Technical verification does not establish that an account is appropriate for a specific
  company or transaction. That decision remains attributable to the operator and reviewer.
- The application supports bookkeeping preparation and review; this evidence does not claim
  Skatteverket filing, authority acceptance, certification, or a professional opinion.

## Revalidation triggers

Repeat technical verification and obtain fresh human review when any policy definition,
catalogue/workbook, role mapping, VAT rule, fixture, unsupported-case inventory, effective
date, scope limitation, or accountant-facing confirmation workflow changes. A later policy
pack or catalogue version must have a new immutable identity and hashes.

## Human handoff

The qualified reviewer should complete `approval-template.md` for the exact hashes above,
including identity, qualification, scope, date, limitations, final decision, and controlled
signature/reference. Until that happens, the release remains `human_accountant_review_pending`.
