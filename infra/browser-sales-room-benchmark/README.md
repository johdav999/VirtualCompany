# Isolated browser Sales-room benchmark target

This template creates a new Windows App Service plan/app, managed identity, Application Insights and Log Analytics workspace inside a dedicated disposable resource group. It enables browser-room paths and disables Teams paths in that isolated app. The load generator is deliberately external and its distinct resource ID is required.

The package must be the reviewed self-contained `win-x64` API artifact. Provider, database, storage and authentication configuration comes from the named Key Vault through the target's managed identity. Do not point that vault at production data; use a synthetic benchmark tenant and isolated dependencies. The template does not create SQL, LiveKit, OpenAI, users or approvals.

Deployment is a separately authorized live action. Before deployment, fill the benchmark run plan with the exact git revision, resource/SKU, named approver, spend ceiling and maximum concurrency. Validate the template first:

```powershell
az bicep build --file infra/browser-sales-room-benchmark/main.bicep
az deployment group validate --resource-group <dedicated-benchmark-rg> --template-file infra/browser-sales-room-benchmark/main.bicep --parameters benchmarkAuthorized=true namePrefix=<unique-name> packageUri=<reviewed-package-url> keyVaultName=<isolated-vault> loadGeneratorResourceId=<separate-load-generator-resource-id>
```

Capture one-second App Service/Application Insights metrics and provider usage into the driver contract. Run paired concurrency-1 trials before 5/10/25/50 and stop at the authorized ceiling or first quality, latency, spend, queue, frame, restart or cleanup failure. App Service instance count is fixed for a run so capacity is attributed to an exact SKU/count; deploy a new immutable run target to compare another count.

After evidence and provider billing reconciliation, delete only the dedicated resource group named in the reviewed run authorization. Retain sanitized report/evidence outside that group. Never reuse the Teams media resource group or send benchmark traffic to Teams routes.
