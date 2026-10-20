# Agent notes — SpecFleet OpenSpec change `add-ai-chatbot-github-integration`

Working notes for the autonomous run that completed sections 5–7. Kept deliberately
factual: what was done, what is blocked, and why. Nothing here overrides the OpenSpec
change or `AGENTS.md`; it is a scratchpad that records environment limits.

## Environment facts

- .NET 10 SDK at `~/.dotnet`. Export `PATH="$HOME/.dotnet:$PATH"` and
  `LD_LIBRARY_PATH="/tmp/icu/extracted/usr/lib/x86_64-linux-gnu"` before `dotnet`.
- Always pass `-nodeReuse:false`; the shared MSBuild node server causes lock errors.
- Docker must run for Testcontainers (real PostgreSQL). It is available here.
- Playwright browsers are not preinstalled. Install with
  `node .playwright/package/cli.js install chromium` from
  `tests/SpecFleet.Test/bin/Debug/net10.0` (the `playwright.ps1` script is PowerShell
  and cannot be run with `dotnet` on Linux).
- npm registry is reachable; the UI project installs and builds locally.
- No hosted Gemini credential (`GEMINI_API_KEY`) is present.
- No least-privilege GitHub App credential for an isolated sandbox repo is present.

## Section 5 (completed and pushed)

- Commit `b07ba37` — OpenSpec + worker orchestration.
- Root cause found while wiring the consumer: the durable envelope originally stored the
  approved payload as a nested `JsonElement`. PostgreSQL `JSONB` normalizes object key
  order, so the SHA-256 payload digest no longer matched on read-back and every worker
  task failed validation. Fix: carry the approved payload as the exact JSON string inside
  the envelope, so the digest is computed over the same bytes the user approved.
- API JSON now serializes enums as strings (`JsonStringEnumConverter`) so worker task
  status is stable and self-describing for the UI.

## Blockers (cannot be resolved without user-provided credentials)

| Task | Blocked clause | Reason |
|---|---|---|
| 3.2 | real-credential verification | no `GEMINI_API_KEY`; adapter is verified against a real local Gemini-protocol HTTP server instead |
| 3.3 | provider-generated harness case | same missing Gemini credential |
| 4.1 | live GitHub sandbox side effects | no least-privilege GitHub App credential for an isolated sandbox repository |
| 4.2 | live sandbox side effects | same |
| 4.3 | live sandbox release verification | same |

These are reported as `blocked` in `spec-scenario-traceability.md`, never relabelled as
passing. They are the only items left that need user help.

## Section 6 (UI)

- The Ui project was the unmodified Vite/React starter. It was replaced with a real
  dashboard: conversation history, streaming answers with cancellation, actionable
  errors, code previews with syntax highlighting, artifact apply/decline confirmation,
  OpenSpec controls, and worker task controls.
- Design constraint: the UI is independently hosted and the API must not serve its
  assets. The UI therefore takes its API base URL and token from a runtime
  `window.__SPECFLEET_CONFIG__` object (or a `?apiBase=`/`?token=` override), not from a
  build-time constant, so the same build works against any API origin.
- Cross-origin calls require CORS on the API; allowed origins come from
  `SpecFleet:Ui:AllowedOrigins`.
- `UiHostHarness` builds and serves the real Vite output from its own origin and each
  Playwright test starts the real API process, so browser verification exercises the
  genuine transport, streaming, and persistence paths.
- Bug found and fixed while wiring the UI: `ProcessOpenSpecCommandRunner` let a missing
  `openspec` executable surface as an unhandled `Win32Exception`, which the API returned
  as an opaque 500. It now returns an unsuccessful `OpenSpecCommandResult` carrying the
  cause, so an unavailable CLI is reported accurately instead of as an unexpected error.
- Bug found and fixed while running the full suite: the shared Testcontainers PostgreSQL
  database was used by several parallel test classes, and each truncates the shared
  tables. One class's reset could delete another class's in-flight rows, making the
  streaming tests fail intermittently. All classes that use `SharedPostgreSql` now join
  one non-parallel xUnit collection (`PostgreSqlCollection`).

## Section 7

- Release gates run the full xUnit suite (Gherkin acceptance + architecture +
  Testcontainers integration + Playwright + AI harness) and formatting checks.
- `dotnet format SpecFleet.sln --verify-no-changes` is clean; the UI `npm run build`
  (tsc + vite) and `npm run lint` (oxlint) are clean.
- Full suite: 108 passed, 0 failed, 0 skipped.
- Blocked checks are surfaced explicitly; nothing is silently skipped.

## OpenSpec workflow reminders

- `openspec validate add-ai-chatbot-github-integration --strict`.
- Every spec scenario is mapped in
  `tests/SpecFleet.Test/Acceptance/spec-scenario-traceability.md` with a status.
- Preview and get explicit approval before any GitHub mutation, push, or orchestration
  run. The user approved pushing this branch; no pull request is opened.
