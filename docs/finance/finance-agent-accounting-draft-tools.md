# Finance agent accounting draft tools

## Boundary

Laura can prepare four reviewable proposal types through the owning Finance services:

- a manual journal draft
- a correction or reversal draft linked to the original journal
- a reconciliation decision draft that remains proposed
- an accounting-treatment draft based on eligible deterministic account candidates

These are preparation tools. They cannot post a journal, accept or apply a reconciliation, reopen a period, overwrite a reviewed draft, or make a statutory decision. Draft creation requires Accounting Administrator authority. Posting remains a separate existing human-authorized action.

## Required evidence and deterministic checks

Journal proposals carry a fiscal period, document and posting dates, currency, lines, rationale, evidence document identifiers, a stable business idempotency key, and at least one source record identifier with its current version. Corrections additionally retain the original ledger-entry identifier and correction reason. Reconciliation proposals retain their versioned nodes, edges, rule version, and any corrected group.

The model may propose descriptions and accounting selections; those fields are returned explicitly as `modelProposedFields`. The owning Finance services remain authoritative for amounts, debit/credit balance, account and dimension eligibility, tax facts, exchange-rate evidence, period availability, approval rules, and posting rules.

Validation runs immediately after a journal draft is stored. The response returns the persisted draft, deterministic posting preview, policy blockers and warnings, missing evidence, approval requirements, and the fields a reviewer may safely edit. An incomplete or unbalanced proposal remains a draft so the evidence and review history are not lost, but it cannot be submitted.

## Review and submission

Submission is a separate execute tool. It requires an explicit reviewed flag, the current draft identifier and version, the exact current payload hash, fresh source versions, and its own idempotency key. The tool reruns the owning preview and only calls the manual-journal submission service. It never calls posting.

A changed draft, stale source, failed preview, missing evidence, or invalid balance rejects submission and requires a refresh. A repeated draft or submission business key returns the original result; reusing the key for different content is rejected.

## Traceability and persistence

Manual journal drafts persist source references and include them in their payload hash. Existing draft operations preserve create, update, submit, approval, and post history. Tool responses and audit writes retain the conversational correlation identifier, execution identifier, task/workflow context, actor, draft version, payload hash, and approval request identifier.

Advanced reconciliation proposals persist a tenant-scoped idempotency key and canonical proposal hash. The additive migration `20260901123920_ImplementFinanceAccountingDraftAgentTools` backfills existing manual-journal source references as an empty JSON array and adds a filtered unique company/idempotency index for reconciliation proposals.
