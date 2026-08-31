# Unified close workspace technical readiness — 2026-08-30

## Scope and authority

The close workspace at `/finance/accounting/close-workspace` is a company-scoped projection over the durable Prompt 1–8 close, governance, reconciliation, report, compliance, package, accountant, notification, and year-end records. It does not recalculate journal, report, VAT, reconciliation, package, or rollover facts in the browser.

The backend returns the current readiness snapshot, its evidence hash and timestamp, task evidence and blockers, and a caller-role-filtered allowed-action list. The browser sends snapshot versions and evidence hashes back to existing command endpoints. Those endpoints remain authoritative and can reject an action after the page has loaded.

## Stale evidence and recovery

- A readiness snapshot is visibly stale when a selected-period ledger entry or close task changed after the snapshot was prepared.
- Lock always sends the displayed snapshot ID, version, and evidence hash.
- If the backend returns `accounting_close_evidence_stale`, the rejection remains visible and the client requests a fresh readiness snapshot before replacing the workspace model.
- A green/current state is rendered only beside an evidence timestamp.

## Roles and tenant isolation

- `owner`, `admin`, and `manager` can receive state-appropriate close, package, and year-end actions.
- An external `accountant` receives review/sign-off navigation only when an explicit open engagement exists; it never receives lock, reopen, package approval, or rollover authority from the workspace policy.
- Every database projection uses the requested company ID even when query filters are intentionally bypassed for an authoritative cross-layer read.
- Accountant portfolio links retain both `companyId` and the engagement fiscal period. The selected company name and grant remain visible on the engagement surface.

## Observability and supported volume

The service emits `finance.close_workspace.load.count` and `finance.close_workspace.load.duration` and creates `close-workspace.load` activities tagged with company and close availability. Lists are bounded to 60 periods, 100 close instances, 100 reconciliation groups, 100 compliance obligations/packages, 20 year-end runs, and eight notifications. A focused test serializes a representative 500-task close payload under a two-second guard.

## Verification evidence

- `AccountingCloseWorkspacePolicyTests`: role/action policy and supported-volume payload.
- `AccountingCloseWorkspaceSurfaceTests`: read-model/tenant markers, typed client, stale refresh, notification links, localization, responsive/accessibility structure, and screenshot-first artifacts.
- `AccountantPortfolioSurfaceTests`: explicit-grant isolation and separation of duties.
- Screenshot-first reference: `docs/design/references/unified-close-workspace-reference.png` and its retained prompt.

The Swedish statutory disclaimer from the underlying report, VAT, compliance, and package services remains in force. This workspace is a technical workflow and is not a statutory compliance opinion.
