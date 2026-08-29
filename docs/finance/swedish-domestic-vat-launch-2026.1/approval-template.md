# Swedish domestic VAT launch 2026.1 approval record

This record must be completed by a qualified Swedish accountant or tax specialist. Completing engineering tests or reviewing source formatting is not statutory approval.

## Reviewer

- Name:
- Organization:
- Professional qualification and relevant Swedish VAT experience:
- Contact reference retained by:
- Review date:

## Frozen artifact identity

- Repository revision: `6dbcb6ed1413630a50660e286b78dbf3c95645bc`
- Specification key: `sweden-domestic-vat-launch-2026.1`
- Policy-pack key/version: `sweden-statutory-candidate` / `1.4.0`
- Runtime definition hash: `f7dd2403535ebd51e5e97137cff2aa629da09768cc45cc6a37fbf667d53b3eb6`
- `provenance.json` SHA-256: `550f2ba1177a189a4d2e2f35aef51cb416db5b7f2e429bac4bef49b7517fe492`
- `chart-role-mappings.json` SHA-256: `d2b82d09457a5aa61692029d50829a11b7d2c4ab6587fc79de87c631c12e88b2`
- `vat-rules.json` SHA-256: `48abceb7f8029a5a94f75975aa66416ac058496d20542ad77c0e5e4fa17d7602`
- `unsupported-scenarios.json` SHA-256: `a04ff836bbd3c77d485a8816e773e32ae3fb341240f7cad2ab50c8580f938ff9`
- `golden-fixtures.json` SHA-256: `9306be95d459db5e11478cf7a416173a0fc72fd36f28272f63ed9f1b6f8e30a7`
- BAS source workbook: `BAS_kontoplan_2026_v2.xlsx`; internal workbook version `1.1`
- BAS source workbook SHA-256: `a86b39937fab280d4e5db895c04c2af6e145695863d4845ff14eea5d0302328a`
- Generated catalogue: `src/VirtualCompany.Infrastructure.Finance/Finance/Resources/bas-kontoplan-2026-v1.1.json`
- Generated catalogue identity: `bas-2026` / `1.1`
- Generated catalogue SHA-256: `2ed4f76eca5655bb62d77be4b30dbc6f511afa67d6470656b24a75b441672efc`
- Generated catalogue coverage: 1,282 unique account codes, 1,283 code/name variants, 26 accounts marked unavailable for K2, and 810 subaccounts with checked parent links
- Licensed BAS `listOfAccounts.json`: not acquired; its richer legal-form, SRU, relationship, `isBasic`, and other licensed fields are not represented by the generated catalogue

The generated BAS catalogue is a supplementary account-selection source. Its catalogue key, version, catalogue hash, source-workbook hash, and limited selection-only scope are bound into the frozen `sweden-statutory-candidate` / `1.4.0` policy-pack definition hash above. It does not automatically create accounts or assign Virtual Company roles. Versions `1.1.0`, `1.2.0`, and `1.3.0` remain registered as immutable historical candidates.

## Required decisions

For each checkpoint, record `approved`, `rejected`, or `not applicable`. Add a comment or referenced review note whenever the decision is not `approved`. The accountant is expected to review the business data and generated outputs identified below; source-code inspection is not required.

| Accountant checkpoint | Where to review in the Virtual Company evidence package | Approved / Rejected / N/A |
|---|---|---|
| Source authority, editions, retrieval dates, and effective dates | `research-inventory.md` and `provenance.json`; compare the frozen hashes above with the supplied files. |  |
| BAS 2026 source identity and completeness | Open `BAS_kontoplan_2026_v2.xlsx` in Excel. Confirm title `KONTOPLAN BAS 2026`, version `1.1`, sheet `Kontoplan 2026`, and populated range through row 1152. Compare with the catalogue summary: 1,285 source records, 1,282 unique codes, and 1,283 code/name variants. |  |
| BAS account codes and Swedish names | Review/search the account list returned from catalogue `bas-2026` version `1.1`, or review `bas-kontoplan-2026-v1.1.json` as a data file. Sample-check against the Excel workbook across account classes 1–8. |  |
| Duplicate-code handling | Review BAS code `2087`. Confirm that both source names—`Bunden överkursfond` and `Insatsemission`—are presented and that the user must select one before creating the account. |  |
| K2 restrictions | In Excel, confirm that `#` means “Konton som inte ska användas när K2 tillämpas.” In the catalogue response, filter to K2-allowed accounts and verify that all 26 `#` accounts are excluded. |  |
| Main-account and subaccount hierarchy | Review representative main/subaccount groups in Excel and the catalogue response. Confirm the reported 810 subaccounts have the expected parent account and that no parent link is missing. |  |
| Account class and normal-balance limitations | Open Finance → Accounting → Chart of accounts → Browse BAS catalogue. Confirm that every catalogue account requires explicit confirmation of the selected class and normal balance, including suggested values for classes 1–7, and that class-8 accounts require both fields to be supplied. |  |
| Fields not supplied by the free workbook | Confirm that the review does not assume licensed legal-form applicability, SRU mappings, `isBasic`, related/contra accounts, VAT mappings, English translations, or posting instructions. |  |
| Controlled creation of a BAS account | In Finance → Accounting → Chart of accounts → Browse BAS catalogue, search for `1510`. Review the source and limitations, choose class `Asset` and normal balance `Debit`, confirm accounting semantics and company suitability, create it, and verify that the account and catalogue source evidence appear in the audit history. |  |
| Virtual Company account-role mappings | `chart-role-mappings.json`; compare every required role with the setup preview and generated account-role output. The full catalogue does not automatically assign roles. |  |
| Domestic 25% sale rule, boxes 05 and 10 | `vat-rules.json` rule `se_domestic_sales_25`; inspect the matching positive and negative cases in `golden-fixtures.json`. |  |
| Domestic 25% fully recoverable purchase rule, box 48 | `vat-rules.json` rule `se_domestic_purchase_25_full_recovery`; inspect the matching positive and negative cases in `golden-fixtures.json`. |  |
| Evidence-classification definitions | `provenance.json`, `vat-rules.json`, and the fixture evidence fields in `golden-fixtures.json`. |  |
| Invoice/accrual-method-only boundary | `vat-rules.json`, `unsupported-scenarios.json`, and setup-preview output for a non-accrual statutory profile. |  |
| Credit-note reversal behavior | Credit-note cases in `golden-fixtures.json`; compare original and reversal amounts, VAT boxes, signs, and evidence references. |  |
| Calculation and rounding behavior | Calculation/rounding cases in `golden-fixtures.json`; compare basis, rate, unrounded amount, rounded VAT, and posting totals. |  |
| Golden positive and negative fixtures | `golden-fixtures.json` and the generated golden-scenario test report supplied by engineering. |  |
| Explicit unsupported-scenario inventory | `unsupported-scenarios.json`; confirm unsupported cases are blocked or clearly reported and not silently treated as domestic 25% cases. |  |
| Separation between bookkeeping support and statutory filing | Setup and VAT-return review screens plus `swedish-domestic-vat-launch-specification-2026.1.md`; confirm the product does not claim filing or statutory approval while review remains pending. |  |

## Limitations and conditions

- Approved legal forms:
- Approved business/activity classifications:
- Approved effective period:
- Required operational controls:
- Known exclusions:
- Required re-review triggers:

## Final decision

- Decision: `pending`
- Effective approval date:
- Approval statement:
- Signature or controlled approval-record reference:

The product must retain this record with the exact reviewed hashes. Approval does not extend to a later source, rule, fixture, policy-pack version, statutory return engine, or filing integration.
