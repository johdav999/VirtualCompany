# Browser and verification findings

- AP26-01: native identity and dashboard flags differed from the initial code assumptions. Corrected compiler signatures and existing access flags; final builds and tests pass.
- AP26-02: access errors inherited Today wording. Annual-specific wording now appears; restricted and late-context checks pass with old values/actions cleared.
- AP26-03: a forged approval test's empty threshold context failed generic validation before the annual bypass guard. Added a syntactically valid binding and used JsonContent to avoid the test project's Newtonsoft extension serializing JsonNode. The native guard and stale-budget decision checks pass.
- AP26-04: decimal scales produced locale-dependent `100,0` diff text. Bounded decimal formatting makes the exact target change readable; lifecycle and browser checks pass.
- AP26-05: browser canonical review uses DecisionReview's labelled decision note, rather than the older ApprovalDetail comment. Replay uses the actual current control.
- AP26-06: unknown approval targets defaulted to a payment and threshold explanation. The annual formatter and canonical rationale now describe governance, retain target/owner/allocation comparison and explicitly grant no execution/payment. Native and rendered checks plus final browser replay pass.
- AP26-07: using the same quarter actual as baseline and current value forced zero progress. Annual progress now uses the native recorded goal baseline, with retained quarter evidence explicitly labelled partial. Hand-checkable API assertion and final replay pass.

Diagnostic logs/TRX remain retained and excluded from acceptance. The final seven affected API/SQL checks supersede annual results in the broader 29-case run. Final Web and wire results supersede their earlier UI versions.

AP26-08: Real desktop evidence exposed cramped period links because the annual isolated stylesheet lacked its own navigation rules. Added wrapping spacing, padding and active state. Final rebuilt browser flow and narrow/keyboard checks pass.
