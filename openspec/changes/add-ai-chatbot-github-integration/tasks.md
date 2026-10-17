# Tasks

## 1. Establish the five-project architecture and runtime foundation

- [ ] 1.1 Verify that the UI Wizard, Postgres Queue, Orchestrator, Bootstrap Worker, and Free AI Pipeline prerequisites are implemented and available; exercise their health checks and record API/task contracts, blocking chatbot integration if a prerequisite is missing.
- [ ] 1.2 Scaffold the .NET 10/C# `SpecFleet.Service`, `SpecFleet.Api`, `SpecFleet.Workers`, and single xUnit `SpecFleet.Test` projects plus a separate `SpecFleet.Ui` React/TypeScript project; verify the repository contains exactly five projects and all backend projects build.
- [ ] 1.3 Establish self-contained feature/use-case slices and DDD boundaries in Service, Minimal API wiring folders in Api, thin consumer wiring in Workers, and the single xUnit project in Test; verify Api/Workers reference Service for application behavior, Test references projects under test, and document the folder/naming conventions.
- [ ] 1.4 Create the standalone UI project's source and build configuration using the latest stable compatible React/TypeScript versions and a lockfile; verify it builds independently, is not nested in Api, and its assets are not bundled or served by the API.
- [ ] 1.5 Configure independent UI hosting/deployment and authenticated communication with the API, including explicit trusted origins and CORS; verify a staging deployment can load the UI from its own location and successfully use authenticated HTTP/streaming API endpoints.
- [ ] 1.6 Configure PostgreSQL persistence and schema/migrations within Service following the prerequisite queue contract; verify migrations and persistence through xUnit integration tests in the single Test project and document database configuration.
- [ ] 1.7 Confirm the deployment secret manager, encryption/key-rotation mechanism, conversation retention policy, supported Gemini model IDs, and free-tier quotas; verify startup and secret handling through xUnit tests and document the selected settings.

## 2. Implement chat and artifact generation in Service

- [ ] 2.1 Implement authenticated conversation storage and server-side provider credential access/encryption in Service; verify authorization, persistence, missing-key errors, key handling, and absence of secrets from client responses/logs with xUnit tests in Test.
- [ ] 2.2 Implement the Gemini provider adapter and streaming chat use case in Service using deployment-configured supported model IDs; verify streaming completion, cancellation, provider failures, and quota failures with provider contract tests in Test and document model configuration.
- [ ] 2.3 Implement typed artifact generation and validation for code, documentation, workers, Terraform, Cloudflare Tunnel, RabbitMQ, and OpenSpec proposal content in Service; verify valid previews and invalid/unsupported output rejection with xUnit tests in Test and document supported artifact types.
- [ ] 2.4 Add self-contained Minimal API endpoint wiring for chat, generation, preview, approval, and status operations; verify HTTP behavior, authorization, streaming, and Service delegation with API tests in Test, with no business logic or persistence access in Api.
- [ ] 2.5 Implement the React/TypeScript chat UI, previews, syntax highlighting, cancellation/errors, and separate apply/OpenSpec controls in the Ui project; verify chat and approval flows through browser-driven xUnit tests in Test and document how to use and deploy the independently hosted dashboard.

## 3. Implement approved GitHub actions in Service

- [ ] 3.1 Add least-privilege GitHub App integration and repository/action authorization in Service; verify denied repositories and insufficient permissions cause no mutation with xUnit tests in Test and document installation permissions.
- [ ] 3.2 Implement confirmed issue creation and review-branch file additions/updates in Service, including path validation and immutable-preview approval; verify issue contents, branch changes, conflicts, and rejected paths using mocked GitHub API tests in Test.
- [ ] 3.3 Implement separately confirmed pull requests and allowlisted workflow dispatch in Service; verify base-branch selection, workflow/input allowlists, GitHub failures, and returned status/URLs with xUnit tests in Test and document workflow configuration.
- [ ] 3.4 Add the corresponding Minimal API route wiring for GitHub actions; verify authentication, request/result mapping, and failure reporting through API tests in Test without adding GitHub logic to Api.

## 4. Implement OpenSpec and worker orchestration in Service

- [ ] 4.1 Implement OpenSpec inspection, proposal preparation, validation, and approved state-changing operations in Service using allowlisted CLI invocations without a shell; verify workspace containment, bounded execution, approval gates, and unsupported-command rejection with xUnit tests in Test.
- [ ] 4.2 Implement typed task contracts, payload validation, durable status, and dispatch for the six supported worker categories in Service; verify accepted/rejected payloads and status transitions with queue contract tests in Test and document the task contract.
- [ ] 4.3 Wire Workers consumers to the Service task use cases without business logic; verify consumer dispatch, acknowledgement/retry behavior, and failure reporting with worker integration tests in Test.
- [ ] 4.4 Add self-contained Minimal API route wiring for OpenSpec and task creation/status; verify supported operations, confirmations, and Service delegation with API tests in Test without adding orchestration logic to Api.

## 5. Verify rollout and operations

- [ ] 5.1 Run an end-to-end xUnit flow in Test from streamed prompt through validated preview, explicit approval, GitHub or worker action, and final status; verify cancellation and provider/queue/GitHub failures do not create unintended mutations.
- [ ] 5.2 Document the five-project architecture, independent UI build/hosting/deployment, API origin/CORS configuration, PostgreSQL setup, secret provisioning/rotation, GitHub App, feature-flag rollout, monitoring, and rollback; verify documented commands and independent deployment procedures in staging before enabling state-changing actions in production.
