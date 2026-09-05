# Implementation handoff

## Starting state

- Azure Portal-style UI is merged in PR #17 (`3dc5aff` on `main`). Preserve this visual direction.
- Current branch: `fix/dlq-operation-reliability`.
- **Uncommitted, unverified backend work is already present for PR 1.** Read `git status` and `git diff` before editing. It has not been compiled or tested. Do not assume it is complete.
- These edits touch `Program.cs`, `Models.cs`, `AppJsonContext.cs`, `Endpoints/DeadLetterEndpoints.cs`, and add `DlqOperationOptions.cs` under `src/ServiceBusEmulatorExplorer`.
- The draft adds delete result models, 30-second operation / 5-second cleanup budgets, partial-acquisition tracking, delete lock renewal, structured delete outcomes, and an unknown-send flag. Frontend consumers, test fakes, regression tests, and documentation still need updating.
- The earlier UI verification passed production build and live browser smoke checks. Repository-wide ESLint still reports eight existing errors in `App.tsx`, `api/client.ts`, `AppError.tsx`, and the three create dialogs. Do not attribute those to new work without comparing the baseline.

## Execution rules for each assignment

- Read root `AGENTS.md`, this plan, and the specific files listed for your task.
- Deliver one focused PR at a time. Include backend, frontend, mocks, contract documentation, and regression tests together where a behavior spans layers.
- Do not replace the portal UI design or combine unrelated dependency upgrades with functional changes.
- Add tests for observable failure cases, not tests that merely duplicate helper implementations.
- At handoff, report changed files, commands run, results, known limitations, and the PR URL. Never describe an unrun check as passed.
- Use the executable config as the source of truth. API contracts are handwritten in both C# and TypeScript; source-generated JSON registration is required for new DTOs.

## PR 1 — DLQ operation reliability

**Priority:** first. **Starting point:** current uncommitted draft.

### Read

- `src/ServiceBusEmulatorExplorer/Endpoints/DeadLetterEndpoints.cs`
- `src/ServiceBusEmulatorExplorer/{ServiceBusEndpointCache,Models,AppJsonContext,DlqOperationOptions}.cs`
- `test/ServiceBusEmulatorExplorer.Tests/{Tests,TestServiceBusClient,WebApplicationFactory}.cs`
- `app/sb-explorer-ui/src/api/{client,hooks,types}.ts`
- `app/sb-explorer-ui/src/utils/replayResult.ts`
- `app/sb-explorer-ui/src/routes/{QueueDetail,TopicDetail}.tsx`
- `app/sb-explorer-ui/src/components/MessageDetailPanel.tsx`

### Backend tasks

1. Review and compile the existing draft before extending it. Ensure options DI resolves through the minimal API handlers.
2. Return structured results for both selected and all-message DLQ deletion. Retain `count` and `notFound` for compatibility; document new status/outcomes/error fields and HTTP 207 for incomplete operations.
3. Count only confirmed removals. Distinguish not-found, failed settlement, timeout, and cleanup failure. Avoid a successful empty response after catching an exception.
4. Drain delete-all with bounded batches and finite waits. Do not collect an unbounded async stream in memory or wait until the timeout merely because the queue is empty.
5. Retain every received batch in caller-owned state before another await. If acquisition fails midway, cleanup must still know which messages were locked.
6. Hold the canonical entity/subqueue operation lock until renewal has stopped and abandonment attempts have completed. Ensure **every** exception path disposes this lock, including renewal-task failures during `finally`.
7. Give cleanup an independent cancellation budget and pass its token to broker calls. Avoid using `WaitAsync` to release the application lock while an underlying broker operation still runs.
8. Renew acquired broker locks during long selected-delete and replay operations, including the acquisition of a large prefix.
9. Use cached sender ownership consistently: endpoint requests must not dispose senders owned by `ServiceBusEndpointCache`.
10. A failed send acknowledgement is not proof that the broker rejected the message. Report ambiguous sends explicitly and never describe them as safe to retry. Check cancellation **before entering the send attempt** so a known-unsent message is not incorrectly classified as ambiguous.
11. Define the HTTP-disconnect policy explicitly. Current draft uses an operation deadline independent of `RequestAborted`; do not silently change mutation cancellation semantics. Reads can use request cancellation separately.

