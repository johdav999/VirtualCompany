# Marketing release verification

Verification date: 2026-08-12

This record separates executable Marketing evidence from environment-specific live-provider prerequisites and unrelated repository-wide test failures. It must not be interpreted as proof of live publication to LinkedIn, Meta, or X.

## Implemented release evidence

- API and Web build successfully in `Debug` and `LocalRun`.
- The focused Marketing, company-orchestration, and startup-migration suite passes: 85 passed, 0 failed.
- The focused Marketing Web surface suite passes: 3 passed, 0 failed.
- `git diff --check` reports no patch-integrity errors; line-ending notices are informational.
- `dotnet ef migrations has-pending-model-changes` reports no model drift.
- The idempotent migration script was generated and inspected at `.codex-build/marketing-final-migrations.sql`.
- The representative upgrade database `virtualcompany` was migrated through `20260812130943_AddMarketingCreativeAssetSafetyScans`.
- The clean Docker SQL Server database `virtualcompany_marketing_verify` was created and migrated from the baseline through the same migration.
- API readiness against the clean migrated database returned HTTP 200 with database readiness code `ready`. Redis and object storage remained explicitly unconfigured rather than being reported as successful dependencies.
- Marketing background workers now treat host cancellation during work and polling delays as a normal shutdown path.
- Creative recovery is operator-visible and durable: `POST /api/marketing/creative-assets/{assetId}/rescan` re-reads at most 25 MB, verifies the immutable checksum, calls the configured authoritative scanner, persists the new result, and writes an audit event. Only the latest authoritative `passed` result enables use.

## UI evidence

The exact prompt-required references exist under `docs/design/references`, including the final complete-workspace reference. The built workspace was also verified in the in-app browser:

- Desktop: 1280 px viewport, 1280 px document scroll width, 250 px fixed sidebar, and 1015 px Marketing content width.
- Narrow: 390 px viewport, 375 px document scroll width, no page-level horizontal overflow, and all 15 Marketing workspace sections remain available through the keyboard-operable horizontal section control.
- The Content surface renders its empty state and creation form in plain English at narrow width.
- Scoped CSS now uses the framework asset fingerprint instead of a deterministic assembly identifier, preventing stale layout CSS after deployment.

Evidence:

- `docs/design/references/marketing-workspace-desktop-verified.png`
- `docs/design/references/marketing-workspace-mobile-verified.png`
- `docs/design/references/marketing-workspace-complete-reference.png`

## Repository-wide test status

The broader repository suites were executed, but they are not green and are not recorded as Marketing success:

- `VirtualCompany.Web.Tests`: 184 passed and 113 failed. Failures are concentrated in existing Finance/component harness behavior such as route registration, query-parameter injection, and Finance presenters.
- `VirtualCompany.Web.ContractTests`: 2 passed and 14 failed. Failures are existing Finance SQLite foreign-key/seed-state failures.
- The full API suite also produced broad existing failures across exception-handler, Finance, dashboard, agents, tasks, and mailbox areas; the captured run did not provide a reliable final count and is therefore not summarized with an invented number.

The focused affected Marketing scope is green, but a repository-wide release gate must remain red until the owners of those suites establish or repair their baseline. Do not weaken or exclude the failing tests to obtain a green result.

## Live external checks

Live channel checks remain externally blocked until an operator supplies and approves all of the following for each provider:

| Provider | Required external prerequisites | Safe behavior while absent |
|---|---|---|
| LinkedIn | Reviewed application, current Posts permissions, approved organization/member destination, protected OAuth credential, and any required access tier | Connection/action is unavailable or reauthorization-required; no fixture result is reported as publication |
| Meta | Reviewed Meta application, Page/Instagram permissions, company-owned destination, public approved media URL where required, protected token, and cost authorization | Unsupported capabilities are not advertised and dispatch remains disabled |
| X | Approved developer project, post/media permissions for the selected tier, company-owned account, protected OAuth credential, and cost authorization | Text-only capability remains the advertised subset unless discovery proves more; dispatch remains disabled |
| Asset scanner | Deployment-owned HTTPS endpoint, protected API-key reference, approved harmless and malware fixtures, and operator authorization | Every missing, disabled, invalid, unreachable, pending, failed, or error result remains quarantined |

Any live verification must record the provider application/environment, company-owned destination type, granted scopes/tier, action type, provider reference, normalized result, reconciliation outcome, and timestamp without credentials or personal/raw provider payloads.

## Release decision

Marketing implementation, migrations, focused tests, clean/upgrade database readiness, and responsive UI evidence are verified. Live external side effects are safely unavailable pending approved credentials and provider access. A whole-repository release must additionally resolve or disposition the unrelated red Web, Web-contract, and API suite baselines above.
