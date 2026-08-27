# Swedish VAT policy foundation and release blocker

Prompt 2 adds a deterministic, effective-dated tax-decision boundary and immutable tax-fact retention. The decision input includes the selected immutable pack, accounting date, sales/purchase direction, document type, explicit line classification, company country and VAT-registration state, accounting and document currency, bookkeeping method, counterparty jurisdiction/VAT status, user-supplied evidence with optional source references, line amount, and configured rounding. An allowed decision retains those inputs together with the source specification, policy-pack key/version/hash, rule key/version, rate, basis, amount, treatment, account roles, recoverability, VAT-box mappings, evidence classification, attesting actor, and rounding policy in the versioned tax-fact snapshot. Customer-invoice and supplier-bill preview policies use this boundary; posting and correction services carry the retained decision snapshot to journal-line tax facts without re-deriving the original rule.

The launch pack is fail-closed outside Swedish (`SE`), Swedish-krona (`SEK`), invoice/accrual-method bookkeeping. Missing evidence is never inferred from a rule key. Stable policy reason codes identify unsupported scope and missing facts. A blocked submission writes a tenant-scoped audit event containing the policy-pack identity and reason code while excluding document contents and tax identifiers. Pack startup validation rejects undefined or unavailable roles, invalid sales/purchase posting shapes, duplicate rule identities, overlapping applicability, and continuity gaps.

The `RetainDeterministicTaxFacts` migration adds `tax_facts_json` to customer-invoice and supplier-bill accounting lines. Existing rows receive `{}` and remain readable. Apply the migration through the normal SQL Server command for either the local or Docker connection:

```powershell
dotnet ef database update --project src/VirtualCompany.Persistence.Migrations/VirtualCompany.Persistence.Migrations.csproj --startup-project src/VirtualCompany.Api/VirtualCompany.Api.csproj --context VirtualCompanyDbContext
```

## Launch scope and remaining gate

The checked-in `swedish-domestic-vat-launch-specification-2026.1.md` and its machine-readable package in `swedish-domestic-vat-launch-2026.1/` supply the bounded source provenance, chart-role mappings, VAT rules, negative-case inventory, and executable golden fixtures implemented by this pack. `artifact-manifest.json` freezes the runtime definition and normative package hashes; automated parity tests detect source/runtime drift. Only domestic standard-rate 25% sales and fully recoverable domestic standard-rate 25% purchases are supported. Every other Swedish VAT case remains blocked.

The package is an engineering source specification with `review_pending` status. A qualified Swedish reviewer must complete the included approval record against the exact runtime definition and artifact hashes before the pack may be described as statutorily validated. Acquisition and hashing of the complete licensed BAS JSON remains required before the full BAS chart is distributed or treated as a frozen release artifact. Statutory VAT-return generation and filing remain separate capability gates.
