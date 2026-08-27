# Prompt 10 native receivables operations UAT evidence

Date: 2026-08-27  
Environment: local workspace, compiled ASP.NET Core Web/API surface  
Role: accounting viewer/administrator contracts  
Revision: working tree containing Release 2 Prompts 1–10

## Product profile

- Product: Virtual Company Web
- Flow: open Finance → Receivables → Operations, understand release state, review a check, and follow recovery guidance
- Reference: `docs/design/references/native-receivables-operations-reference.png`
- Evidence: reference prompt/image, Razor/CSS implementation, typed-client and authorization integration tests, English/Swedish resource parity
- Safety boundary: the view is read-only; provider acceptance, recipient delivery, bank refund completion, statutory validity, performance, and restore success require external retained evidence

## Evidence packet

### AR10-FLOW-001 — Review receivables operational readiness

Expected: an authorized company member sees ten bounded, friendly checks; blocking/attention/healthy summaries; AR control amount; plain remediation; recovery links; evaluation time; and an explicit warning not to resend ambiguous delivery.

Observed: the compiled Razor surface binds to the company-scoped readiness endpoint, maps internal keys to localized titles, caps backend evidence at 25 identifiers per check, displays the three summary states, and presents recovery guidance without raw reason codes or provider payloads. Focused Web and API tests verify the route, authorization boundary, tenant isolation, reference inventory, and English/Swedish resource parity.

Result: **verified using the strongest safe automated substitute**.

### AR10-FLOW-002 — Live desktop and narrow browser comparison

Expected: authenticated desktop and 390px views are visually compared with the screenshot-first reference, with no clipping, inaccessible controls, or runtime console errors.

Observed: the in-app browser runtime could not initialize on this host because its required kernel asset path was unavailable. No authenticated browser or runtime screenshot was produced, and no visual pass is claimed.

Result: **blocked — rerun in an environment with a working in-app browser and an accounting-configured test company**.

## Issue ledger

| ID | Severity | Flow | Type | Summary | Evidence | Acceptance / regression | Status |
|---|---|---|---|---|---|---|---|
| AR10-UAT-001 | P1 | AR10-FLOW-002 | environment | Live browser comparison unavailable on this host | browser initialization error retained in Prompt 10 execution | Run authenticated desktop and 390px Operations views, compare to the checked-in reference, verify keyboard flow and zero console errors | blocked |

No UI defect was marked verified from source inspection alone. The automated substitute verifies contract and structure, not final pixels or runtime interaction.
