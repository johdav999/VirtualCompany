# Recipient communication language

Recipient-facing AI and deterministic templates resolve language independently of the operator UI. Precedence is explicit recipient preference, conversation language, campaign language, company communication default, then `en-GB`.

Tags are normalized BCP 47 values. Conflicting conversation/campaign evidence and the system fallback require human review. The resolution, source, confidence, and evidence are included in structured orchestration context. UI culture must never be copied into recipient communication metadata. Approved delivery retains the resolved language and template identity across retries.
