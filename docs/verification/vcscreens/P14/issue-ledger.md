# P14 issue ledger

| ID | Finding | Resolution | Verification |
| --- | --- | --- | --- |
| P14-01 | Configured unsupported Finance entries were correctly excluded from execution but absent from the explanation. | Added informational excluded-configuration metadata, separate from executable tools and grants; display Unsupported. | Resolver, projection, wire and browser checks. |
| P14-02 | Numeric company, agent and Finance values could falsely suggest equivalent authority. | Separate labels/receipts from each owner; company external review remains explicit at Act within limits. | Matrix, presenter and read-only UI checks. |
| P14-03 | Unbound task context could look like an evaluated task policy or complete action permission. | Explicit not-evaluated task policy; individual guardrail checks identify missing proposed payload; owning Work dependency remains visible. | Bound/unbound source and browser comparison. |
| P14-04 | Expired/stale grant and asynchronous company changes could leave misleading evidence. | No-store fresh evaluator reads, content fingerprint, refresh, cancellation and company-version diagnostic. | Expiry, refresh and late-response tests plus browser replay. |
| P14-05 | First browser Work load sent literal `Kind` instead of the current work kind in the new Razor child parameter. | Corrected both parent bindings to expressions; added rendered Work/settings integration regression test and a read-only settings action label. | Rebuilt parent/component tests and original browser journey replay. |
| P14-06 | Work detail showed a large full catalogue before the task step, and generic humanization broke decimal amount punctuation. | Compact native disclosure on Work; separate unconfigured catalogue disclosure; preserve numeric limit text and identify missing currency field. | Decimal component regression, desktop/mobile visual comparison and keyboard disclosure replay. |
| P14-07 | Standalone settings agent selection was held only in component state. | Persist the selected agent in the existing query parameter, while preserving company and other view context. | Rendered selection/reload regression and native browser reload. |
| ENV-14 | Existing Roslyn parallel compilation crash and unavailable NuGet vulnerability feed. | Serialized compilation (`DOTNET_PROCESSOR_COUNT=1`, `-m:1`, shared compilation disabled); use restored dependencies. | Accepted build/test logs. Vulnerability feed retrieval remains unavailable in sandbox. |

No implementation issue is considered verified solely from a screenshot. Accepted automated/browser evidence and independent remaining gates are listed in `verification.json` and `uat.md`.
