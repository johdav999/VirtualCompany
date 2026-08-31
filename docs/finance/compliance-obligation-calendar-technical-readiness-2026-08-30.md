# Compliance obligation calendar technical-readiness report — 2026-08-30

Release classification: `technically_verified_for_human_review`  
Human decision: `pending`  
Statutory approval: **No**

This is an engineering evidence addendum for the Prompt 5 obligation workflow. It does not replace review or approval by a qualified Swedish accountant or tax specialist and does not alter the frozen Swedish VAT policy pack.

## Frozen source identity

| Field | Frozen value |
|---|---|
| Evaluation date | `2026-08-30` |
| Specification | `sweden-domestic-vat-launch-2026.1` |
| Policy pack | `sweden-statutory-candidate` / `1.4.0` |
| Runtime definition SHA-256 | `f7dd2403535ebd51e5e97137cff2aa629da09768cc45cc6a37fbf667d53b3eb6` |
| Evidence manifest | `docs/finance/swedish-domestic-vat-launch-2026.1/artifact-manifest.json` |
| Fixture | `tests/VirtualCompany.Finance.Tests/Fixtures/Compliance/swedish-vat-obligation-explicit-deadline.json` |
| Submission capability | `export_and_manual_evidence_only` |

The example fixture’s due date is explicitly operator supplied and is not a statutory rule or reusable legal deadline.

## Technical audit table

| Checkpoint | Evidence reviewed | Technical status | Human decision | Finding / action |
|---|---|---|---|---|
| Rule source | Filing-period `DueDate`, `DueDateRule`, source hash | `verified` | `pending` | Generation rejects missing deadlines and never calculates a Swedish statutory due date. |
| Generation and idempotency | Service, command receipts, tenant-origin indexes | `verified` | `pending` | One origin is created per company/definition/filing period; command receipts reject mismatched replay payloads. |
| VAT authority reuse | VAT filing period/return and existing close-task link | `verified` | `pending` | The workflow references the existing VAT aggregate and close task rather than duplicating VAT calculation or close authority. |
| Source integrity | Profile version, pack key/version/hash, period facts, VAT hashes | `verified` | `pending` | A deterministic source hash is retained and may refresh only while generated. |
| Review and approval | Domain guards, finance-approval API policy, focused tests | `verified` | `pending` | The preparer cannot self-approve; evidence acceptance also requires a different actor. |
| State separation | Domain tests and accountant-facing UI | `verified` | `pending` | Export, manual-submission evidence, authority receipt, authority approval, rejection, and correction are separate states. |
| Submission evidence | Content hash, reference, actor, time, review state | `verified` | `pending` | Upload/reference evidence does not itself establish filing or receipt. |
| Authority evidence | Acknowledgement kind, reference, hash, actor, time | `verified` | `pending` | Receipt is required before approval; acceptance evidence is retained separately. |
| Correction | Bidirectional instance links and immutable history | `verified` | `pending` | Corrections preserve the original state trail rather than rewriting it. |
| Authorization and tenancy | Accounting view/admin and finance-approval policies; query filters | `verified` | `pending` | API and service enforce company scope and least-privilege role boundaries. |
| Reminder escalation | Durable unique reminder records and counters | `verified` | `pending` | Upcoming/due-soon/overdue reminders are recorded without sending external communications. |
| Migration and retention | Migration, indexes, operational runbook | `verified` | `pending` | Existing filing periods remain valid with null deadlines; accounting evidence has no routine purge. |
| Accountant-facing surface | Generated design reference, responsive Razor surface, Web tests | `verified` using automated substitute | `pending` | List/detail/evidence/timeline/permissions and explicit non-provider messaging are implemented; a signed-in live browser remains a release-environment check. |

## Verification results

| Verification | Result |
|---|---|
| Domain, migration, service-boundary, and accountant-fixture focused tests | 10 passed, 0 failed |
| Accountant-facing Web surface tests | 2 passed, 0 failed |
| Finance infrastructure build | Succeeded with 0 errors; existing warnings only |
| Web build | Succeeded with 0 errors; existing warnings only |
| API compile | Succeeded with 0 errors using `BuildProjectReferences=false` after dependency builds |
| Migration scaffold | `20260830210000_AddComplianceObligationCalendar` and `20260830211000_AddComplianceObligationDefinitions`; Prompt 5 schema only |

## Limitations and revalidation triggers

- No Skatteverket or other authority provider was selected or implemented.
- No due-date formula is encoded. An explicit date and source remain an operator responsibility.
- The workflow does not prove that a return is legally correct, filed on time, received, accepted, or compliant.
- Revalidate after changes to policy-pack identity/hash, profile facts, deadline sourcing, VAT package rules, evidence requirements, authority provider, state transitions, authorization, retention, or the accountant-facing workflow.

## Release conclusion

The Prompt 5 obligation workflow is `technically_verified_for_human_review` for the narrow export/manual-evidence boundary. It is not professionally approved and does not authorize a claim of filing or statutory compliance. The exact Swedish pack remains `review_pending`; final status is `human_accountant_review_pending`.