### Frontend tasks

1. Use one named DLQ request timeout that exceeds the backend operation plus cleanup budgets with a small network allowance; 45 seconds fits the default 30 + 5 second budgets. Apply it to replay and deletion.
2. Add `DeleteDlqResult` / per-message outcome types and the replay unknown-send field. Update built-in mocks to match actual response shapes, including delete-all.
3. Replace unconditional green deletion notifications with completed/partial/timed-out/failed summaries. Preserve relevant unsuccessful selections; show not-found separately.
4. On a mutation transport failure without a trustworthy response, say the outcome is unknown and instruct the user to refresh/check the destination before retrying. Do not auto-retry mutations.
5. Update `summarizeReplayResult`: ambiguous sends must not enter the safe-retry selection. Preserve the distinction between sent-but-not-removed and confirmed-unsent messages.
6. Refresh affected message lists and counts after partial outcomes and uncertain transport errors, as well as full success.
7. Prevent duplicate submissions across toolbar replay/delete and message-blade Save & Requeue while either DLQ operation is pending. The blade needs explicit pending state from its parent.
8. Keep the compact portal command bars and existing notification patterns.

### Test-fake changes required

- `TestServiceBusReceiver` currently ignores `ReceiveMode` for its batch receive overload. Model `ReceiveAndDelete` removal and `PeekLock` acquisition separately before testing the new delete-all loop.
- Make blocked abandon hooks respect their cancellation token.
- Add deterministic failure points: failure after the first receive batch, partial completion failure, delayed/ambiguous send acknowledgement, and cleanup cancellation.
- Tests may override `IOptions<DlqOperationOptions>.Value` to use short budgets. Avoid real 30-second waits.

### Acceptance tests

- Delete-all empty queue completes promptly and returns count zero; populated delete-all returns the actual count.
- Selected deletion removes only targets; missing targets are reported explicitly.
- Deletion with some failed settlements returns an incomplete result and the confirmed count.
- Mid-acquisition cancellation releases previously acquired messages for both delete and replay.
- A second operation on the same entity/subqueue waits through the first operation's cleanup, including different receive modes.
- Cleanup timeout returns a useful error and releases the application semaphore for future requests.
- Lock renewal protects early batches while acquiring later ones.
- Replay send succeeds but settlement fails: sent=true, removed=false, no safe replay retry.
- Send acknowledgement is lost: unknown-send outcome and no automatic/safe retry selection.
- Pre-send cancellation produces a known-unsent outcome.
- Browser checks verify partial deletion notification, retained selection, pending controls, and unknown-outcome feedback.

### Done when

Backend suite, frontend build, focused frontend checks, and the above regression tests pass. Document response/status changes. Open the reliability PR; do not bundle cursor pagination into it.

## PR 2 — Message identity and cursor pagination

**Depends on:** PR 1 result contracts.

### Read / change

- Backend: `Models.cs`, `AppJsonContext.cs`, `Endpoints/{Queue,Subscription,DeadLetter}Endpoints.cs`.
- Frontend: `api/{types,client,hooks}.ts`, `components/MessageGrid.tsx`, both detail routes, result-summary utilities.
- Tests: broker fakes and endpoint tests.

### Tasks

1. Expose broker sequence numbers as decimal **strings** in JSON; JavaScript numbers cannot represent every C# `long` exactly.
2. Use entity/subqueue plus sequence number for row keys, inspection, selection, and targeted DLQ actions. `MessageId` remains display metadata and can be duplicated.
3. Define how existing `messageIds` requests are supported or deprecated. Prefer additive sequence-number targeting and reject conflicting selectors rather than silently changing old semantics.
4. Replace offset-based paging with an opaque/string sequence cursor. Return `nextCursor` and a trustworthy `hasMore`.
5. Accumulate short peek batches until the page is full or an empty batch is observed. Probe for another message without consuming it or skipping it in the next cursor.
6. Keep a cursor history for Previous/Next in the UI; reset it when entity or active/DLQ state changes.
7. Validate cursors and cap page size. Do not expose the current page length as the total queue count.
8. Update mocks and fakes to preserve monotonically increasing sequence numbers after deletion; do not derive sequence numbers from current list indices.

