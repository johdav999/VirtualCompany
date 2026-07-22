# Localization lifecycle

The Web application supports `en-GB` and `sv-SE`. `en-GB` is the source and fallback culture.

## Culture resolution

1. A persisted user preference is returned with `GET /api/auth/me` when one exists.
2. The interactive navigation compares that value with the active circuit culture. If they differ, it submits an antiforgery-protected form to `/localization/apply`.
3. The Web endpoint validates the BCP 47 tag against `SupportedWebCultures`, writes the standard ASP.NET localization cookie, validates the return URL as local, and reloads once.
4. Without a persisted preference, request localization uses the validated localization cookie, then `Accept-Language`, then `en-GB`.

User UI and formatting preferences are global to the user. They do not change `Company.Language` or `CompanySettings.Locale`, and switching companies does not overwrite them.

## Registering another culture

Add the canonical culture tag and selector label to `SupportedWebCultures` and the matching allow-list entry to `SupportedUserCultures`. A release must also include complete resource families and outbound communication templates before the culture is enabled. Run localization key and placeholder parity tests before release.

The preference schema is an EF Core migration and is provider-neutral for both local SQL Server and Docker SQL Server. Both startup paths use the same migration history.
