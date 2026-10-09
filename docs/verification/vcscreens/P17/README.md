# P17 supervision reporting evidence

P17 is implemented in the same checkout, preserving P01–P16. Available local verification is complete. Full screen acceptance is **Blocked** by the computer-use runtime before browser inventory or tab creation.

- [Implementation, measurement definitions and boundaries](implementation.md)
- [Accepted verification and exact source timing](verification.json)
- [Issue ledger](issue-ledger.md)
- [UAT profile and pending browser flows](uat-profile.md)
- [Real HTTP source reconciliation](http-source-reconciliation.json)
- [Entry preservation baseline](preservation-baseline.json) and [final preservation check](preservation-verification.json)
- [Owned host cleanup](cleanup.json) and [superseded test-run cleanup](superseded-run-cleanup.json)
- [P18 handoff](handoff.md)
- [Overview reference](../../../design/references/agent-supervision-p17-reference.png) and [filtered bottleneck reference](../../../design/references/agent-supervision-p17-bottleneck-reference.png)

Accepted: **81 distinct API checks**, including one isolated migrated SQL Server check; **51 final rendered Web checks**; **36 full typed Web/API checks**; **16 composed HTTP source checks**. API/Web/UAT builds, model-change check and diff check pass. Individual API runs overlap; totals must not be added blindly. Failed/aborted earlier logs are historical diagnostics, not accepted evidence.

No P17 schema migration or fabricated backfill is needed. The report uses retained typed history and explicitly unavailable instrumentation. Browser/keyboard/narrow visual acceptance and a physically saved CSV remain unverified. Live provider/deployed/native/statutory and human gates are independent. P15 broader acceptance remains incomplete, and P16 browser acceptance remains blocked; this packet does not approve Release 2.
