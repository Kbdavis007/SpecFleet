# AGENTS.md

Repository-specific knowledge for SpecFleet. Keep this current.

## Build and test

The .NET 10 SDK lives at `~/.dotnet` and ICU is extracted locally. The ICU extraction path can change after a sandbox reset, so resolve it dynamically. Export both before running `dotnet`:

```bash
export PATH="$HOME/.dotnet:$PATH"
export LD_LIBRARY_PATH="$(dirname "$(find /tmp -name 'libicuuc.so*' 2>/dev/null | head -1)")"
dotnet build SpecFleet.sln -nodeReuse:false
dotnet test tests/SpecFleet.Test/SpecFleet.Test.csproj -nodeReuse:false
```

Always pass `-nodeReuse:false`; the shared MSBuild node server causes spurious lock errors here.
Docker (dockerd) must be running for Testcontainers-backed tests.

## Architecture rules (enforced by tests)

- Exactly five projects: Service, Api, Workers, Ui, Test.
- Service owns all application/domain logic. Api and Workers are thin transport/host consumers.
- Source files live under vertical slices: Service under `Features`/`Shared`, Api under `Features`, Workers under `Consumers`. Only `Program.cs` may sit at the project root.
- `Program.cs` must not contain inline route mappings (`.MapGet(`, `.MapPost(`, `.MapPut(`, `.MapDelete(`). Put route definitions in a `Features/*Endpoints.cs` slice instead.
- Do not create global layer folders such as `Repositories` or `Services`.

`tests/SpecFleet.Test/Architecture/ArchitectureTests.cs` enforces these; run it after adding files.

## Conventions

- Typed results via `ServiceResult<TValue>` in `Service/Shared/Results`. Expected failures are typed results, not exceptions.
- Minimal API endpoints return `result.Match(...)` from Service contracts.
- The API cannot `yield` inside a `catch`; capture the error and yield after the block (CS1631).

## Testing conventions

- Acceptance scenarios live in `tests/SpecFleet.Test/Acceptance/Features/*.feature`; step bindings go in `tests/SpecFleet.Test/Features/<Capability>/*Steps.cs`.
- Use real infrastructure. `SharedPostgreSql` starts one real PostgreSQL container per test run; `GeminiProtocolServer` is a real local HTTP server that speaks the Gemini SSE protocol so the real adapter is exercised. Do not substitute fakes or mocks for Service or persistence.
- Every test class that touches `SharedPostgreSql` is annotated `[Collection(PostgreSqlCollection.Name)]`. The collection disables parallelization because the container is process-wide and each test truncates the shared tables; without it, a parallel reset deletes another test's in-flight rows and streaming tests fail intermittently.
- `ApiProcessHarness` starts the real `SpecFleet.Api` process for HTTP/stream/authorization tests. Pass `uiOrigin:` to set `SpecFleet:Ui:AllowedOrigins__0` so the independently hosted UI can call the API cross-origin.
- `UiHostHarness` builds the real Vite output (`npm run build`) and serves `src/SpecFleet.Ui/dist` from its own origin for Playwright. Install the browser once with `node .playwright/package/cli.js install chromium` from `tests/SpecFleet.Test/bin/Debug/net10.0` (the `playwright.ps1` script is PowerShell-only).
- Gherkin scenarios without bindings fail as "No matching step definition" (pending). That is intentional until the owning section is implemented.
- xUnit here is 2.9.3: dynamic skip (`Assert.Skip`/`Assert.SkipUnless`) is xUnit v3-only and surfaces as a compile error. Use `RequiresConfigurationFactAttribute` (in `tests/SpecFleet.Test/Support`) for credential-gated cases: it skips at discovery time so a blocked clause reports as `Skipped`, never `Passed`. A credential-blocked clause must never be relabelled as passing.
- Reqnroll 3.3.4 exposes `ScenarioContext.Pending()`; `ScenarioExecutionStatus` is read-only.

## OpenSpec workflow

- Change: `openspec/changes/add-ai-chatbot-github-integration` (spec deltas, `design.md`, `tasks.md`).
- Validate with `openspec validate add-ai-chatbot-github-integration --strict`.
- Map every spec scenario to its acceptance scenario in `tests/SpecFleet.Test/Acceptance/spec-scenario-traceability.md`, and record `passing`/`pending`/`blocked` status there.
- Never mark a scenario or task complete when it was blocked or not executed. Report blocked work explicitly.

## Credential-gated verification

Credential-dependent clauses are gated so that a missing credential reports as `Skipped`, never `Passed`.

- Hosted Gemini (task 3.2): set `GEMINI_API_KEY`. `HostedGeminiVerificationTests` runs against the real provider when it is set, otherwise skips. CI passes the `GEMINI_API_KEY` secret through. The free tier returns `429` under load; a 429 fails red by design rather than faking a pass, so a CI run can be red on a transient rate limit even when the code is unchanged. Re-run failed jobs rather than weakening the assertion.
- Live GitHub sandbox (tasks 4.1-4.3): set `SPECFLEET_SANDBOX_OWNER`, `SPECFLEET_SANDBOX_REPO`, and `SPECFLEET_SANDBOX_TOKEN` (falls back to `GITHUB_TOKEN`), plus `SPECFLEET_SANDBOX_WORKFLOW` to exercise dispatch. `LiveGitHubSandboxTests` then creates a real issue, writes files on a real review branch, opens a real pull request, and dispatches a real allowlisted workflow against a disposable repository, cleaning each up afterwards. The tests refuse any configuration naming the SpecFleet repository. Without the variables they skip. Use the disposable `kbdavis07/testing-1` repository and leave it restored (branches `[main]`, files `[index.html]`, no open issues or PRs).
- The available GitHub credential is a repository-scoped user token, not a least-privilege GitHub App installation. It cannot create repositories (`POST /user/repos` returns 403), so a sandbox repository must already exist.
- Only the least-privilege GitHub App form of the live-sandbox clause stays `blocked` in the traceability map; the side-effect behavior itself is verified.

## Live API lessons

- The local `GitHubProtocolServer` stub can hide real-API contract errors. Running the real `HttpGitHubClient` against GitHub caught a defect the stub missed: `POST /git/refs` needs a commit SHA, not a branch name, and the old client treated *any* 422 as "branch already exists", so file writes silently targeted a branch that was never created. `EnsureReviewBranchAsync` now resolves the base branch to a commit SHA and only tolerates a 422 that reports an existing reference. Keep the stub's validation faithful to GitHub (see `GitHubProtocolServer`).

## Environment notes

- Preview and get explicit approval before any GitHub or orchestration mutation. Do not push branches, open PRs, or run orchestration without approval. The branch for this change is `feature/ai-chat-artifact-generation-section-3`; do not push to the default branch.
