# Proposal

## Why

SpecFleet has no interactive interface for turning feature requests into reviewed OpenSpec proposals, code, and repository actions. A chat assistant can make the project easier to extend, provided generated changes are previewed and explicitly approved before they affect GitHub or run orchestration tasks.

## What Changes

- Add a dashboard chat experience with conversation history, streaming answers, and readable code previews, using React and TypeScript.
- Implement the backend in .NET 10/C# with separate Service, Api, and Workers projects plus one xUnit Test project, and implement the dashboard as a fifth, independently built React/TypeScript UI project. Keep application and domain behavior in the Service library; Api and Workers provide only transport/consumer wiring.
- Organize Service logic as self-contained vertical slices by feature/use case, using DDD boundaries and a flat, searchable folder/file layout. Implement HTTP endpoints with Minimal APIs in self-contained API folders and use PostgreSQL for persistence.
- Build acceptance scenarios from the capabilities' spec scenarios before feature implementation, then develop test-first with Gherkin executed in the single xUnit project, architecture tests, Testcontainers-backed integration tests, and Playwright UI acceptance tests.
- Add a server-side Gemini integration for chat and generation of code, documentation, worker and infrastructure artifacts, and OpenSpec slices.
- Add controlled GitHub actions for issue creation, branch/file changes, pull request creation, and approved workflow dispatches.
- Add OpenSpec browsing, proposal generation, validation, and task dispatch through a constrained server-side orchestrator.
- Add worker-side validation for generated artifacts and task payloads, with action status and errors visible to the user.
- Require authenticated, authorized user approval before any repository write, pull request, workflow dispatch, or worker task that can change project state. Do not execute arbitrary model-generated shell commands.

## Capabilities

### New Capabilities
- `ai-chat`: Interactive dashboard conversations, streaming model responses, artifact previews, and safe generation of project artifacts.
- `github-repository-actions`: Authorized GitHub issue, branch/file, pull request, and workflow operations with explicit user approval.
- `openspec-worker-orchestration`: OpenSpec inspection and proposal/validation actions, plus validated dispatch of supported worker tasks.

### Modified Capabilities

None. The project has no existing OpenSpec capability specs.

## Impact

- New React/TypeScript dashboard as a separate UI project with its own build and independent hosting/deployment location; it is not nested in or served by Api. The UI communicates with the API over authenticated HTTP and streaming endpoints.
- New .NET 10/C# backend projects (Service, Api, Workers) and one xUnit Test project, for five total projects including the separate UI project. Add PostgreSQL persistence, Gemini integration, and secret handling. The Service class library owns application/domain logic and integrations; API endpoints and workers are thin consumers. Persisted provider credentials must be encrypted at rest and never exposed to the browser.
- GitHub REST API integration and worker/orchestrator interfaces; no existing GitHub integration, worker, queue, or Postgres schema is present in this repository.
- OpenSpec CLI integration must be server-side, allowlisted, and scoped to the project workspace. Existing documented prerequisites (UI Wizard, Bootstrap Worker, Postgres Queue, Orchestrator, and Free AI Pipeline) are not present here and must be verified or supplied before deployment.
- Model identifiers and free-tier availability must be confirmed against the current Gemini API when implementation begins; model selection should not rely on unsupported fixed aliases.
