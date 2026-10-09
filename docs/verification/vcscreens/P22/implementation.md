# P22 implementation

Marketing management now compares recorded channel costs and attribution, discloses missing acquisition economics, exposes campaign/segment and experiment evidence, and retains private budget proposal revisions. `/marketing/reports/management` and `/marketing/reports/budget` extend the existing Marketing workspace and P20 monthly review. P05 delivery, spend/budget and lead reports retain their definitions.

## Ownership and definitions

Application/Marketing owns the typed contracts and authoritative budget calculation. Infrastructure.Sales/Marketing queries the native measurement/portfolio tables and calls the current Marketing lens resolver for every report, export, save, history and open. Domain/Persistence owns one new immutable proposal table. Web uses CompanyApiTransport and typed MarketingManagementApiClient; it renders server-owned figures and sends explicit assumptions, without duplicating business calculations.

Costs use immutable attribution touches in the company-local month, with an exclusive month/as-of cutoff. Company filters are reapplied on all IgnoreQueryFilters queries. Equal source keys count the latest version once; conflicting costs at that version become unavailable. Different currencies remain separate. Known cost is not represented as complete spend when costs are missing.

Whole recorded outcome windows inside the month are considered. The retained model ID/version must match its native definition and run label. Allocations must reconcile to included sources, weights and outcome values. Selecting an explicit model version is required for additive outcome totals. Repeated identical cohorts count once; conflicting repeats and overlapping windows are excluded. Other versions remain inspectable. The actual window and configured lookback are disclosed separately; no claim is made that native attribution enforced that lookback.

Channel economics divide recorded channel/currency cost by positive credited outcome units only when included cost and per-unit attribution coverage are complete. Unknown costs or currency, invalid allocations or missing denominators produce unavailable economics. Units are never combined. Attributed cost per unit does not establish causal acquisition cost, ROI or experiment uplift. Native experiment decisions retain their sample, contamination, guardrail, evidence and limitations; causal eligibility remains confined to the recorded experiment. Safe source connection health states explain missing or failed measurement coverage without exposing connection secrets.

Campaign budgets, portfolio ceilings and segment associations are current recorded context. Segment filtering selects campaigns through exact recorded version links; it does not fabricate segment-level outcomes. Contact/other-subject touches remain in unfiltered reports and are not guessed onto campaigns. Every source collection is bounded at 2,000 records; overflow is an explicit validation failure, not silent truncation. No historical campaign ownership, ingestion completeness or achieved impact is inferred.

## Proposals and retention

Saving requires one currency, unique included same-currency campaigns, bounded nonnegative amounts, allocations equal to the proposed total, and compliance with any assumed maximum. Applicable recorded portfolio ceilings include other campaign allocations. Missing ceiling/amount/currency coverage is explicit; assumed ceilings are not actual spend authority. Differences are computed only from known current campaign budgets. Impact descriptions and notes remain assumptions.

`AddMarketingManagementBudgetProposals` adds only `marketing_budget_proposal_revisions` and its company relation/unique request, series/revision, successor and history indexes. No native measurement, portfolio, budget or content records are changed or backfilled. JSON retains the exact report, assumptions and result, with source as-of, calculation version and SHA-256 checksum. Payloads are bounded at one MiB. Save and business audit are atomic. Repeated request identities reproduce the original payload; changed assumptions or competing successors return conflict. SQL Server uniqueness resolves concurrent request/successor races. Revisions reject ordinary modification/deletion and are retained until company deletion.

History and open are private to the accountable reviewer and require current Marketing access. Reopen checks checksum, required context and reproduced results. P20 reviews capture the management report with all source/model evidence; monthly reports do not implicitly choose an attribution model. Existing reviews without the new optional payload show its absence. Proposal evidence opens the exact selected-model retained report; source links open current authorized records. Conflicting retained-link filters are rejected.

## Navigation and export

Campaign/segment drill-down and proposal actions preserve company, month, filters and bounded return context. Monthly drill-down preserves lens and exact saved snapshot. Loading, offline, empty, forbidden, validation, conflict, integrity and timeout states have visible recovery actions. Company/location changes cancel reads and withhold late results. Save retries preserve request identity and edited assumptions.

CSV is generated by the same server report definition with fresh authorization, filter metadata, formulas/coverage, channel values, cost sources, model/version/windows/allocations, campaign/segment/portfolio context, experiment decisions and safe connection health. Quoting and formula-leading text escaping protect the handoff. Retained reports do not offer a current-data export. Web calls the native `downloadReport` function. Browser Blob handoff is separate from a physical saved download.

## Design and limits

Built-in ImageGen produced both reference images before UI implementation; exact prompts and images are under docs/design/references/marketing-*-p22-*. Native Razor/CSS follows the existing shell, padded white panels, compact comparison, evidence detail and bounded decision form. Generated illustrative figures/model labels do not override authoritative native models or coverage. Browser capture review led to improved card/control padding and a keyboard-focusable horizontally scrollable table. Accountable reviewer IDs are retained audit identities.

Local automated, disposable SQL Server and headless Edge acceptance are recorded separately. User in-app browser control fails before inventory with the Windows ACL helper error. Existing user hosts are preserved. Live provider ingestion, deployment, physical output and human release approval remain independent; this proposal workflow never publishes content or raises actual spend limits.
