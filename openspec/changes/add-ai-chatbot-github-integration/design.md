# Design

## Context

See proposal.md for motivation and specs/ for externally visible behavior. The repository currently has no application source, solution, database schema, queue, worker, or deployment configuration. This design therefore defines the target application structure while retaining the prerequisite gate for the UI Wizard, Postgres Queue, Orchestrator, Bootstrap Worker, and Free AI Pipeline. The UI is a separate project and deployment, not content hosted by the API.

## Goals / Non-Goals

**Goals:**
- Deliver five projects in total: the `SpecFleet.Service`, `SpecFleet.Api`, and `SpecFleet.Workers` backend projects, one `SpecFleet.Ui` React/TypeScript project, and one `SpecFleet.Test` xUnit project.
- Keep all application, domain, persistence, provider, GitHub, and orchestration logic in the Service class library. API and Workers are thin consumers responsible only for wiring and transport/consumer integration.
- Organize feature behavior into self-contained vertical slices with DDD boundaries and low folder depth.
- Build, host, and deploy the React/TypeScript UI independently from the API.
- Use one xUnit test project for backend, integration, and browser-driven UI verification.

**Non-Goals:**
- Add separate .NET projects for domain, infrastructure, or individual features.
- Put application or domain behavior in Minimal API route handlers or worker consumers.
- Bundle or serve the UI from the API deployment.
- Automatically merge pull requests or write generated files directly to the default branch.
- Execute arbitrary model-generated shell commands, scripts, or OpenSpec CLI arguments.

## Decisions

### Keep backend responsibilities in three .NET projects and business behavior in Service

Create the Service, Api, and Workers .NET projects, one `SpecFleet.Ui` frontend project, and one `SpecFleet.Test` xUnit project: five projects total. `SpecFleet.Service` is the class library and owns use cases, domain rules, persistence, Gemini/GitHub/OpenSpec adapters, task validation, and orchestration behavior. `SpecFleet.Api` maps HTTP to Service use cases; `SpecFleet.Workers` adapts queue messages to Service use cases; neither project contains business logic. `SpecFleet.Test` references the backend projects as needed and is the only automated test project. `SpecFleet.Ui` has its own frontend project manifest, toolchain, build, and deployment configuration and is not nested under Api.

This keeps backend and frontend deployment boundaries explicit and prevents API or worker entry points from becoming competing owners of rules. Do not introduce extra class libraries to express DDD layers; keep those boundaries within the Service assembly. Alternatives considered: separate Domain/Application/Infrastructure projects would add unrequested projects; placing rules in API or Workers would duplicate behavior and violate the stated ownership boundary.

### Organize Service by self-contained vertical feature/use-case slices

Place each use case in its feature slice under `SpecFleet.Service`, keeping its domain behavior, request/result contracts, validation, persistence coordination, and provider/orchestrator interactions together where they belong. Use DDD concepts inside each slice rather than a global folder tree organized only by technical layer. Keep folder depth modest and names searchable; use clear, corresponding file names for folders and types, for example `Features/Chat/Ask/ChatAsk.cs` and `Features/Chat/Ask/ChatAskHandler.cs`. Share code only when it is genuinely common across use cases.

Alternatives considered: a global `Controllers/Services/Repositories` layout obscures complete use cases and encourages cross-feature coupling; a deeply nested clean-architecture tree conflicts with the requested flat, searchable structure.

### Keep Minimal API route definitions as transport wiring

Define Minimal API endpoints in self-contained folders in `SpecFleet.Api`, grouped by feature/use case. Endpoint definitions bind and validate transport input, enforce authentication/authorization policies, call the corresponding Service operation, and map its result to HTTP. They do not implement business rules or access persistence/provider integrations directly. Keep route registration composable so application startup only wires endpoint groups and dependencies.

Alternatives considered: controllers add a different endpoint model than requested; putting full use cases in route delegates would make the API project a second logic layer.

### Keep workers as thin queue consumers

The Workers project registers consumers and dependencies, translates queue envelopes into Service use-case calls, and reports acknowledgement/retry outcomes. Payload validation and task behavior remain owned by Service so API-triggered and queue-triggered work share the same rules. Avoid feature logic in consumer classes.

Alternatives considered: implementing task rules in workers would make behavior unavailable to API callers and produce divergent validation.

### Keep the React/TypeScript UI in its own project and deployment

Use the latest stable React and TypeScript versions supported by the selected toolchain when implementation begins, and pin resolved versions in the UI project's lockfile. Keep its source, build, runtime configuration, and deployment pipeline in the standalone `SpecFleet.Ui` project. Deploy the compiled UI to its own hosting location independently from the API; the API must not contain, bundle, or serve UI assets. The UI calls authenticated API endpoints over HTTP and streaming responses. Configure allowed UI origins and credentialed CORS deliberately, and use the deployment's authentication mechanism across the separate origins.

Alternatives considered: serving static UI assets from the API would couple releases and availability, contrary to the requested independent deployment; rendering the dashboard in .NET would not satisfy the React/TypeScript requirement.

### Use PostgreSQL for persisted application state

Persist conversation, preview, approval, GitHub action, and worker task state in PostgreSQL. Keep database access and persistence behavior in the Service project and define schema/migration ownership there, following the existing queue/orchestrator contract when it is available. Protect provider credentials using deployment-managed encryption keys; do not store decryption keys beside ciphertext or expose secrets to the client.

