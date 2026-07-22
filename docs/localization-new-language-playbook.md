# Adding a language

Use this checklist to add a complete language such as `de-DE`. Do not register a culture until every required translation and communication template is ready.

1. Add the BCP 47 tag and a native selector display name to `SupportedWebCultures`.
2. Add a matching satellite `.resx` for every resource marker: UserPreferences, Common, Navigation, Validation, Agents, Finance, Sales, and Support.
3. Preserve every semantic key and composite-format placeholder exactly. Translate values, not keys, API codes, routes, enums, provider keys, account codes, or identifiers.
4. Add deterministic outbound communication templates for the language. UI resources do not double as recipient templates.
5. Run `dotnet test tests\VirtualCompany.Web.Tests\VirtualCompany.Web.Tests.csproj --no-restore --filter FullyQualifiedName~Localization` and the resource parity tests.
6. Add formatter cases for dates, numbers, percentages, money, DST, and ambiguous currency symbols. Keep machine serialization invariant.
7. Browser-check the shell plus representative Agents, Finance, Sales, and Support routes at desktop and mobile widths. Verify text expansion, focus order, form errors, empty/error states, and `<html lang>`.
8. Review translations with a domain-aware native speaker, especially finance, approval, safety, and customer-support terminology.
9. Verify recipient-language fallback/template coverage and approved outbound retry stability.
10. Run release builds, `git diff --check`, and the affected authorization/tenant-isolation suites before release.

Adding a language must not require page logic, API contract, workflow, database status, or domain-enum changes.
