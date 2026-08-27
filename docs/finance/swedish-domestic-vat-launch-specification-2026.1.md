# Swedish domestic VAT launch specification 2026.1

Specification key: `sweden-domestic-vat-launch-2026.1`

Status: engineering source specification; qualified Swedish reviewer approval pending.

Effective date: 2026-01-01. Jurisdiction: Sweden. Accounting currency: SEK. Accounting method: invoice/accrual method only. Policy-pack key/version: `sweden-statutory-candidate` / `1.1.0`.

This specification is intentionally narrower than the complete Swedish VAT system. It defines the evidence-backed source facts, account-role mappings, deterministic VAT rules, fixtures, and blocked boundaries for the first Swedish candidate. It does not claim statutory validation or authorize filing with Skatteverket.

## Package contents

The normative human-readable rules are in this document. The associated machine-readable artifacts are stored in `docs/finance/swedish-domestic-vat-launch-2026.1/`:

- `provenance.json` identifies every legal, operational, and chart source used by this version.
- `chart-role-mappings.json` maps the bounded BAS 2026 subset to application roles without modifying BAS source data.
- `vat-rules.json` contains the two supported, effective-dated VAT decisions.
- `unsupported-scenarios.json` enumerates cases that must fail closed.
- `golden-fixtures.json` defines calculation, VAT-box, journal, evidence, and blocking fixtures.
- `research-inventory.md` records the wider Swedish information inventory and sources for future versions.
- `approval-template.md` defines the qualified-review evidence needed to change the status.

The JSON artifacts are specification inputs, not a parallel runtime policy. The checked-in `SwedishCandidateAccountingPolicyPack` is the machine-executed implementation and must remain semantically identical to the approved version of this package. Its deterministic definition hash changes whenever a runtime rule or chart fact changes.

## Source authority and provenance

Apply sources in this order:

1. Enacted Swedish legislation published by Sveriges riksdag controls legal effect and effective dates.
2. Current Skatteverket guidance and filing specifications control operational classification, VAT-return boxes, evidence, and electronic representation where consistent with legislation.
3. BAS 2026 controls account codes, names, hierarchy, normal-balance metadata, organization applicability, SRU metadata, and published account relationships.
4. The application overlay controls only Virtual Company role assignment and supported-product scope.
5. A qualified Swedish reviewer approves the resulting version and its golden fixtures before statutory capability is represented as validated.

Every source has a stable `sourceId`, URL, publisher, retrieval date, and applicability statement in `provenance.json`. A source update does not silently alter this version. It creates a reviewed successor specification with new effective dates and fixtures.

## BAS source handling

The preferred production source is the licensed BAS 2026 `listOfAccounts.json` or the BAS REST API. The upstream JSON must be retained unchanged in its documented UTF-8 structure. It must not be regenerated from this subset or enriched with Virtual Company role keys.

The complete licensed BAS JSON is not checked into this package. Until it is acquired under appropriate terms, the bounded launch accounts below are evidenced by the publicly published BAS 2026 chart. Acquisition metadata and a SHA-256 hash must be added to `provenance.json` before the complete chart is distributed or treated as a frozen release input.

## Supported company and transaction profile

The two supported decisions require all of the following:

- The company is established in Sweden, uses SEK accounting, uses the invoice/accrual method, and has a current user-attested VAT-registration state of `registered`.
- The accounting date is on or after 2026-01-01.
- The source document is a customer invoice, customer credit note, supplier invoice, or supplier credit note explicitly supported by the selected rule.
- An operator supplies the required evidence classifications. Those classifications are attestations with the meanings defined below; they are not inferred from free text or by an LLM.
- The transaction is an ordinary domestic 25% case. Reduced rates, exemptions, international trade, reverse charge, partial recovery, mixed use, and special schemes are outside this version.

## Evidence classification semantics

`operator_classified_domestic_standard_25` means the operator attests that:

- the supply is taxable in Sweden;
- the supplier and transaction facts make ordinary supplier-accounted Swedish VAT applicable rather than reverse charge;
- the supplied good or service is subject to the Swedish standard 25% rate on the accounting date; and
- the source document supports the classification and has no conflicting rate, exemption, or foreign-VAT evidence.

`business_use_full_recovery` means the operator attests that:

- the purchase is used wholly in activity that permits deduction of input VAT;
- no private use, exempt activity, deduction prohibition, mixed-use allocation, or partial-recovery limitation applies; and
- a qualifying invoice or other legally sufficient evidence is retained.

The application must retain the supplied evidence classification keys with the immutable tax facts. An attestation does not establish qualified statutory validation of the policy pack.

## Supported chart subset

The Swedish source names below come from BAS 2026. English labels are application display translations. Role assignments are Virtual Company policy decisions.