Alternatives considered: a separate persistence project is outside the requested project scope; an in-memory or file-backed store cannot provide durable shared state for API and worker consumers.

### Use a single xUnit project for verification

Place all automated tests in `SpecFleet.Test` using xUnit. Cover Service use cases and adapters with unit/contract tests, exercise PostgreSQL and queue boundaries with integration tests, and verify the separately deployed React UI through browser-driven tests launched from the same xUnit project where UI behavior requires a browser. Do not add Jest, Vitest, or a second test project.

Alternatives considered: separate frontend and backend test projects would violate the one-test-project requirement; relying only on manual UI verification would not provide repeatable coverage of chat and approval flows.

### Separate preview generation from approved actions

Keep generation non-mutating: Service returns typed artifacts and validation results, and the UI presents those results before any external action. Bind each approval to the immutable preview and target. Authorized use cases perform issue creation, review-branch/file changes, pull requests, configured workflow dispatch, OpenSpec operations, and supported worker dispatches as distinct actions. GitHub writes use a least-privilege GitHub App and never write generated content directly to the default branch.

Alternatives considered: allowing the model to invoke actions directly removes user review and creates an unsafe trust boundary; combining multiple mutations into one implicit “apply” operation makes outcomes and retries difficult to reason about.

### Constrain OpenSpec and worker execution

Expose a fixed set of OpenSpec operations through Service: inspect/list, proposal preparation, validation, and explicitly confirmed state-changing actions. Invoke the CLI without a shell, with operation-specific arguments, from a workspace-contained path, and with bounded time/output. Accept only the six specified worker task categories and typed payloads; validate at both the Service/API boundary and worker consumption boundary. Generated source and configuration remain data, never executable command input.

Alternatives considered: arbitrary CLI passthrough or generated shell execution allows workspace escape and command injection; a typed allowlist makes supported behavior auditable and testable.

### Use a server-mediated streaming chat

The React client calls authenticated API endpoints. Use streaming HTTP (SSE where supported) for assistant output and REST for previews, approvals, action submission, and status retrieval. The API streams results from Service without owning provider logic; Service invokes the configured Gemini adapter. Persist conversation and action status independently of the stream so reconnects do not turn interrupted responses or long-running tasks into false successes.

Alternatives considered: WebSockets add bidirectional connection complexity for primarily one-way model output; non-streaming HTTP does not meet the incremental-response requirement.

## Risks / Trade-offs

- [The listed foundation proposals are not present in this repository] → Gate integration on verifying the UI Wizard, Postgres/queue, orchestrator, bootstrap worker, and AI pipeline contracts; sequence missing prerequisites before enabling the bot.
- [The separately deployed UI and API have cross-origin and rollout concerns] → Configure explicit trusted UI origins, secure authentication, CORS, and compatible API/UI versioning; verify staged rollout and rollback for each deployment independently.
- [The five-project scope concentrates infrastructure adapters and migrations in the Service assembly] → Keep adapters and schema concerns in their feature slices with clear internal boundaries; test those boundaries in the single xUnit project.
- [Gemini model names and free-tier quotas may change] → Configure a supported model ID at deployment and expose quota/rate-limit errors.
- [A compromised account or deceptive generated preview could cause repository changes] → Bind approval to an immutable preview digest and target, enforce repository permissions server-side, isolate writes to review branches, and log outcomes.
- [OpenSpec CLI or worker input could escape intended scope] → Use allowlisted operations and task types, no shell evaluation, strict argument/schema validation, workspace containment checks, and bounded execution.
- [Prompts and generated artifacts can contain sensitive project data] → Restrict access, define retention, avoid logging credentials, and document provider data handling before production use.
- [Streaming or long-running work may be interrupted] → Persist action/task status independently from the stream and expose explicit cancellation and failure states.

## Migration Plan

1. Verify or complete the UI Wizard, Postgres/queue, orchestrator, Bootstrap Worker, and Free AI Pipeline prerequisites and agree on their API/task contracts.
2. Scaffold the .NET 10 Service, Api, Workers, and Test projects, plus the separate React/TypeScript Ui project; verify there are exactly five projects and that the UI has an independent build and deployment pipeline with no UI assets in Api.
3. Provision PostgreSQL, repository-scoped GitHub App permissions, provider secret encryption, model configuration, and workflow/task allowlists.
4. Deploy Service use cases and their xUnit coverage, then wire authenticated Minimal API routes and thin worker consumers; independently deploy the UI to its own hosting location and configure its trusted API origin. Initially expose read/generate behavior behind feature flags.
5. Enable issue, review-branch/PR, workflow, OpenSpec, and worker actions only after authorization, preview confirmation, audit records, and boundary tests pass.
6. Roll back by disabling the feature flag and action endpoints, draining or cancelling queued tasks where safe, and retaining completed action/audit records. Revoke installation/provider credentials if compromise is suspected; do not automatically revert already-created GitHub issues or pull requests.

## Open Questions

- Which deployment secret-management mechanism and key-rotation policy will provide the encryption key for persisted provider credentials?
- What retention period and access policy should apply to conversation prompts, generated previews, and action audit records?
- Which Gemini model IDs and request quotas are available to the target deployment when implementation begins?
