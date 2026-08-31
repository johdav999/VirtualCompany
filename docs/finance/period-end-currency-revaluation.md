# Period-end currency revaluation

## Purpose

The currency revaluation control converts posted foreign-currency monetary balances at an authoritative period-end rate. It retains the exact source population, rate observations, rounded proposal, approval identity, posted journal, reconciliations, review decisions, and next-period reversal. The retained checksums let finance reproduce the result without a spreadsheet.

The standard monetary population is governed by the `cash`, `bank`, `accounts_receivable`, and `accounts_payable` account roles. Finance administrators can explicitly enable another monetary account or disable a role-derived account. The exchange gain and exchange loss roles must both be configured before a proposal can be prepared.

## Controlled workflow

1. Open **Finance → Reports & close → Currency revaluation** and select the fiscal period and governed voucher series.
2. Prepare the run. The service groups posted lines deterministically by account and document currency, binds each group to the period-end exchange-rate evidence, rounds with the company accounting configuration, and retains control totals and checksums.
3. Resolve every `needs_review` population item. Missing authoritative rates cannot be included. An exclusion requires a retained evidence reason and regenerates the proposal.
4. Request approval. The approval binds the run version, population checksum, rate-set checksum, proposal checksum, and total adjustment. Regenerating cancels the pending approval and supersedes the old mutable run.
5. A separate finance approver decides the request in the existing approval inbox.
6. Post only when the exact approved version and checksum still match current source evidence. The posting authority allocates the voucher and writes the immutable journal.
7. Reverse the posted journal in the next open fiscal period. Repeated worker or operator requests reuse the same reversal identity and create exactly one reversal.

Posted runs cannot be edited. A correction uses the reversal action followed by a newly prepared and separately approved replacement.

## Close controls

Period close is blocked when a foreign monetary population exists and the required run is missing, failed, superseded, unposted, unreconciled, or stale. A posted run becomes stale when a later non-revaluation journal changes a foreign monetary account in that period. Each blocker links back to the retained run evidence.

The close evidence panel exposes shortened population, rate-set, and proposal checksums. The API detail contains the complete identities, source checksums, rate-set versions, observation identities, evidence checksums, proposal lines, review records, and reconciliations.

## Scheduling and operations

The per-company schedule controls how many days before period end preparation begins and whether the next-period reversal is automatic. `CurrencyRevaluation:Worker` controls the host worker; its default poll interval is 900 seconds and supported range is 60–86400 seconds.

Monitor logs for `Currency revaluation worker handled` and `The currency revaluation worker cycle failed`. Metrics are emitted from the `VirtualCompany.Finance.CurrencyRevaluation` meter and label actions by workflow action and resulting state. Audit events use the `currency_revaluation_run` target and cover preparation, review, approval request, posting, reversal, and configuration.

If preparation fails, verify monetary and gain/loss account roles, voucher series, open fiscal period, and the authoritative period-end rate source. Correct the governed source and regenerate; do not alter retained evidence. If posting is stale, reverse any posted run, regenerate, obtain a new approval, and post the replacement.

## Deployment and rollback

Apply migration `AddPeriodEndCurrencyRevaluation` before enabling the worker. The migration adds only new tables, constraints, and indexes; existing journals and exchange-rate evidence are unchanged. During rollback, disable `CurrencyRevaluation:Worker:Enabled` first. Do not drop the tables after production runs exist unless the evidence has been archived under the organization’s retention policy. Application rollback can safely leave the tables in place.

## Security and tenancy

Read endpoints require `AccountingView`; preparation, review, configuration, approval submission, posting, and reversal require `AccountingAdmin`. All records carry a company identifier, use compound company foreign keys, and are protected by tenant query filters. The service independently verifies that the acting user remains an active company member.