| Code | BAS 2026 name | Application role/use |
|---|---|---|
| 1510 | Kundfordringar | Customer control account (`accounts_receivable`) |
| 1930 | Företagskonto | Bank control account (`bank`) |
| 2081 | Aktiekapital | Equity control account (`equity`) |
| 2440 | Leverantörsskulder | Supplier control account (`accounts_payable`) |
| 2611 | Utgående moms på försäljning inom Sverige, 25 % | Output-VAT control account (`tax_output_25`) |
| 2641 | Debiterad ingående moms | Deductible input-VAT control account (`tax_input`) |
| 2999 | OBS-konto | Suspense control account (`suspense`) |
| 3001 | Försäljning inom Sverige, 25 % moms | Domestic standard-rate revenue (`revenue`) |
| 4000 | Inköp av handelsvaror (gruppkonto) | Launch purchase/expense default (`operating_expense`) |
| 3740 | Öres- och kronutjämning | Explicit posting rounding (`rounding_difference`) |
| 3960 | Valutakursvinster på fordringar och skulder av rörelsekaraktär | Exchange gain (`exchange_gain`) |
| 6570 | Bankkostnader | Bank adjustments (`bank_fee`) |
| 7960 | Valutakursförluster på fordringar och skulder av rörelsekaraktär | Exchange loss (`exchange_loss`) |

Account 4000 is a BAS group account. Its use as the candidate's generic operating-expense default is a bounded application mapping, not a claim that every domestic purchase belongs on account 4000. Operators must choose a more specific active BAS account when the transaction classification requires one.

## Supported VAT decision: domestic sale at 25%

Rule key/version: `se_domestic_sales_25` / `2026.1`.

- Direction: `sales`.
- Effective period: 2026-01-01 with no end date in this version.
- Documents: `customer_invoice`, `customer_credit_note`.
- Line classification: `standard_goods_or_services`.
- Required evidence: `operator_classified_domestic_standard_25`.
- Input amount: VAT-exclusive line amount.
- Calculation: taxable basis equals the line amount; VAT is 25% of the basis; gross is basis plus VAT, rounded using the selected accounting configuration.
- Posting: debit accounts receivable for gross; credit revenue for basis; credit `tax_output_25` (BAS 2611) for VAT. A credit note reverses the signs through the correction workflow.
- VAT return: taxable basis maps to box 05; output VAT maps to box 10.
- Recoverability: not applicable to a sale.

## Supported VAT decision: domestic purchase at 25% with full recovery

Rule key/version: `se_domestic_purchase_25_full_recovery` / `2026.1`.

- Direction: `purchase`.
- Effective period: 2026-01-01 with no end date in this version.
- Documents: `supplier_invoice`, `supplier_credit_note`.
- Line classifications: `expense`, `asset`.
- Required evidence: `operator_classified_domestic_standard_25`, `business_use_full_recovery`.
- Input amount: VAT-inclusive line amount.
- Calculation: taxable basis is gross divided by 1.25; input VAT is gross minus basis, rounded using the selected accounting configuration.
- Posting: debit the selected expense or asset for basis; debit `tax_input` (BAS 2641) for recoverable VAT; credit accounts payable for gross. A credit note reverses the signs through the correction workflow.
- VAT return: deductible input VAT maps to box 48. There is no ordinary domestic-purchase basis box.
- Recoverability: `full`.

## Calculation, rounding, and currency

Journal and retained tax facts preserve SEK kronor and öre. Calculation precision and midpoint behavior are explicit accounting-configuration inputs and must be retained with the decision context. The launch fixtures use two decimal places and avoid unresolved midpoint-boundary assumptions.

Skatteverket guidance states that amounts with more than two decimals may be rounded to two decimals using ordinary mathematical rounding. Payment-level öresavrundning is separate: it must not alter the taxable basis or VAT. Any payment difference is posted explicitly to BAS 3740.

Foreign-currency VAT conversion is unsupported by this version. The wider research inventory records the permitted source rates, but no exchange-date or conversion rule is enabled here.

## Immutable tax facts

An allowed decision must retain at least:

- specification key and policy-pack key/version;
- rule key and rule version;
- accounting date, direction, document type, and line classification;
- input amount method, taxable basis, rate, VAT amount, gross amount, and currency;
- liability and recoverable account roles;
- recoverability and VAT-box mappings;
- supplied evidence classifications; and
- rounding precision and mode used for the calculation.

Historical facts are never recomputed using a newer pack. Corrections create linked reversing/replacement entries; they do not edit posted facts.

## Explicitly unsupported

All 12% and 6% cases; exemptions; small-business VAT exemption; exports; EU sales or purchases; non-EU transactions; imports; domestic or cross-border reverse charge; partial recovery; mixed or private use; deduction prohibitions; margin schemes; OSS; voluntary rental taxation; cash accounting; self-supply; foreign VAT; foreign-currency VAT conversion; triangular trade; and any classification not explicitly listed above.

Unsupported or insufficiently evidenced cases return a blocking decision. No rate, account, recoverability, VAT box, or fallback treatment may be inferred. `unsupported-scenarios.json` is the normative negative-case inventory for this version.

## Approval and change control

This package remains `review_pending`. To approve it, a qualified Swedish accountant or tax specialist must complete `approval-template.md`, review the exact artifact hashes, confirm the source editions and effective dates, approve the two account/rule mappings and all golden fixtures, and confirm every unsupported boundary.

Approval of this bounded package does not by itself authorize statutory return generation or filing. Those remain separate capability gates. Any material source, rate, applicability, evidence, account, box, calculation, or fixture change requires a new specification version; previously posted decisions retain their original rule version and facts.
