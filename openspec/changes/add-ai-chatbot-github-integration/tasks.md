# Tasks

## 1. Verify platform prerequisites and contracts

- [ ] 1.1 Verify that the UI Wizard, Postgres Queue, Orchestrator, Bootstrap Worker, and Free AI Pipeline prerequisites are implemented and available; exercise their documented health checks and record the API/task contracts, blocking chatbot integration if any are missing.
- [ ] 1.2 Confirm the deployment secret manager, encryption/key-rotation mechanism, conversation retention policy, supported Gemini model IDs, and free-tier quotas; document the selected settings and verify the service starts with those settings without exposing secrets.

## 2. Establish secured API and persistence foundations

- [ ] 2.1 Add authenticated authorization checks and request limits for all chat/action routes; verify unauthorized users and over-limit requests are rejected by API tests and document the permission model.
- [ ] 2.2 Add durable Postgres records for conversations, messages, artifact previews, approvals, GitHub actions, and worker task status using the prerequisite queue/orchestrator contract; verify migrations, access boundaries, and persistence with database tests and document retention behavior.
- [ ] 2.3 Add server-side provider credential loading and encrypted-at-rest persistence where required; verify encryption/decryption, key rotation behavior, missing-credential errors, and absence of secrets from client responses and logs with security-focused tests and configuration documentation.

## 3. Implement Gemini chat and artifact generation

- [ ] 3.1 Implement the server-side Gemini adapter and `POST /api/v1/chat/ask` streaming response using deployment-configured supported model IDs; verify streaming completion, cancellation, provider errors, and quota failures with provider contract tests and document model configuration.
- [ ] 3.2 Implement typed generation and worker-side validation for code, documentation, worker definitions, Terraform modules, Cloudflare Tunnel configuration, RabbitMQ configuration, and OpenSpec proposal content; verify valid previews and malformed/unsupported output rejection with unit tests and document supported artifact types.

## 4. Integrate the React dashboard chat

- [ ] 4.1 Add `SpecFleetChatBot.tsx` to the prerequisite dashboard with message history, incremental responses, cancellation, and actionable error states; verify rendering and stream lifecycle behavior with UI tests and document how to open the assistant.
- [ ] 4.2 Add syntax-highlighted artifact previews and distinct “Apply to GitHub”, “Generate OpenSpec Slice”, and “Run OpenSpec CLI” controls; verify each control binds confirmation to the displayed immutable preview and target using UI tests.

## 5. Implement authorized GitHub actions

- [ ] 5.1 Configure a least-privilege GitHub App integration and repository-level authorization for the issue, contents, pull request, and allowlisted workflow operations; verify denied repositories and insufficient permissions produce no mutations with API tests and document required installation permissions.
- [ ] 5.2 Implement `POST /api/v1/chat/github/issue` and reviewed file additions/updates on isolated branches, including safe path validation and commits; verify confirmed issue creation, branch contents, and rejection of invalid paths using mocked GitHub API tests.
- [ ] 5.3 Implement `POST /api/v1/chat/github/pr` and allowlisted workflow dispatches with separate confirmation and durable outcomes; verify pull-request targets, workflow/input allowlists, GitHub failures, and returned URLs with integration tests and document workflow configuration.

## 6. Implement constrained OpenSpec and worker orchestration

- [ ] 6.1 Implement `POST /api/v1/chat/spec/proposal` and OpenSpec inspection/validation through the configured CLI, generating changes in the repository's standard `openspec/changes` structure; verify generated proposals against the configured schema and verify workspace containment and unsupported-command rejection in CLI integration tests.
- [ ] 6.2 Implement `POST /api/v1/chat/openspec/run` for allowlisted CLI operations, requiring confirmation for file-changing or apply operations; verify no shell execution, argument allowlisting, execution bounds, and approval enforcement with API/CLI tests and document supported commands.
- [ ] 6.3 Implement `POST /api/v1/chat/tasks/create` for the six supported worker categories with API- and worker-side payload validation and durable status lookup; verify valid dispatch, invalid payload rejection, task progress, and failure reporting with queue/worker contract tests and document the task payload contract.

## 7. Verify integrated rollout and operations

- [ ] 7.1 Run an end-to-end flow from streamed prompt through validated preview, explicit approval, GitHub or worker action, and final status; verify cancellation, provider/queue/GitHub failures, and non-authorized attempts leave no unintended mutations.
- [ ] 7.2 Document deployment configuration, secret provisioning/rotation, GitHub App setup, feature-flag rollout, monitoring, and rollback; verify the documented rollout and disable/rollback procedures in a staging environment before enabling state-changing actions in production.
