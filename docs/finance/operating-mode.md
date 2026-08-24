# Finance operating mode

Normal Finance reads use the `operational` source. They exclude simulation records and never silently combine simulations with internal or provider facts. `fortnox` and `simulation` are explicit source selections; `all` is intentionally rejected for operational reads.

`GET /api/companies/{companyId}/finance/operating-mode` is the operator-visible decision for a date. It is derived from accounting configuration, the active accounting authority period, provider connection status, and the company simulation state. It reports the permitted read and posting sources plus the next safe action.

Simulation Lab remains a separate, feature-gated surface. Simulation data remains retained and can be reviewed only through an intentional simulation selection; it is not operational accounting evidence.

Production configuration must keep `FinanceTools:Provider` as `internal` and must not set `FinanceTools:AllowMockProvider`. A mock provider is registered only when that opt-in is true (for an explicitly configured test, development, or simulation environment). Selecting `mock` without the opt-in fails options validation at startup rather than falling back to deterministic data.

`FinanceSeedBackfill:Enabled` is also disabled in production. Enabling automatic finance seed generation is allowed only in Development, Testing, or Simulation environments; production startup fails with a remediation message if it is enabled. This does not remove retained simulation records or Simulation Lab—it prevents new simulated finance data from becoming an implicit production default.
