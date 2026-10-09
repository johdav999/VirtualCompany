# P23 issue ledger

| ID | Finding | Fix | Verification |
|---|---|---|---|
| F23-01 | Native variance can add unspecified planning versions and omit actual-only rows | Explicit single-version enriched source report; union posted/history/baseline rows; nullable gaps; shared signed variance arithmetic | Focused API/rendered/wire and browser report reconciliation |
| F23-02 | Native Forecast has no saved assumptions/reopen audit boundary | Immutable header, native composite revision FK, original source payload/checksum, explicit future assumptions, idempotent requests and single successor | Persistence/SQL save/reopen/concurrency/immutability checks |
| F23-03 | Forecast component name collided with transport DTO | Fully qualify the comparison model | Web build and rendered comparison flow |
| F23-04 | Ledger fixture constructor was ambiguous | Explicit costCenterId selects native constructor | Focused composed fixture checks |
| F23-05 | In-app browser inventory cannot initialize | Record exact ACL failure; use fresh headless Edge against owned disposable composed hosts | Browser acceptance packet; user IAB remains unverified |
| F23-06 | Finance agent context needs exact retained forecast provenance | Extend existing exact-reference parser/resolver with forecast version and immutable checksum freshness | Existing and new resolver/projector checks |
| F23-07 | Concurrent SQL duplicate saves could deadlock before idempotent replay | Company-scoped transaction application lock plus Serializable reads inside the production execution strategy; one successor/request identity | Accepted SQL Server duplicate/successor concurrency tests, one audit and native write set |
| F23-08 | Retained report source checks did not recheck nested Finance authority | Current Finance responsibility required before nested planning opens/exports, including Company-lens reviews | Accepted revoked-access snapshot and export tests |
| F23-09 | Saved assumptions clipped in disabled form controls; report tables competed with a sidebar | Render saved assumptions as wrapping text; give tables full width at normal desktop widths and stack sidebar; preserve keyboard horizontal scroll | Final desktop/narrow screenshots visually reviewed; 34 rendered checks and four browser journeys pass |
| F23-10 | Comparison capture could occur during loading; changed preview could reuse the preceding heading | Wait for native Finance loading to finish and assert the changed numeric result before capture/save | Final polished replay records -1400.25 reopen, -1500.25 successor and -100.00 change with no page errors |

Human reviewer: Pending. No release, deployed-tenant, provider or statutory approval is implied.
