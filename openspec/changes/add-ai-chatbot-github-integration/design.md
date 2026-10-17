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
- Define stable, implementation-independent acceptance contracts for every scenario in the capability specs and use them to guide test-first delivery.
- Make expected success and failure outcomes explicit through typed response/result values; use tests and deployment checks to reduce risk without promising that tests prove the absence of all defects.

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

### Return typed results for expected outcomes and use domain events selectively

Service use cases return typed result/response objects that represent successful values and expected business or operational failures, such as invalid input, denied access, conflicts, unavailable providers, and retryable queue errors. Pass these results through Api and Workers; map them to stable HTTP, streaming, or acknowledgement outcomes at those transport boundaries. Do not use exceptions for expected control flow. Handle unexpected exceptions at host or infrastructure boundaries with explicit logging and an error response/status; do not swallow them or convert them into success-shaped results.

Use domain events only when a meaningful domain state transition needs to notify decoupled reactions; do not emit events mechanically for every operation. If a persisted state change must reliably trigger queue work, use a transactional outbox or the agreed queue contract and verify delivery/retry behavior with real infrastructure.

Alternatives considered: exceptions for expected validation and business outcomes obscure normal control flow; publishing events for every method call creates unnecessary coupling and operational complexity.

### Establish acceptance contracts before feature implementation

Before implementing feature behavior, translate every scenario in the three capability specs into an implementation-independent Gherkin acceptance scenario. Store `.feature` files and xUnit bindings in the single `SpecFleet.Test` project, using a Gherkin runner compatible with xUnit. Treat these scenarios as stable behavior contracts: update them only when the corresponding approved spec changes, not to match an implementation detail. Trace every spec scenario to at least one executable acceptance scenario and report missing, pending, or blocked coverage explicitly.

Follow acceptance-test-driven development and layered test-driven development: establish the acceptance scenarios first, add a failing test for each subsequent behavior or defect, implement the smallest change that makes it pass, then refactor while keeping the suite green. For bug fixes, reproduce the defect with a failing regression test before changing implementation, then rerun that test and the relevant broader suite. Avoid tests written only to inflate line or branch coverage; prioritize meaningful cases, boundaries, and failure behavior required by the specs.

Alternatives considered: writing tests after implementation risks baking implementation assumptions into the contract; percentage-only coverage targets reward uninformative tests rather than behavior.

### Exercise real infrastructure and the independently deployed UI

Do not use mocks or fakes in automated tests. Instantiate real domain objects and application collaborators; use Testcontainers from `SpecFleet.Test` to run real PostgreSQL and the selected queue/broker for integration and acceptance tests. Use Playwright browser tests, invoked from the same xUnit project, against the independently hosted UI and real API in a controlled test environment.

Gemini and GitHub are external hosted systems and cannot be made real by Testcontainers. Test their adapters and relevant end-to-end acceptance scenarios against dedicated real provider credentials and isolated Gemini/GitHub sandbox resources. Never silently replace unavailable external dependencies with substitutes or report those scenarios as passing. If sandbox credentials/resources are unavailable, mark the dependent tests as not run/blocked and gate claims of complete acceptance verification or release on running them in the configured environment. Keep tests isolated, clean up sandbox-created resources, and avoid production repositories or credentials.

Alternatives considered: mocked integration tests do not verify provider protocols, credentials, or actual side effects; live production-account tests create unacceptable data and security risks.

### Enforce architecture and generated-code checks in the test project

Add architecture tests in `SpecFleet.Test` that inspect the solution/assemblies and source layout to enforce exactly five projects, allowed project references, Service ownership of application behavior, thin Api/Workers boundaries, independently located UI assets, and vertical-slice dependency rules. Keep these checks focused on stable architectural constraints rather than incidental naming or implementation details.

Build an AI-generated-code harness in the same test project. Feed generated artifacts through the real validation path and check required file/folder structure, project-boundary and dependency rules, formatting/static analysis, compilation, and applicable tests. Where a harness case calls Gemini, use the configured real provider and record model/configuration; the harness validates defined constraints and build/test outcomes but cannot prove arbitrary generated code is defect-free.

Alternatives considered: source-level checks run only manually can drift from the architecture; claiming an AI harness proves correctness would overstate what finite automated checks establish.

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
- [Finite tests cannot establish that production has zero defects, and unconfigured third-party sandboxes leave coverage gaps] → Trace all spec scenarios, run real Testcontainers and sandbox-backed acceptance checks, publish skipped/blocked scenarios, and gate release on required checks; retain staged smoke tests, observability, and rollback.
- [Uncontrolled use of live Gemini/GitHub resources can incur cost or mutate unintended repositories] → Use dedicated least-privilege sandbox resources, explicit test-only configuration, bounded test data, and cleanup; never fall back to production credentials.
- [Overuse of response objects or domain events can obscure failures or add complexity] → Model expected outcomes explicitly, keep unexpected failures observable at boundaries, and publish events only for meaningful decoupled reactions.
- [Gemini model names and free-tier quotas may change] → Configure a supported model ID at deployment and expose quota/rate-limit errors.
- [A compromised account or deceptive generated preview could cause repository changes] → Bind approval to an immutable preview digest and target, enforce repository permissions server-side, isolate writes to review branches, and log outcomes.
- [OpenSpec CLI or worker input could escape intended scope] → Use allowlisted operations and task types, no shell evaluation, strict argument/schema validation, workspace containment checks, and bounded execution.
- [Prompts and generated artifacts can contain sensitive project data] → Restrict access, define retention, avoid logging credentials, and document provider data handling before production use.
- [Streaming or long-running work may be interrupted] → Persist action/task status independently from the stream and expose explicit cancellation and failure states.

## Migration Plan

1. Verify or complete the UI Wizard, Postgres/queue, orchestrator, Bootstrap Worker, and Free AI Pipeline prerequisites and agree on their API/task contracts.
2. Scaffold the .NET 10 Service, Api, Workers, and Test projects, plus the separate React/TypeScript Ui project; verify there are exactly five projects and that the UI has an independent build and deployment pipeline with no UI assets in Api.
3. Provision PostgreSQL, repository-scoped GitHub App permissions, provider secret encryption, model configuration, and workflow/task allowlists.
4. Create the Gherkin/xUnit acceptance contract for every spec scenario and architecture-test baseline before feature implementation. Develop Service use cases test-first using typed results and selective domain events, with PostgreSQL/queue integration against Testcontainers.
5. Wire authenticated Minimal API routes and thin worker consumers, then independently deploy the UI and exercise browser behavior through Playwright in the same xUnit project. Run Gemini/GitHub scenarios against isolated real sandboxes; report blocked/not-run tests honestly.
6. Enable issue, review-branch/PR, workflow, OpenSpec, and worker actions only after authorization, preview confirmation, audit records, scenario tests, and architecture checks pass.
7. Roll back by disabling the feature flag and action endpoints, draining or cancelling queued tasks where safe, and retaining completed action/audit records. Revoke installation/provider credentials if compromise is suspected; do not automatically revert already-created GitHub issues or pull requests.

## Open Questions

- Which deployment secret-management mechanism and key-rotation policy will provide the encryption key for persisted provider credentials?
- What retention period and access policy should apply to conversation prompts, generated previews, and action audit records?
- Which Gemini model IDs and request quotas are available to the target deployment when implementation begins?
