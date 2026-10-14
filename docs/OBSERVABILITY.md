# Health, diagnostics and API contracts

- `/health` and `/health/live` check process liveness, without broker dependencies.
- `/health/ready` returns 200 only when administration responds and the configured
  AMQP TCP transport is reachable; otherwise it returns 503 with component flags.
- `/api/connection` returns the same component flags for the UI, using HTTP 200
  even when dependencies are unavailable. Failures reaching this endpoint mean
  the API itself is unavailable.

Readiness checks are non-destructive, share a three-second deadline and are
cached for five seconds. The TCP probe is **not** an authenticated AMQP handshake:
reachability does not prove send/receive permissions. Browser peeking and the
emulator integration suite exercise the broker protocol. Checks target AMQP TCP
(port 5672 by default), not AMQP-over-WebSockets.

The UI displays connection status and the time of its last successful diagnostic
request. A failed refresh does not discard the previous snapshot.

OpenTelemetry includes meter `ServiceBusEmulatorExplorer`:

| Instrument | Meaning |
| --- | --- |
| `explorer.count.duration` | Count scan duration in milliseconds; tagged with exactness |
| `explorer.receiver.lock_wait` | Receiver operation lock wait in milliseconds |
| `explorer.bulk.operations` | Bulk operations tagged with operation and outcome |

Metrics avoid entity names and message IDs to limit cardinality. No message
bodies or credentials are added to these instruments. Do not enable verbose SDK
logging or attach sensitive message payloads to custom tracing in shared deployments.

## Generated TypeScript contracts

`app/sb-explorer-ui/src/api/generated.ts` is generated from the backend OpenAPI
document. `src/api/types.ts` only provides aliases and UI navigation scopes.
Do not edit the generated file manually. CI exports OpenAPI through the
fake-backed test host, regenerates TypeScript, and rejects a changed generated file.

To update contracts, run the tests with `OPENAPI_OUTPUT` set to the absolute path
of `artifacts/openapi.json`, then run `npm run api:generate` in the frontend.
The `OpenApiDocumentCanBeGenerated` test can be selected to avoid running the
entire suite. Neither export nor generation requires a running emulator.