### Acceptance tests

- Duplicate MessageIds can be individually inspected/deleted/replayed.
- Sequence numbers beyond `Number.MAX_SAFE_INTEGER` round-trip unchanged.
- Short broker batches still yield complete pages when more messages exist.
- Empty queues, exact-page boundaries, last partial page, and Previous/Next work.
- Removing messages between requests does not produce offset-based duplicates.
- Changes to active/DLQ tab or resource clear stale cursor and selection state.

## PR 3 — Error visibility and stale data

**Depends on:** PR 2 browsing contract.

### Read / change

- `Endpoints/{Queue,Subscription}Endpoints.cs`, `ServiceBusExceptionHandler.cs`.
- Frontend query hooks, resource lists, both detail routes, `MessageGrid.tsx`, create/send/delete dialog call sites.

### Tasks

1. Stop swallowing all peek exceptions and returning HTTP 200 with no messages.
2. Propagate read-request cancellation and use bounded timeouts. Log actual broker failures with useful context.
3. Render distinct loading, empty, missing-resource, failed-request, and stale-data states. A failed list lookup is not proof that an entity does not exist.
4. Preserve previously loaded data after refresh failure, with a stale/last-successful-update indicator and Retry action.
5. Display API ProblemDetails consistently across mutations, with a readable fallback for transport errors.
6. Ensure the empty-state added by the portal PR is rendered only for successful empty results, not failed requests.
7. Keep errors accessible using inline alerts/status regions; do not rely solely on transient notifications.

### Acceptance tests

- Emulator unavailable on initial load, disconnected after a successful load, and reconnected.
- Entity deleted while a detail page is open.
- Empty successful response is visibly different from API failure.
- Failed send/create/delete gets actionable feedback without closing away user input.
- Request cancellation during navigation does not produce spurious failure notifications.

## PR 4 — Count/polling performance

**Depends on:** PR 3 stale/error UI.

### Read / change

- `Helpers.CountMessagesAsync`, list handlers in all three entity endpoint files, `ServiceBusEndpointCache`, `ServiceBusConfig`.
- `api/hooks.ts`, count displays, resource/message refresh commands.

### Tasks

1. Measure baseline broker calls and list latency with multiple entities before optimizing.
2. Separate fast entity metadata discovery from expensive message-count refreshes. Define explicit loading/unavailable count state; do not represent unavailable as exact zero.
3. Add a short-lived server-side count cache with single-flight refresh per entity/subqueue and bounded concurrency across entities.
4. Invalidate affected counts after full or partial mutations. Account for topic-send fan-out and subscription replay fan-out.
5. Propagate read cancellation without allowing one cancelled cache consumer to disrupt every other consumer.
6. Pause message polling in background tabs; support manual refresh and a configurable interval with one source of truth.
7. Show approximate counts consistently and cap work rather than promising exact counts under concurrent broker changes.
8. Ensure cached receivers/count entries for deleted entities do not grow without bound; preserve shared receiver ownership and synchronization.

### Acceptance tests / measurements

- Concurrent requests coalesce equivalent count scans.
- Invalidation refreshes relevant entities and does not retain deleted resources.
- One slow entity does not block metadata for all entities.
- Background-tab polling stops and resumes correctly.
- Publish before/after broker-call counts and response times for a documented namespace size.

## PR 5 — Remaining accessibility and frontend loading

**Depends on:** merged UI PR; best done after PRs 2–3 to avoid rewriting the same components.

### Already done — do not reimplement

- Keyboard-accessible entity/message links, labeled row/select-all checkboxes, indeterminate selection.
- Responsive shell/navigation, named message blade with one close button.
- Approximate tab counts, navigation DLQ badges, explicit empty-message text.

### Remaining tasks

