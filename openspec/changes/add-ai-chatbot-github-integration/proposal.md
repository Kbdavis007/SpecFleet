# Proposal

## Why

SpecFleet has no interactive interface for turning feature requests into reviewed OpenSpec proposals, code, and repository actions. A chat assistant can make the project easier to extend, provided generated changes are previewed and explicitly approved before they affect GitHub or run orchestration tasks.

## What Changes

- Add a dashboard chat experience with conversation history, streaming answers, and readable code previews.
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

- New React dashboard component and chat/API surfaces; the repository currently has no application source or UI framework to extend.
- New backend API gateway, Gemini client, persistence for conversation/task state, and secret handling. The requested Postgres API-key storage must use encrypted-at-rest secrets and must never expose credentials to the browser.
- GitHub REST API integration and worker/orchestrator interfaces; no existing GitHub integration, worker, queue, or Postgres schema is present in this repository.
- OpenSpec CLI integration must be server-side, allowlisted, and scoped to the project workspace. Existing documented prerequisites (UI Wizard, Bootstrap Worker, Postgres Queue, Orchestrator, and Free AI Pipeline) are not present here and must be verified or supplied before deployment.
- Model identifiers and free-tier availability must be confirmed against the current Gemini API when implementation begins; model selection should not rely on unsupported fixed aliases.
