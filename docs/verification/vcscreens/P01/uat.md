# P01 browser acceptance evidence

Date: 2026-10-01. Revision: `37834c7f` plus the P01 working-tree changes. Browser: Codex in-app browser, real Blazor Interactive Server pages and typed HTTP clients. API: composed `TestWebApplicationFactory`, isolated SQLite fixtures, normal development-header authentication and existing authorization/query services. Profile and issue ledger are alongside this packet.

This is **live browser verification against an isolated test API**, not deployed-tenant, SQL Server, provider, accounting correctness or human release approval. No external recipients, money movement or production configuration changes were used. Fixtures are exclusively under `tests/VirtualCompany.Workspace.Uat`.

## Results

| Flow / account | Steps and expected result | Observed result / artifacts | Result |
| --- | --- | --- | --- |
| F01 / p01-owner, desktop 1280px | North Today; select Company, Finance, Sales, Marketing, Customer Support. Each authorized button updates canonical lens; Support uses customers. | All five canonical URLs and pressed states recorded in `browser-checks.json`; `owner-support.jpg`. Finance's unavailable source is explicitly labeled, not represented as a healthy invented value. | Pass for shell |
| F02 / p01-dual | Open North with `lens=finance`; only assigned Sales/Marketing appear; server defaults to Sales. Press Enter on Marketing. | Canonical sales fallback, two views, no Finance feature section; Enter changes lens to marketing. `dual-marketing.jpg`, JSON. Membership permissions to modules remain separate from lens availability. | Pass |
| F02 record / p01-dual | Open seeded Sales Today priority → North renewal deal `44444444-4444-4444-4444-444444444444`; reload; sidebar Overview returns to Sales Today. | Actual persisted deal title and 12,000 SEK value render; company and exact return origin remain in URL after reload; Overview returns to sales. `sales-record.jpg`, JSON. No business command or delivery was executed. | Pass |
| F03 / p01-owner | August 2026 Sales Monthly → sidebar Sales → reload module → sidebar Overview; then browser Back to Sales and Back to Monthly. | Restored `/dashboard?companyId=11111111-1111-1111-1111-111111111111&period=month&lens=sales&year=2026&month=8`; same August period after return and Back. `monthly-return.jpg`. | Pass |
| F03 invalid link / p01-owner | Open Monthly with month=15 and no year; activate Try again. | Clear invalid-link feedback; Try again removes malformed period inputs and loads the current reporting month with the same company/lens. Canonical October 2026 in this run. `invalid-month-recovered.jpg`, JSON. | Pass |
| F04 / p01-owner | Switch North August Sales Monthly to South using native company selector. | South Today canonicalizes to company default, old lens/month removed, active option South, links carry South only. `company-switch.jpg`. Delayed-response isolation is separately covered by automated tests. | Pass |
| F05 unassigned / p01-member | Open configured North without a responsibility assignment. | Safe company lens, single visible responsibility and backend reason, no redundant picker or Finance section. `unassigned-member.jpg`, JSON. | Pass |
| F05 revoked / p01-owner | Direct North-independent URL for revoked Restricted company with finance lens. | Restricted message, no Today workspace, Restricted absent from selector; final sidebar identity is neutral Company rather than another company's active name. `restricted-company.jpg`, JSON. | Pass |
| F05 foreign record / p01-dual | Direct link to South deal `55555555-5555-5555-5555-555555555555` while scoped to North. | “Deal is unavailable” / “This deal was not found for the active company.” South title and value are absent. `foreign-record-denied.jpg`, JSON. | Pass |
| F06 / p01-member, 390×844 | Open drawer with Enter; native company control visible; Escape closes drawer. Inspect page width. | Drawer and selector accessible; closed page scrollWidth 375 vs viewport 390; open drawer width 390. `mobile-drawer.jpg`, `mobile-member.jpg`. | Pass |
| F06 / p01-owner, 390×844 | Inspect Today with all five lens controls. | Overall page fits viewport; responsibility strip keeps its existing internal horizontal scrolling behavior; metrics stack in two columns. `mobile-five-lenses.jpg`, JSON. Temporary viewport reset afterward. | Pass for responsive shell |

## Fixes discovered during replay

The first role-card replay showed missing lens in a nested return link: Razor string parameters needed explicit expressions. The decisions footer also bypassed the return helper. Both now carry the same origin as priority/metric links and are covered by the component/route checks.

The first Monthly → Sales replay revealed that query subscriptions can notify the departing Dashboard before disposal. It could load a default Today projection and canonicalize over the destination. `IsDashboardLocation` guards both the load entry and every async commit/redirect; the regression test preserves a business navigation even when Dashboard is still mounted. The original browser flow passed after rebuilding.

A revoked-company replay showed the sidebar fallback using the persisted active-company name instead of the URI scope. It now resolves only the current active membership or uses the neutral Company label. Automatic and browser checks verify the correction.

## Environment findings and limits

Sandbox startup hit Windows Event Log access and inability to decrypt existing Windows DPAPI antiforgery keys. Local fixture logging was configured with `Logging__EventLog__LogLevel__Default=None`; final Web fixture processes ran outside the sandbox after tool approval. No production source workaround or authentication bypass was introduced. A locked fixture DLL caused an intermediate build failure; its recorded process was stopped before rebuilding. Final checks pass.

Existing inner module links can omit returnUrl. Within a circuit, sidebar Overview remembers the origin; a fresh reload of an inner URL without that parameter defaults to Today. Durable nested-return integration and role-specific content/report acceptance remain explicitly owned by P04–P08. P01 verifies entry links and record URLs carrying their origin.

The browser fixture has no configured bank/provider and its Finance source can be unavailable. This is adequate to verify authorized shell selection/error treatment, not Finance amounts or statutory correctness. Full role journeys, report reconciliation, external delivery and policy changes remain their owning prompts' checks. Human approval is pending.

Fixture PIDs and ports were recorded during execution and all P01-created hosts are stopped at completion. Screenshots and JSON are durable evidence; PID files are historical, not instructions to stop a later process with a reused ID.
