# SpecFleet

SpecFleet is an authenticated AI assistant that turns prompts into reviewed artifacts, GitHub
actions, OpenSpec operations, and queued worker tasks. Everything is built test-first against
real infrastructure: a real PostgreSQL container, a real running API process, real HTTP
provider/GitHub servers, and a real browser driving the independently hosted UI.

## Five-project architecture

The repository contains exactly five projects, and the architecture tests enforce that.

| Project | Path | Responsibility |
|---|---|---|
| `SpecFleet.Service` | `src/SpecFleet.Service` | Owns all application and domain logic: use cases, persistence, queue/outbox, provider/GitHub/OpenSpec adapters, task validation, orchestration. |
| `SpecFleet.Api` | `src/SpecFleet.Api` | Thin Minimal API transport. Binds input, authenticates, calls Service, maps results to HTTP. No business rules. |
| `SpecFleet.Workers` | `src/SpecFleet.Workers` | Thin queue consumer. Leases envelopes, calls Service use cases, acknowledges or fails. No task behavior. |
| `SpecFleet.Ui` | `src/SpecFleet.Ui` | Independently built and hosted React + TypeScript dashboard. Not referenced by, bundled with, or served by the API. |
| `SpecFleet.Test` | `tests/SpecFleet.Test` | The single xUnit project: Gherkin acceptance, architecture, Testcontainers integration, and Playwright browser tests. |

Service is the only owner of behavior. Api and Workers are consumers of Service contracts, so
an API request and a queue message share the same rules and validation.

### Vertical slices

Source files live in self-contained feature slices, not global layer folders. Service code sits
under `Features`/`Shared`, Api under `Features`, Workers under `Consumers`. Only `Program.cs`
may sit at a project root, and it must not contain inline route mappings. `ArchitectureTests`
fails the build when these rules are broken.

### Typed results, not exceptions

Expected outcomes use `ServiceResult<TValue>` in `Service/Shared/Results`. Endpoints return
`result.Match(...)`, so success, validation, authorization, conflict, provider, and queue
outcomes map to explicit HTTP responses. Unexpected exceptions are handled once at the host
boundary and are never reshaped into a success.

## Prerequisites

- .NET 10 SDK
- Node.js 22+ and npm
- Docker (for Testcontainers PostgreSQL)
- OpenSpec CLI: `npm install -g @fission-ai/openspec`

## Build and test

```bash
dotnet build SpecFleet.sln
dotnet test SpecFleet.sln
```

Always pass `-nodeReuse:false` in constrained or containerized environments; a shared MSBuild
node server can cause spurious file-lock errors.

The UI builds and lints independently:

```bash
cd src/SpecFleet.Ui
npm ci
npm run build   # tsc -b && vite build
npm run lint    # oxlint
```

Install the Playwright browser once before browser tests:

```bash
dotnet build tests/SpecFleet.Test/SpecFleet.Test.csproj -nodeReuse:false
node tests/SpecFleet.Test/bin/Debug/net10.0/.playwright/package/cli.js install chromium
```

On Linux use the Node CLI above; `playwright.ps1` is PowerShell-only.

## PostgreSQL queue, outbox, and bootstrap worker

PostgreSQL provides both application persistence and the durable task queue, so no external
broker is required.

- Enqueue is idempotent: the payload digest and an idempotency key prevent duplicate work.
- A lease with a visibility timeout lets a consumer own a task without blocking others.
- Failures retry up to a bounded limit and then reach a terminal status.
- State changes write their durable outbox rows in the same transaction as the state change,
  so a reaction is never lost or double-published.

`SpecFleet.Workers` polls the queue, leases an eligible task, and dispatches it through the
Service-owned `WorkerTaskExecutor`. It contains no task rules; acknowledgement, retry, and
failure reporting are the only behavior it owns.

## The independently deployed UI

The API neither bundles nor serves UI assets (`ArchitectureTests.Api_does_not_bundle_or_serve_the_ui_project`
asserts this). The UI therefore reads its API origin and token at runtime from
`window.__SPECFLEET_CONFIG__`, or from `?apiBase=`/`?token=` query overrides, so one build works
against any API origin.