1. Check keyboard focus restoration after blade/dialog close, including when a mutation removes the selected message or resource.
2. Audit contrast, accessible error announcements, long names, and loading/pending controls in both themes.
3. Consider whether named `toolbar` regions need arrow-key navigation or should simply be labeled action groups; choose consistent accessible semantics.
4. Measure bundle composition. Lazy-load routes and heavy editor/dialog code using stable loading/error fallbacks.
5. Confirm Monaco's loading strategy supports the intended local/offline workflow; the current wrapper may fetch editor assets externally.
6. Remove unused UI dependencies only after verifying real imports and measuring impact. A wholesale Mantine-to-Fluent migration is not required.

### Acceptance checks

- Keyboard-only select → inspect → edit/cancel → return-focus workflow.
- No trapped or lost focus when an entity/message disappears.
- Mobile and dark-mode screenshots with long content and error states.
- Report initial JS/CSS sizes before/after; verify all lazy routes/dialogs still load.

## PR 6 — Quality baseline, dependencies, and repeatable browser checks

**Depends on:** merge the functional PRs before final end-to-end coverage. Lint/dependency cleanup can be a separate independent PR if necessary.

### Tasks

1. Fix the existing lint baseline without suppressing rules wholesale: shared context export placement, Axios adapter typings/unused destructuring, route-error typing, and dialog reset effects.
2. Add `npm run lint` to CI once the baseline is green. Preserve the Markdown/docs/editor-only path exclusions.
3. Resolve the NuGet advisory reported for transitive `Microsoft.OpenApi` 2.0.0 by identifying the dependency chain and compatible fixed versions. Verify OpenAPI/Scalar and Release/AOT publishing after upgrading.
4. Establish a small repository-owned browser suite with deterministic API fixtures for navigation, errors, pending operations, and partial results.
5. Provide a separately invoked emulator smoke workflow for queue/topic/subscription creation, send/peek, and representative DLQ behavior. Use unique resource names and clean up only owned resources.
6. Avoid blanket retries hiding regressions. Review the existing test assembly's `Retry(3)` and retain retry behavior only where justified.
7. Validate the final published container serves UI, API, OpenAPI, and client-side deep links.
8. Update README/RUN_LOCAL/AGENTS commands where changed. Inspect the bundled workflow's Development SPA proxy behavior against the executable startup code before documenting it as self-contained.

### Done when

Full lint, frontend build, backend tests, deterministic browser checks, and container smoke pass. Dependency remediation is verified by the relevant audit command. Document any intentionally separate real-service checks and their prerequisites.

## Verification commands

From repository root:

```bash
dotnet run --project test/ServiceBusEmulatorExplorer.Tests/ServiceBusEmulatorExplorer.Tests.csproj
dotnet run --project test/ServiceBusEmulatorExplorer.Tests/ServiceBusEmulatorExplorer.Tests.csproj -- --treenode-filter "/*/*/*/TestMethodName"
git diff --check
```

From `app/sb-explorer-ui`:

```bash
npm ci
npm run build
npm run lint
```

For CI parity on Linux, follow the exact restore/build/test order in `AGENTS.md`. The Windows-friendly executable test runner defaults to Debug; Release targets Linux/musl in the project config. Docker publishing explicitly overrides the RID to `linux-x64`.

Local live verification:

```bash
docker compose -f compose-services.yaml up -d
dotnet run --project src/ServiceBusEmulatorExplorer
# In another terminal, from app/sb-explorer-ui:
npm run dev
```

- UI: `http://localhost:5173`; backend: `http://localhost:5123`; health: `/health`.
- Check for existing dev servers before starting duplicates. They were running during the UI work; do not assume they survive a session restart.
- An ad hoc Playwright installation and smoke script were used under `C:/Users/tombi/AppData/Local/Temp/opencode/portal-preview`. They are local scratch artifacts, not a portable test suite. Build repository-owned coverage in PR 6.

## Copy/paste assignment prompt

> Implement **PR N — [title]** from `docs/IMPROVEMENT_PLAN.md`. Read `AGENTS.md` and inspect git status/diff first. Preserve existing user work and the merged portal-style UI. Complete that PR's backend/frontend/contracts/mocks/tests together, run the specified checks, and open a focused PR. Report exactly what passed, what failed, and what remains. Do not start another numbered PR or merge without instruction.
