# AGENTS.md

Repository-specific knowledge for SpecFleet. Keep this current.

## Build and test

The .NET 10 SDK lives at `~/.dotnet` and ICU is extracted locally. Export both before running `dotnet`:

```bash
export PATH="$HOME/.dotnet:$PATH"
export LD_LIBRARY_PATH="/tmp/icu/extracted/usr/lib/x86_64-linux-gnu"
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
- `ApiProcessHarness` starts the real `SpecFleet.Api` process for HTTP/stream/authorization tests.
- Gherkin scenarios without bindings fail as "No matching step definition" (pending). That is intentional until the owning section is implemented.
- xUnit here is 2.9.3: dynamic skip (`Assert.Skip`) is xUnit v3-only and surfaces as a failure. Report credential-blocked cases explicitly in the traceability map rather than skipping dynamically.
- Reqnroll 3.3.4 exposes `ScenarioContext.Pending()`; `ScenarioExecutionStatus` is read-only.

## OpenSpec workflow

- Change: `openspec/changes/add-ai-chatbot-github-integration` (spec deltas, `design.md`, `tasks.md`).
- Validate with `openspec validate add-ai-chatbot-github-integration --strict`.
- Map every spec scenario to its acceptance scenario in `tests/SpecFleet.Test/Acceptance/spec-scenario-traceability.md`, and record `passing`/`pending`/`blocked` status there.
- Never mark a scenario or task complete when it was blocked or not executed. Report blocked work explicitly.

## Environment notes

- No hosted Gemini credential is present. `GeminiChatCompletionProvider` streaming/error/cancellation paths are verified against `GeminiProtocolServer`; hosted-provider verification is blocked and must be reported as such.
- Preview and get explicit approval before any GitHub or orchestration mutation. Do not push branches, open PRs, or run orchestration without approval.
