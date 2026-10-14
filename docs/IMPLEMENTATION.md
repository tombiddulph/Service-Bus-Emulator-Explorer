# Improvement milestones

Each milestone has a separate commit. Optional export/import, templates, filters
and built-in authentication remain outside this implementation.

1. **Reliability and validation** — meaningful peek errors, stale/error UI,
   bounded pagination, cancellation, entity validation and regression tests.
2. **Testing** — Vitest/Testing Library, Playwright, lint/test CI, a separate
   real-emulator runner, published AOT smoke checks and failure artifacts.
3. **Safety** — typed destructive confirmations, server-enforced read-only mode,
   restricted CORS, loopback Compose ports and remote-deployment guidance.
4. **Performance** — refresh controls, hidden-tab polling disabled, cancellation,
   two-second single-flight count caching, mutation invalidation, cursor paging
   and receiver/sender retirement when entities are deleted.
5. **Diagnostics and contracts** — liveness/readiness endpoints, component status
   in the UI, low-cardinality metrics, OpenAPI-generated TypeScript aliases,
   explicit request types and contract-drift CI checks.
6. **Developer experience** — working standalone mocks, .NET 10 SDK selection,
   lockfile-based publish, corrected instructions, MIT license, digest-pinned
   emulator/SQL images, Renovate configuration and a local verification script.

Published-container verification additionally caught missing JSON metadata for
OpenAPI enum wrappers, and the emulator's one-hour TTL limit is now enforced with
compatible creation defaults. Cached receiver retirement preserves canonical
lock identities; already queued requests receive an entity-not-found response
instead of operating on a disposed receiver.

## Verification

`node scripts/verify.mjs` runs frontend build/lint/unit/browser checks, Release
backend build/tests and generated-contract drift checks. The real-emulator runner
tests the actual published native executable and embedded UI in a disposable
Compose stack. See README and the integration workflow for setup.

Enable the Renovate GitHub integration separately to activate update proposals.
AMQP readiness is transport reachability, not authentication; see OBSERVABILITY.md.
