# Daily cash and treasury workspace

## Purpose and route

The consolidated workspace is served at `/finance/cash-position` so existing Cash Position bookmarks and finance navigation continue to work. Its bounded read endpoint is `GET /api/companies/{companyId}/finance/treasury-workspace` and requires the resolved company context plus `FinanceView` authorization.

Connection configuration remains in `/finance/settings/bank-connections`. Recovery links from the workspace carry the retained connection, checkpoint, and gap identifiers. Reconciliation links target the retained bank transaction. Payment links target the native payment batch workflow.

## Evidence and truth boundaries

- Bank balances prefer the latest retained bank-feed balance snapshot and fall back to a retained finance balance. The source type and observed timestamp are always returned per mapped account.
- Bank evidence is `current` for six hours, `stale` after six hours, and `missing` when no retained balance timestamp exists. A future timestamp is not treated as stale, but should be investigated through telemetry and source controls.
- An open feed gap remains visible until its retained gap row is resolved. Coverage never claims the missing range as complete.
- Approved, queued, submitting, awaiting authorization, provider accepted, processing, provider completed, rejected, reconciliation required, and settled are distinct payment states. Rejected and ambiguous (`reconciliation_required`) executions are never shown as successful and must not be resubmitted blindly.
- The 14-day projection is calculated by the backend from posted cash plus expected open inflows minus expected open outflows. The UI formats returned values and does not recalculate finance policy.
- Laura is recommendation-only. Her card cites retained record links and explicitly lists stale or missing evidence. It does not approve, cancel, reconcile, reconnect, or initiate payments.

## Authorization and recovery

The endpoint evaluates `FinanceEdit` and `FinanceApproval` without upgrading the request. The policy returns an allow/deny decision, stable reason code, plain explanation, approval requirement, and deep link for reconnect, gap recovery, reconciliation, payment review, cancellation review, and liquidity investigation.

A permission-denied action remains visible as explanatory text, not an enabled control. A permitted action navigates to the existing authoritative workflow; the workspace itself is read-only.

## Operational bounds

- Horizon: 1–30 days; default 14.
- Connected accounts: maximum 50.
- Source candidates: maximum 10 retained rows per account.
- Payment work rows: maximum 30.
- Priority exceptions: 1–50; default 12.
- Finance tasks: 1–25; default 8.

`IsTruncated` indicates that at least one source exceeded a response boundary. The service uses company-scoped, no-tracking database reads and never writes during a workspace load.

## Telemetry

Backend meters:

- `finance.treasury_workspace.loads`
- `finance.treasury_workspace.failures`
- `finance.treasury_workspace.duration`
- `finance.treasury_workspace.exception_count`

Web usage meters:

- `finance.treasury_workspace.views` with risk, stale, and locale dimensions
- `finance.treasury_workspace.actions` with action and policy reason-code dimensions

Investigate sustained load failures, p95 duration regressions, growth in stale views, repeated open gaps, and repeated ambiguous payment outcomes. Never infer success from an absent provider acknowledgement.

## Verification checklist

1. Load a company with multiple connected accounts and confirm each row shows a balance source and timestamp or explicitly says it is missing.
2. Open a retained feed gap and verify the workspace shows the gap, does not claim complete coverage, and links to the matching bank connection/checkpoint/gap.
3. Load a rejected and a reconciliation-required payment and verify neither appears as settled or successful; both link to their payment batch evidence.
4. Verify a viewer sees reasons but no enabled edit/approval actions; verify permitted roles receive only policy-allowed links.
5. Switch between English and Swedish and test desktop and narrow layouts with keyboard navigation and visible focus.
