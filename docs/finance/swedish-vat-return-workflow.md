# Swedish VAT return workflow (Release 1, Prompt 4)

## Supported boundary

The workflow is a human-filing workspace for the unvalidated Swedish candidate pack. It calculates only the launch mappings retained by posted tax facts:

- box 05: domestic 25% sales taxable basis;
- box 10: domestic 25% output VAT;
- box 48: fully deductible domestic input VAT; and
- box 49: box 10 minus box 48.

Exact journal values remain at their retained precision. Filing values are rounded to whole kronor with midpoint values away from zero. The package does not call, authenticate to, or report success from Skatteverket. `submissionCapability` remains `not_configured`, and the policy pack remains `IsStatutoryComplianceValidated = false` pending Prompt 7 evidence.

## Lifecycle and controls

1. An accounting admin creates a company-scoped, non-overlapping SEK filing period. A filing period may be linked to one fiscal period but remains a separate record.
2. Calculation reads only posted journal lines inside the filing dates. It hashes every relevant immutable tax fact and VAT-control line at a stable cutoff.
3. The calculator de-duplicates the posting copies of each retained source fact, persists box results and source drill-down, and blocks unclassified, malformed, unsupported, currency-mismatched, duplicate, unavailable-pack, reporting-locked-period, or unreconciled sources.
4. Any later relevant posted line changes the current input hash. Reads show the return as stale; approval and finalization actions are removed until recalculation.
5. A clean current return is submitted to the existing finance approval workflow. Finalization rechecks the approval status, input hash, and all blocking issues immediately before locking.
6. Finalization writes a deterministic company-scoped JSON human-filing package to object storage, verifies its retained SHA-256 on download, and records actor, approval, source manifest, pack versions, exact and whole-krona values.
7. A correction creates a linked new version with a reason and evidence reference. The original locked return, package, checksum, and source manifest are not changed; reads expose the original as corrected while retaining its locked evidence.

Swedish fiscal-period close is blocked when a linked filing period is missing a return or its latest return is stale, blocking, unapproved, or not finalized. Country-neutral companies keep the existing tax-summary review behavior.

## API operations

All routes are under `/internal/companies/{companyId}/finance/accounting/vat` and enforce the current company membership in addition to the route identifier.

- `POST/GET filing-periods`
- `POST returns/calculate`
- `GET returns` and `GET returns/{id}`
- `POST returns/{id}/approval`
- `POST returns/{id}/finalize`
- `POST returns/{id}/corrections`
- `GET returns/{id}/package`

Writes require accounting-admin authorization. Reads and package downloads require accounting-view authorization. Replays use company-scoped business idempotency keys.

## Migration and deployment

Apply `20260824143033_AddSwedishVatReturnWorkflow` after the Prompt 1–3 migrations. It adds filing-period, return, box, contribution, issue, and review tables with company-scoped alternate keys, uniqueness indexes, correction links, and a SQL Server row-version concurrency token.

Local SQL Server and Docker SQL Server use the same EF migration assembly and order. Do not create or repair these tables at startup. With automatic migration disabled, deploy the migration before the API build. Existing country-neutral companies receive no filing periods or returns and retain their current accounting data and behavior.

## Recovery verification

A coordinated restore must include the SQL database and object-storage root. For every locked VAT return:

1. confirm the database input hash, calculation checksum, approval request, finalized actor/time, storage key, file name, media type, content length, and package checksum;
2. open the object by its exact company-scoped storage key;
3. calculate SHA-256 over the bytes and compare it to `package_checksum`;
4. confirm the package source manifest and box values agree with the relational contribution and box records; and
5. retain the original package when correction versions exist.

Missing objects, checksum mismatches, stale returns, unresolved issues, or approval evidence mismatches are release and close blockers. Regenerate only an unfinalized return; never overwrite a locked package.
