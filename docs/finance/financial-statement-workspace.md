# Financial statement workspace

The accounting report route supports `?view=profit-loss` and `?view=balance-sheet`, with `companyId` and optional `periodId`. Other accounting reports and period close remain available from the report tabs.

The workspace reads authoritative statement services through `GET /internal/companies/{companyId}/finance/accounting/statement-workspace/{reportKind}`. Required scope is `fiscalPeriodId`; optional parameters are `comparisonFiscalPeriodId`, `snapshotId`, and `comparisonSnapshotId`. Company membership and accounting-view permission are checked before reading facts. Every source, period and retained version is company scoped.

## Scope and presentation

Period selectors offer existing fiscal periods only. Profit and loss shows the period's exact start and end; a balance sheet shows balances at the selected period end. The underlying exclusive UTC end boundary is retained in export metadata. There is no arbitrary January–August aggregation or arbitrary balance-date feature. An eight-month label appears only when a fiscal period actually covers those dates.

Swedish presentation uses verified BAS catalogue subdivisions when the company has a Swedish policy pack. Other companies use retained statement classifications and see a subdivision warning. Ledger facts and mappings are never changed for presentation. The management layout has its own version, `management-statements/1.0`; it does not claim statutory approval.

Expenses are displayed as signed costs, preserving reversals. Profit-and-loss percentages require a positive comparison base; zero and negative bases show a dash. Cost increases use an adverse colour. Balance-sheet changes are absolute currency differences. Currency is read-only; mixed-currency statements or comparisons across currencies are rejected rather than relabelled or added without conversion. No scale conversion is offered.

## Retained evidence

Closed periods use the existing retained statement snapshot where available. History lets users select a specific version. CSV and account drill-down use the displayed snapshot identity; comparison snapshot identity is also retained. Legacy closed data without a retained version shows a limitation and cannot offer exact version drill-down.

Selecting a total exposes its contributing accounts. Selecting an account reads the existing live or snapshot drill-down endpoint while leaving the report open. Expense evidence uses the statement's display sign. The evidence difference compares the displayed account amount with journal reconciliation, so a changing live source is visible. Journal links use the existing `journalId` deep link. The general-ledger link preserves company, account and fiscal period; it opens current ledger facts, while retained-version evidence remains in the report rail.

Filters cancel prior requests and late responses cannot overwrite a newer company context. Account or row selection survives an appropriate report reload when its contributor still exists. Errors expose retry; missing periods and loading states disable report output.

## Output

Export downloads a bounded statement CSV containing exactly the displayed rows, account contributors, totals, dates, currency, scale, layout version, source identity, comparison identity and warnings. This is a small read-payload export, separate from the existing durable accounting export jobs. CSV output escapes text and protects formula-leading fields. Print styles include report scope and provenance and remove application navigation and action controls.

Reference amounts appear only in isolated test fixtures. Laura's commentary is deterministic and derived from the loaded report and its warnings. No production example-company amounts, fabricated vouchers, journal counts or professional-validation assertions are introduced.

Verification evidence and limitations: [financial statements UAT](../verification/financial-statements/verification.md).
