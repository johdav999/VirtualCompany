# P05 issue ledger

| ID | Severity / flow | Finding | Acceptance / evidence | State |
| --- | --- | --- | --- | --- |
| P05-01 | P1 / F05-01–04 | Selected Marketing tabs/records lacked durable review and report routes | Exact IDs/company/period/filters/previous view survive reload; wire/route tests and exact browser report return | Verified |
| P05-02 | P1 / F05-02 | Content revisions could preserve previous brief approval; queue check omitted brief version | Atomic draft/version/audit reset; stale commands conflict; genuine shared approval queues only exact approved content; retry-aware API suite | Verified |
| P05-03 | P1 / F05-03 | Unknown spend, currencies, overlapping leads could produce misleading totals | Immutable rows/gaps/currencies and nonoverlapping observations reconcile in API, CSV and browser | Verified |
| P05-04 | P1 / F05-05 | Existing asset/content endpoints permitted an unassigned member | Resolver guards all existing Marketing actions; explicit asset bytes/scans denial and foreign mutations; report revoke clears export/choices | Verified |
| P05-05 | P2 / F05-01 | Daily Marketing lacked launch/budget/attribution/recovery entries; compact shared card hid details | Marketing-only expanded card; visible daily counts, rejected asset and exact delivery; Today browser captures | Verified |
| P05-06 | P1 / launch persistence | Shared approval service did not recognize Marketing channel action; production retries reject raw user transactions | Registered scoped target/context; atomic repeat-safe launch request; execution-strategy transactions and seven retry-aware integration journeys | Verified by strongest safe substitute; SQL Server live gate remains |
| P05-07 | P2 / F05-02–03 | Literal version label, cramped cards and unreliable currency edit | Correct v1/v2 labels, panel spacing and input binding; final filtered URL has currency=SEK, exact return and responsive captures | Verified |
| P05-G1 | External acceptance | Live controlled channel credentials are unavailable | Fixture workers disabled; no provider publish/delivery success claim | Unverified gate |
| P05-G2 | Release acceptance | Production SQL Server, deployed tenant and human release approval are separate | No schema/migration change; CAS exercised through provider-compatible SQLite | Unverified gate |
| P05-G3 | Browser download | Browser download event timed out with no saved file path | Refreshed authorized CSV and revoked-access prevention verified in component; physical saved-file completion remains unobserved | Unverified gate |
