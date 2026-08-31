# Exchange-rate authority and retention

## Scope

The exchange-rate catalogue is the accounting authority for currency conversion. It stores company-scoped currency definitions, source policy, immutable observations, protected source evidence, correction lineage, deterministic lookup explanations, and retained conversion results. Foreign-currency invoice and bill accounting references the retained conversion and observation identifiers rather than re-querying a current rate.

The catalogue does not change the native posting boundary. `IAccountingPostingService` remains the only service allowed to create native journals.

## Supported sources

- `manual`: company evidence imported by an accounting administrator. Every set requires independent approval by a different accounting administrator.
- `riksbank_swea`: indicative exchange rates from the Sveriges Riksbank SWEA v1 REST API. The adapter imports group 130 observations quoted as Swedish kronor per unit of foreign currency. Its default source priority is lower than manual evidence.

Sveriges Riksbank states that the rates are indicative, not transactional prices, and requires source attribution when its statistics are disseminated. The source is freely available for commercial use, subject to the published API limits and conditions. See the [official API guidance](https://www.riksbank.se/en-gb/statistics/interest-rates-and-exchange-rates/retrieving-interest-rates-and-exchange-rates-via-api/) and [FAQ](https://www.riksbank.se/en-gb/statistics/interest-rates-and-exchange-rates/retrieving-interest-rates-and-exchange-rates-via-api/faq--the-api-for-interest-rates-and-exchange-rates/).

An installation may use the public API allowance or provide `RIKSBANK_API_SUBSCRIPTION_KEY`. The key is configuration-only, is added as `Ocp-Apim-Subscription-Key`, and must not be persisted in rate entities, API responses, audit metadata, or logs.

## Selection policy

1. Both currencies must be explicitly enabled for the company. The accounting functional currency is always recognized, but foreign currencies require a company currency definition.
2. Only enabled sources and approved rate sets participate.
3. Lower numeric source priority wins. Within that priority, the newest effective date on or before the requested date wins. A linked correction with the latest source set version wins over the corrected observation.
4. Equally ranked sources with different factors require review. The system never averages conflicting observations.
5. Source staleness is checked after precedence is chosen. A stale preferred source blocks conversion; the policy does not silently fall through to a lower-precedence source.
6. Direct rates are preferred. If none exists, the policy attempts a one-pivot cross rate, preferring the accounting functional currency, then SEK, then an alphabetically stable pivot.
7. The target currency precision and the accounting configuration rounding mode determine the rounded result. The unrounded amount and residual are retained.
8. Missing, stale, ambiguous, pending, disabled, or unsupported facts block conversion. A rate of `1.0` is produced only for an explicit same-currency identity conversion.

Transaction-date, settlement-date, and period-end lookups use the same reproducible selection rules while retaining the requested purpose. Later prompts may add purpose-specific source policies without changing historical conversion records.

## Document and journal currency facts

- Customer-invoice and supplier-bill accounting snapshots retain document totals, functional totals, transaction-date rate identity, retained conversion identifier, rounding residual, and provenance. Approval payloads and source-version hashes bind those facts.
- Posted ledger lines balance in the company's functional currency and separately retain the debit or credit amount in the one document currency represented by the journal. A foreign journal is accepted only when its totals reproduce one retained conversion exactly.
- Credit notes and reversals copy the original document-side and rate evidence while reversing debit and credit direction. Posted evidence is immutable; a correction creates linked new facts.
- Aging, open-item, control reconciliation, and immutable customer statements expose both amount views when authoritative document accounting exists. Imported or historical gaps return `legacy_or_imported_unavailable`; the application does not manufacture a historical conversion.
- The migration treats an existing ledger line as an explicit same-currency identity conversion, so its amount is unchanged. Existing invoice or bill accounting with identical document and base currency receives `base_currency_identity`. A pre-existing foreign profile without retained authority receives `legacy_unverified_rate` and remains ineligible for authoritative foreign posting until re-prepared.

Currency enablement is not tax enablement. The current Swedish accounting tax decision policy blocks non-SEK document currency combinations before statutory numbering or journal allocation. Expand that governed tax policy and its statutory tests before treating a foreign Swedish invoice or bill as supported.

## Imports, corrections, and approval

Manual imports require a stable import identity, canonical evidence description, positive decimal rate, explicit precision, quotation convention, and effective date. Replaying the same identity with the same content returns the original set. Reusing it with different content conflicts.

An approved observation is never edited. A changed value for the same company, source, pair, and date must reference the latest approved observation and rate set as a correction. Provider corrections are linked automatically when the provider publishes a changed value for an already observed pair and date.

Provider refresh requests create durable, leased jobs. Retries are bounded and only transient transport, rate-limit, or server failures retry. Invalid payloads, unsupported configuration, and reused identities with different evidence become terminal operator-visible failures.

## Retention and recovery

- Observation, set, source, checksum, approval, correction, conversion, and conversion-leg records are accounting evidence and are retained with the accounting archive.
- Protected raw import evidence defaults to 2,555 days (seven years). The configured period must remain between one and ten years. When it expires, the worker replaces only the protected payload with an expiry marker; checksum, normalized facts, source identity, and accounting references remain.
- Do not delete an observation referenced by a conversion or future journal. Correction and reversal are additive.
- A database backup must include the data-protection key ring required to decrypt unexpired evidence. Restore verification should confirm both a protected payload round trip and retained checksums.
- If the Riksbank API is unavailable, keep the source in a failed/retry state or import independently evidenced manual rates. Never copy today's rate into a historical date or assume parity.

## Operator checks

- Review `/api/companies/{companyId}/finance/exchange-rates/readiness` for missing observations, pending approvals, failed jobs, and source health.
- Resolve failed refresh jobs before enabling dependent foreign-currency posting.
- Rotate a Riksbank subscription key through the platform secret/configuration boundary and restart the host; no catalogue rewrite is required.
- For a disputed rate, retain the original observation, import a linked correction, obtain independent approval when required, and ensure new conversions reference the correction. Historical conversions remain unchanged.
- For a blocked invoice or bill, inspect its accounting preview for the exact tax-policy or exchange-rate reason. Do not edit a posted journal or replace retained rate evidence; prepare a governed correction or new accounting version.
- Reconcile receivables and payables by document-currency breakdown as well as the functional control total. A `legacy_unverified_rate` or `legacy_or_imported_unavailable` result is a migration/import review item, not permission to assume rate 1.
