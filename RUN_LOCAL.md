# Run Locally With .env

This guide runs the published ASP.NET Core application with the bundled frontend and the environment values in the repository root `.env` file.

## Prerequisites

- .NET 10 SDK, because the backend targets `net10.0`
- Node.js and npm, because publishing installs and builds the frontend
- Docker and Docker Compose, to run the Service Bus Emulator and its dependencies
- A `.env` file in the project root

## Start The Service Bus Emulator

From the project root, start the emulator and its dependencies:

```bash
docker compose up -d
```

## Create The .env File

Create `.env` in the project root, alongside `compose.yaml`, `README.md`, and `RUN_LOCAL.md`.

```env
ASPNETCORE_ENVIRONMENT=Development
ASPNETCORE_URLS=http://localhost:5123
ServiceBus__ConnectionString="Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"
ServiceBus__AdministrationConnectionString="Endpoint=sb://localhost:5300;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"
ServiceBus__RefreshIntervalMs=5000
```

## Frontend Environment Variables

Do not create a frontend `.env` file for this bundled local-run workflow. The frontend build script sets `VITE_API_BASE_URL=/api` and disables all mock-data flags, so the bundled UI calls the API served by this application.

The run command also clears inherited `VITE_API_BASE_URL` and `VITE_USE_MOCK*` variables before publishing. This prevents shell or machine-level frontend settings from overriding the bundled application's API route or enabling mock data.

For standalone frontend development, run `npm run dev` from `app/sb-explorer-ui`. Its script sets the API URL to `http://localhost:5123/api` and disables mock data; the backend must be running first.

## Run The Application

From the project root, run this command in Git Bash or another Bash-compatible terminal:

```bash
unset VITE_API_BASE_URL VITE_USE_MOCK VITE_USE_MOCK_QUEUES VITE_USE_MOCK_TOPICS VITE_USE_MOCK_SUBSCRIPTIONS VITE_USE_MOCK_MESSAGES VITE_USE_MOCK_DLQ && \
set -a && source .env && set +a && \
rm -rf artifacts/local && \
dotnet publish src/ServiceBusEmulatorExplorer -c Debug -o artifacts/local && \
cd artifacts/local && \
dotnet ServiceBusEmulatorExplorer.Api.dll
```

The command clears Vite mock and API override variables, exports the values from `.env`, publishes the backend and frontend to `artifacts/local`, and starts the application.

No separate `dotnet restore`, `npm install`, `npm run build`, or `dotnet build` command is needed. `dotnet publish` restores and builds the backend, then runs `npm install` and `npm run build` for the frontend.

## Use The Application

After the application reports that it has started, open:

- UI: <http://localhost:5123/>
- API: <http://localhost:5123/api>
- API documentation: <http://localhost:5123/scalar/v1>

The port comes from `.env`. If it differs, use the port reported in the application startup output.

## Stop And Run Again

- Press `Ctrl+C` in the terminal running the application to stop it.
- Wait for shutdown to complete before running the command again. The running process holds files in `artifacts/local`, so publishing again before it stops can fail with a file-lock error.
- Rerun the command above after making backend or frontend changes.

## Troubleshooting

- If the application starts on port `5000` instead of `5123`, ensure the command includes `set -a && source .env && set +a`.
- If publishing fails because `artifacts/local` is busy, stop any previous `ServiceBusEmulatorExplorer.Api.dll` process and rerun the command.
