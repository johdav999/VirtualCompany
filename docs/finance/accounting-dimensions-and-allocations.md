# Governed accounting dimensions and allocations

Virtual Company treats **cost center** (`cost_center`) and **project** (`project`) as the initial governed accounting dimension types. Companies can add further types through the accounting-dimensions administration API. A dimension type and member have a stable code, lifecycle, effective dates, and company scope. Codes are identities and are not renamed after creation; display names and hierarchy placement can change for current administration without rewriting posted history.

## Posting control

`IAccountingPostingService` remains the only native posting boundary. Before preview or posting, the dimension policy:

- resolves explicit member IDs and configured dimension facts;
- resolves provider values only through one effective external mapping;
- blocks missing required dimensions and prohibited account/dimension combinations with stable reason codes;
- blocks inactive members, duplicate members of one type, and invalid whitelisted or prohibited combinations;
- retains relational `ledger_entry_line_dimensions` assignments with immutable type, member, and hierarchy display snapshots.

Manual-journal draft assignments are relational before posting. Budget cost-center writes validate the same catalogue, effective dates, and account requirement. Invoices, bills, schedules, assets, bank adjustments, imports, and provider-originated candidates are checked when they reach the central posting preview or post operation. Unknown metadata in legacy `dimension_facts_json` remains a snapshot only and is not treated as authority.

Ambiguous legacy or provider values are not guessed. They appear in `accounting_dimension_mapping_conflicts` and must be resolved by creating one effective external mapping. The migration creates the initial cost-center and project types for existing companies and records unresolved cost-center identifiers from posted lines, manual-journal drafts, budgets, and forecasts as explicit conflicts.

## Allocation templates

Allocation templates have immutable effective-dated versions. One version uses either:

- percentage lines totaling exactly 100%; or
- fixed lines totaling the source amount.

Preview uses deterministic largest-remainder rounding at the version's configured precision. The generated lines always reconcile to the source total or the preview is rejected. Applying a preview records the exact template version, source identity/version, payload hash, generated raw and rounded amounts, rounding residuals, approval reference, idempotency key, actor, and source evidence.

Templates can define a materiality threshold. Applications at or above the threshold require a current approved request targeting the allocation template. Reusing an idempotency key with the same payload returns the original application; different content is rejected.

## Reporting and operations

The dimension report is built directly from relational posted-line assignments and returns the immutable ledger-entry and ledger-line IDs behind its totals. Current catalogue names can change without altering the snapshots shown for historical lines.

Operators should monitor:

- open mapping-conflict count;
- invalid dimension posting previews by stable reason code;
- allocation preview validity and material applications;
- audit events for dimension, member, account-policy, combination, mapping, template-version, and allocation changes.

Before release, apply the EF migration through the normal SQL Server migration path and verify `dotnet ef migrations has-pending-model-changes` reports no differences.
