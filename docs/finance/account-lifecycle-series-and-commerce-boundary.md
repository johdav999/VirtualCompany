# Account lifecycle, accounting series, and commerce boundary

Prompt 8 completes the company-scoped administration controls for accounts and accounting identities. It deliberately does not add an inventory quantity or COGS subledger.

## Account lifecycle

Every governed account has reportability, posting restriction, effective dates, replacement account, lifecycle reason, and an optimistic lifecycle version. A lifecycle change appends an immutable history row containing the effective classification and restrictions; it does not rewrite ledger lines. Existing accounts are backfilled as lifecycle version 1 during migration.

Before retirement, the preview reports posted journals, account-role assignments, provider mappings, schedules, dimension policies, and fixed-asset-class dependencies. An account with posted history requires a compatible, active replacement. Replacement is routing guidance for future work only: accounts and posted journals are never merged, deleted, or renumbered.

Posting enforcement blocks accounts outside their effective dates, accounts with an `all` restriction, and manual postings when the restriction is `manual`. Errors identify a configured replacement without disclosing another company’s data. Historical ledger entries retain the original account identifier and amounts.

## Voucher and statutory-document series policies

A series policy selects one existing voucher or statutory-document series for a scope made from:

- source and transaction type
- fiscal year
- optional location/dimension member and jurisdiction
- active accounting policy-pack key and version

The company, series kind, and canonical scope key form a unique database index. Policy updates use an expected version concurrency token, and an issued series cannot be moved by editing its policy. Voucher posting evaluates all applicable policy dimensions before allocating a number. Existing voucher and statutory-document allocators remain the identity authority and retain their serializable/concurrency protections.

Voucher gaps are not reused. When a previously allocated number has no issued journal, an administrator may attach a bounded reason as gap evidence. The evidence is unique for company, series, fiscal year, and number; unexplained gap counts remain visible until evidence is recorded. Statutory-document gaps continue to use the existing immutable number-allocation evidence.

Provider mappings are annotations on a policy (`providerKey` and `providerSeriesCode`). They do not transfer numbering authority or permit renumbering an issued voucher or document.

## Commerce integration boundary

`finance-commerce.v1` accepts idempotent, versioned `sale.finalized`, `sale.reversed`, and `purchase.received` facts for traceability. The receipt key is company, event id, and event version. Replays return the retained accepted result.

The capability endpoint reports inventory accounting as `unsupported`. Any event requesting inventory accounting is blocked with `inventory_unsupported`. Finance stores no stock-on-hand, quantity movement, valuation layer, adjustment, or COGS state. Quantity and valuation remain in the commerce or inventory system; supported source documents may enter Finance through their existing accounting workflows.

## Operations and evidence

Administration routes require `AccountingView` for reads and `AccountingAdmin` for mutations. All new entities use the active-company query filter. Lifecycle changes, policy changes, gap evidence, and accepted commerce facts are audited. Low-cardinality meters count lifecycle changes, series policy changes, explained gaps, and accepted/replayed/rejected commerce events.

Migration `CompleteAccountingAdministrationGovernance` adds the lifecycle history, series policy, voucher-gap evidence, and commerce-event receipt tables plus account governance columns and replacement foreign key. Apply it through the normal migration pipeline; do not hand-edit issued identities. After upgrade, verify the backfill count against classified accounts, confirm the unique scope and receipt indexes, and review any account whose classification was incomplete before enabling posting.

The visual target and its exact built-in image-generation prompt are retained in `docs/design/references/finance-account-administration-reference.png` and `docs/design/references/finance-account-administration-reference-prompt.md`.
