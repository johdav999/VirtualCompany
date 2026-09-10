# Browser sales room deployment

`main.bicep` creates a dedicated Windows App Service pool, user-assigned managed identity, Log Analytics workspace, Application Insights resource, App Service diagnostics and a five-minute `/health/ready` availability test. It references SQL, Redis, LiveKit and OpenAI secrets from an existing Key Vault. It does not create or change Teams resources or settings.

The template intentionally requires capacity, duration, provider rates and spend budgets. Values in `main.parameters.example.json` marked `REPLACE_...` make that file non-deployable until approved evidence is supplied. Initial controls default to browser admission off, room AI off, emergency disable on and drain on.

Run a review only:

```powershell
./scripts/Deploy-BrowserSalesRoom.ps1 -ResourceGroup <group> -ParametersFile <approved-parameters.json>
```

After reviewing the Bicep compile and Azure what-if output, an authorized operator may add `-Apply`. Deployment authorization does not authorize browser admission: enablement is a separate configuration rollout described in `docs/browser-sales-room-operations.md`.

Required Key Vault secrets are `browser-room-sql`, `browser-room-redis`, `browser-room-livekit-url`, `browser-room-livekit-api-key`, `browser-room-livekit-api-secret` and `browser-room-openai-api-key` unless the corresponding secret-name parameters are changed. The managed identity receives only Key Vault Secrets User on that vault.
