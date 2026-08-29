# Advanced reconciliation operations

## Purpose

Advanced reconciliation groups explain how bank transactions, payments, invoices or bills, and explicit differences form one balanced settlement. A suggestion is evidence for review; it never grants posting authority. The API enforces the active company context and requires Finance Approval for rule changes, acceptance, rejection, and reversal.

## Evidence model

Each proposed group stores a deterministic graph with immutable identifiers:

- bank transaction nodes and their source versions
- payment, invoice, and bill nodes and their source versions
- explicit fee, rounding, adjustment, residual, or suspense nodes
- amount-bearing edges between compatible node types
- per-feature confidence contributions for reference, counterparty, amount, timing, and provider patterns
- the exact company rule version used to evaluate the group
- debit and credit control totals, variance, currency, and cardinality

The graph must balance at the bank-row level. An unexplained residual cannot be hidden in a confidence score; it must be represented by an explicit adjustment or residual node.

## Rule lifecycle

Rules are append-only company-scoped versions. Creating a rule version supersedes the previous current version without rewriting existing evidence. Normalization patterns have bounded length and bounded regular-expression execution time. The active rule controls normalization, tolerances, timing windows, provider patterns, recommendation confidence, low-confidence review, and materiality.

When the current rule changes, every still-proposed group evaluated with an older version becomes stale. It remains visible for diagnosis, but the service rejects acceptance atomically. Rebuild the proposal with the active rule rather than changing the stored evidence.

## Review and decision flow

1. Open **Finance → Accounting → Reconciliation** and select a settlement group.
2. Verify every source node, link amount, currency, fee or rounding line, residual, and the debit/credit control totals.
3. Review each confidence contribution and its evidence. Confidence is advisory and cannot override an imbalance or stale source.
4. Enter a concrete decision reason. Material or low-confidence groups are always marked as requiring authorized review.
5. Accept only when the graph is balanced and the group and rule versions are current. Acceptance writes payment allocations, bank links, adjustments, and governed posting results in one transaction.
6. Reject an unsuitable proposal with a reason. Rejection leaves the proposed graph and history available for audit.

Optimistic concurrency protects the group version. Acceptance also rechecks every source record version. A concurrent rule, bank transaction, payment, invoice, or bill change aborts the whole decision before any allocation, bank link, or posting result is committed.

## Corrections and reversals

Accepted evidence is never edited or deleted. To correct an accepted result:

1. Choose **Create linked reversal**, select an open fiscal period, and enter the reason.
2. The existing accounting posting service creates governed reversing journals for the accepted ledger entries.
3. The service appends a reversal result linked to the accepted result and records a new history and global audit event.
4. Create a new correction group for the replacement settlement if further allocation or matching is required.

A reversal does not rewrite the original accepted snapshot. The accepted result and its rule version, graph, contributions, and ledger references remain the authoritative record of what was decided at that time.

## Queue health and investigation

Monitor the reconciliation summary for:

- **Needs review**: proposed groups awaiting a human decision
- **Low confidence**: proposals below the company threshold
- **Conflicts**: proposals that could not proceed due to deterministic validation or concurrency
- **Stale suggestions**: proposals built with a superseded rule version
- **Average confidence**: directional quality signal, not an approval target
- **Accepted value**: gross accepted bank value for the selected queue window

For a conflict, inspect the blocking reason and group history. For a stale group, rebuild it with the current rule. For a source-version conflict, reload the underlying record and construct a new proposal from current evidence. Do not relax a tolerance merely to clear the queue; rule changes affect all new candidates and require Finance Approval.

## Verification checklist

- Batch deposits allocate exactly to the related receivables and bank control total.
- Partial settlements and provider fees use distinct traceable edges or adjustment nodes.
- Rounding and suspense amounts are explicit, currency-consistent, and balanced.
- A replayed decision, changed rule, or changed source version fails without partial writes.
- Cross-company reads and mutations are rejected by company context enforcement.
- Accepted results remain immutable after a linked reversal.
