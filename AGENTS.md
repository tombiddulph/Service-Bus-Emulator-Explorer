# Repository guidance

## Toolchain and verification

- Use .NET 10 and Node.js 24. The backend targets `net10.0`; CI uses Node 24.
- Reproduce CI in this order: `npm ci` then `npm run build` in `app/sb-explorer-ui`, followed by `dotnet restore -r linux-musl-x64`, `dotnet build --no-restore --configuration Release`, and `dotnet run --project test/ServiceBusEmulatorExplorer.Tests/ServiceBusEmulatorExplorer.Tests.csproj --configuration Release`.
- Frontend lint is separate from its build: run `npm run lint` in `app/sb-explorer-ui` when changing frontend code.
- `node scripts/verify.mjs` runs the local CI checks, including Vitest, Playwright and generated-contract drift checks. Real-emulator integration uses the separate `test/ServiceBusEmulatorExplorer.Integration` runner and disposable `test/compose.integration.yaml` stack.
- Tests use TUnit through Microsoft.Testing.Platform, not the usual VSTest runner. Run one test with `dotnet run --project test/ServiceBusEmulatorExplorer.Tests/ServiceBusEmulatorExplorer.Tests.csproj -- --treenode-filter "/*/*/*/TestMethodName"`; list names with the same command plus `-- --list-tests`.
- Backend tests replace both Service Bus clients with in-memory fakes and do not require Docker. The test assembly retries every test up to three times and its test class is non-parallel.

## Runtime and build wiring

- The ASP.NET entrypoint is `src/ServiceBusEmulatorExplorer/Program.cs`; endpoint groups live under `Endpoints/`. The React entrypoint is `app/sb-explorer-ui/src/main.tsx`; API behavior and built-in mock data are centralized in `src/api/client.ts`.
- `dotnet build` in Debug installs frontend dependencies with `npm ci` only when `node_modules` is absent. `dotnet publish` always runs `npm ci` and `npm run build`, then embeds `dist` under `wwwroot`; expect backend publish changes to exercise the frontend too.
- Release builds enable AOT, trimming, invariant globalization, and `linux-musl-x64`. Keep new runtime behavior trim/AOT-safe. Add new API request/response model types to `AppJsonContext.cs` because HTTP JSON uses source-generated metadata.
- `ServiceBusEndpointCache` shares cached receivers across HTTP requests. Any operation on a receiver must hold `LockAsync` for the full operation; locks deliberately cover the same entity/subqueue across receive modes.

## Local workflows

- For split development, start only dependencies with `docker compose -f compose-services.yaml up -d`, run `dotnet run --project src/ServiceBusEmulatorExplorer` (API on 5123), then `npm run dev` in `app/sb-explorer-ui` (UI on 5173). The frontend script explicitly disables mocks and targets `http://localhost:5123/api`.
- `npm run dev:mock` enables all built-in mocks, including environment and connection metadata, without a backend. Use explicit `VITE_USE_MOCK*` variables for partial mocks.
- The bundled publish-and-run workflow has shell/environment and file-lock requirements; follow `RUN_LOCAL.md` rather than improvising. Stop the running published DLL before replacing `artifacts/local`.
- Docker Compose requires root `.env` values `ACCEPT_EULA=Y` and a complexity-compliant `SQL_PASSWORD`. `compose.yaml` runs the full stack; `compose-services.yaml` runs only SQL and the emulator.
