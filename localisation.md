# Localisation Strategy

## Current State

The solution currently has no ASP.NET localisation registration, request-localisation middleware, `IStringLocalizer` usage, or `.resx` resources. Most UI and backend messages are hardcoded in English.

The existing `Company` entity contains `Language`, while `CompanySettings` contains `Locale`. These should not directly control the UI language because different users in the same company may prefer different languages.

## Recommended Model

Keep these concepts separate:

- **User interface culture:** The signed-in user's preferred UI language, such as `en-GB` or `sv-SE`.
- **Formatting culture:** Controls dates, numbers, and currency display. It normally follows the UI culture but can be overridden.
- **Company language:** The company's default business and agent communication language.
- **Recipient language:** The language used for a particular customer email, sales message, or support response.
- **Invariant storage format:** API values, status codes, timestamps, database values, and idempotency keys remain language-neutral.

This separation prevents a company setting from unexpectedly changing every user's interface or causing an agent to answer in the wrong language.

## Implementation Architecture

Use the built-in .NET resource system and `IStringLocalizer`. It requires little custom infrastructure and works with Razor Components, validation, and MAUI.

Suggested resource organization:

```text
src/VirtualCompany.Web/Localization/
  Common/
    Common.resx
    Common.sv-SE.resx
  Navigation/
    Navigation.resx
    Navigation.sv-SE.resx
  Agents/
    Agents.resx
    Agents.sv-SE.resx
  Finance/
    Finance.resx
    Finance.sv-SE.resx
  Sales/
    Sales.resx
    Sales.sv-SE.resx
  Support/
    Support.resx
    Support.sv-SE.resx
  Validation/
    Validation.resx
    Validation.sv-SE.resx
```

Use stable semantic keys:

```text
Agents.Brief.AttachDocument
Agents.Brief.DocumentReady
Finance.Bills.PaymentStatus.Paid
Common.Actions.Save
Validation.Required
```

Avoid using the English sentence itself as the key. Semantic keys remain stable when English wording changes.

Register localisation in `src/VirtualCompany.Web/Program.cs`:

```csharp
builder.Services.AddLocalization(options =>
    options.ResourcesPath = "Localization");

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var cultures = new[] { "en-GB", "sv-SE" }
        .Select(value => new CultureInfo(value))
        .ToArray();

    options.DefaultRequestCulture = new RequestCulture("en-GB");
    options.SupportedCultures = cultures;
    options.SupportedUICultures = cultures;
});
```

Apply `UseRequestLocalization` before Razor endpoints are mapped.

## Culture Selection

Use this precedence:

1. User's saved UI-language preference.
2. Localisation cookie.
3. Browser `Accept-Language` on first visit.
4. Application default, initially `en-GB`.

Add a language selector under the user/profile settings. Because the application uses Interactive Server Blazor, changing language should update the culture cookie and reload the page so the new circuit starts with the correct culture.

A user preference should be persisted independently of `Company.Language`. A `UserPreference` entity is preferable if more preferences such as timezone, date format, and accessibility settings are expected.

## UI Localisation

Razor components should inject a feature-specific localiser:

```razor
@inject IStringLocalizer<AgentsResources> L

<button class="btn btn-primary">
    @L["Brief.AttachDocument"]
</button>
```

Statuses must remain stable on the API:

```json
{
  "status": "pending_review"
}
```

The UI maps that code to a resource key. Never translate database enums or API wire values.

For dynamic values, use resource placeholders:

```text
DocumentReady = "{0} is ready for the agent."
```

```csharp
L["DocumentReady", fileName]
```

## Formatting

Introduce shared presentation services such as:

- `ILocalDateTimeFormatter`
- `IMoneyFormatter`
- `INumberFormatter`

Formatting should use the user culture and company timezone, while calculations and serialized values continue using invariant culture.

Currency should remain an ISO code such as `SEK`. Display can become culture-sensitive:

```text
22 000,00 kr
GBP 22,000.00
```

Do not store formatted money strings.

## API And Validation

The API should preferably return stable error codes and arguments:

```json
{
  "code": "documents.unsupported_file_type",
  "arguments": {
    "extension": ".exe"
  }
}
```

The Web client localises these codes. This prevents API consumers, logs, tests, and automation from depending on English text.

Server-generated operational logs and audit codes should remain invariant. User-visible audit descriptions can be localised when presented.

## Agent Communication

Agent language is a separate concern from UI localisation. Prompt construction should explicitly resolve:

1. Recipient's known language.
2. Case, conversation, or campaign language.
3. Company default language.
4. English fallback.

The resolved BCP 47 language tag should be passed into the shared AI orchestration context. Agents should not infer language from unrelated profile fields or generated company descriptions.

Templates for deterministic emails and notifications should use localised templates. AI-generated communication should receive language and regional tone as structured instructions.

## Adding A Language

With this structure, adding German would require:

1. Add `de-DE` to the supported-culture registry.
2. Add matching `.de-DE.resx` files.
3. Translate values without changing keys.
4. Run automated key-completeness and placeholder tests.
5. Perform UI checks for text expansion, date/currency formatting, and validation.

No page code, API contract, workflow, or database status should need modification.

## Recommended Delivery Order

1. Add localisation registration, culture resolution, and the user language selector.
2. Localise navigation, shared actions, validation, and status labels.
3. Migrate Agents, Finance, Sales, and Support one feature at a time.
4. Introduce centralized date, number, and money formatting.
5. Replace user-facing API strings with stable error codes.
6. Add recipient-language resolution to AI and outbound communication.
7. Add CI tests for missing keys, placeholder mismatches, fallback behavior, and accidental hardcoded UI text.

Start with `en-GB` as the complete source language and `sv-SE` as the first translated language. This exercises formatting and translation differences without introducing a large initial language matrix.