Because the UI is cross-origin, the API enables CORS for the origins listed in
`SpecFleet:Ui:AllowedOrigins`. Only listed origins are trusted.

The dashboard renders conversation history, streaming answers with cancellation, actionable
errors, syntax-highlighted code previews, artifact apply/decline confirmation, OpenSpec
operation controls, and worker task dispatch.

## Credentials and secrets

- AI credentials stay server-side. The browser never receives a provider key; the API streams
  only the answer.
- Provider credentials are stored encrypted (`AesGcmSecretProtector`) and are never echoed in
  responses or logs.
- GitHub mutations require an allowlisted workflow, path containment, and confirmation bound to
  the reviewed preview digest.

## Acceptance contract and traceability

Every scenario in the capability specs maps to an implementation-independent Gherkin scenario in
`tests/SpecFleet.Test/Acceptance/Features`. `tests/SpecFleet.Test/Acceptance/spec-scenario-traceability.md`
records each mapping and its status: `passing`, `pending`, or `blocked`. Blocked work is reported
as blocked, never relabelled as passing.

Treat the Gherkin scenarios as a stable contract. Change a scenario only when the corresponding
specification behavior is approved to change, never to match an implementation detail.

## Test-first workflow (red, green, refactor)

1. Write the acceptance scenario and verify it fails for the missing behavior.
2. Implement the smallest Service change that makes it pass.
3. Refactor with the suite green, then run the full suite before committing.

Tests use real components: `SharedPostgreSql` starts one real PostgreSQL container per run,
`GeminiProtocolServer` is a real local HTTP server speaking the Gemini SSE protocol, and
`ApiProcessHarness` starts the real API process. Fakes and mocks are not substituted for
Service or persistence.

Every test class that touches `SharedPostgreSql` joins the non-parallel `PostgreSqlCollection`,
because the container is process-wide and each test truncates the shared tables.

## Environment setup for real components

- Start Docker before running any Testcontainers-backed test.
- Set `GEMINI_API_KEY` to verify the hosted Gemini provider. Without it, the adapter is verified
  against `GeminiProtocolServer` and hosted verification is reported as skipped, never passing.
- Set `SPECFLEET_SANDBOX_OWNER`, `SPECFLEET_SANDBOX_REPO`, and `SPECFLEET_SANDBOX_TOKEN` to a
  disposable repository to verify live GitHub side effects; optionally set
  `SPECFLEET_SANDBOX_WORKFLOW` to an allowlisted workflow for dispatch. Without them,
  live-sandbox verification is reported as skipped, never passing. Never point these at the
  SpecFleet repository itself; the tests refuse that configuration. A least-privilege GitHub
  App installation is the intended production credential, but a repository-scoped token with
  write access verifies the same code paths.
- Set `OpenSpec:WorkspaceRoot` to the directory that OpenSpec operations may touch. The
  `OpenSpecWorkspaceGuard` enforces containment within it and an allowlist of operations.

## Deployment smoke tests, monitoring, and rollback

- Smoke test: start the API, request `/health`, run one authenticated chat ask, and run one
  read-only OpenSpec operation (`Validate`) against the deployed workspace root.
- Smoke test the UI separately by loading the built bundle from its own origin and confirming
  it reaches the API origin configured at runtime.
- Monitor the API and worker hosts for `Failed` task status, queue depth, and retry exhaustion;
  the durable status column is the source of truth.
- Roll back by redeploying the previous API, Workers, and UI builds. Because queue state lives in
  PostgreSQL and is idempotent, a rollback does not duplicate or lose queued work.

## CI release gates

`.github/workflows/ci.yml` runs the gates in order and fails explicitly on any failure:

1. `openspec validate add-ai-chatbot-github-integration --strict`
2. UI build (`tsc` + `vite`) and lint (`oxlint`)
3. `dotnet format SpecFleet.sln --verify-no-changes`
4. The full xUnit suite: Gherkin acceptance, architecture, Testcontainers integration,
   Playwright browser tests, and AI harness checks

Credential-blocked checks are surfaced through the traceability map rather than being waived in
CI.