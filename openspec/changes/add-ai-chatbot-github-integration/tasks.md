# Tasks

## 1. Establish project and test foundations before feature implementation

- [ ] 1.1 Verify that the UI Wizard, Postgres Queue, Orchestrator, Bootstrap Worker, and Free AI Pipeline prerequisites are available; exercise their health checks and record contracts, blocking dependent implementation if any are missing.
- [ ] 1.2 Scaffold the .NET 10/C# Service, Api, Workers, and Test projects plus the separately built React/TypeScript Ui project; verify the repository contains exactly five projects and backend builds succeed.
- [ ] 1.3 Configure the single Test project with xUnit, Gherkin scenario execution, Testcontainers, and Playwright .NET; verify one test project can discover and execute a sample Gherkin scenario and browser test without adding another test project.
- [ ] 1.4 Before implementing any feature behavior, translate every scenario in all three capability specs into stable, implementation-independent Gherkin acceptance scenarios in Test and add a traceability map from each spec scenario to its test; verify the scenarios are discovered by xUnit and fail or remain explicitly pending for the not-yet-implemented behavior.
- [ ] 1.5 Add executable architecture tests for exactly five projects, allowed project references, Service ownership of application behavior, thin Api/Workers, independent UI placement, and vertical-slice boundaries; verify the rules pass for the intended skeleton and demonstrably fail when a deliberate temporary violation is introduced, then remove that violation.
- [ ] 1.6 Configure Testcontainers for real PostgreSQL and the selected real queue/broker; verify migrations, queue connectivity, isolation, and cleanup from the xUnit project without mocks or fakes.
- [ ] 1.7 Add an AI-generated-code harness in Test that runs real provider-generated artifacts through structural checks, formatting/static analysis, compilation, and applicable tests; verify it rejects an artifact violating a required constraint and accepts a conforming generated artifact without asserting that passing checks proves defect-free code.

## 2. Establish Service result and domain-event conventions

- [ ] 2.1 Define typed Service response/result contracts for success and expected validation, authorization, conflict, provider, and queue outcomes; write failing xUnit tests first and verify expected failures propagate through Api/Workers as explicit outcomes without exception-based control flow.
- [ ] 2.2 Define when meaningful domain state transitions publish domain events and how durable reactions use the agreed queue/outbox contract; write failing tests first and verify persisted state and event delivery/retry behavior against Testcontainers-backed infrastructure.
- [ ] 2.3 Add host-boundary handling for unexpected exceptions with explicit logs and error responses/statuses; verify unexpected failures are not swallowed or converted into success-shaped results using end-to-end tests with real configured components.

## 3. Implement chat and artifact generation test-first

- [ ] 3.1 For each chat and artifact-generation Gherkin scenario, verify the acceptance test fails before implementation, then implement authenticated conversation persistence and typed Service use cases; rerun the scenario tests against Testcontainers PostgreSQL and document response/result contracts.
- [ ] 3.2 Implement the Gemini adapter and streaming use case after adding failing tests for completion, cancellation, missing credentials, and provider errors; verify with real configured Gemini credentials and provider contract tests, reporting credential-blocked tests as not run rather than passing.
- [ ] 3.3 Implement typed artifact generation and validation for code, documentation, workers, Terraform, Cloudflare Tunnel, RabbitMQ, and OpenSpec proposal content; verify acceptance scenarios and AI harness structural/build/test checks against real generated output.
- [ ] 3.4 Add Minimal API route wiring that delegates to Service result contracts; verify HTTP/stream mappings and authorization through the running API with real Testcontainers dependencies and no mocked/faked Service or persistence components.

## 4. Implement GitHub actions test-first

- [ ] 4.1 For each GitHub authorization, issue, file-change, pull-request, workflow, and failure scenario in the specs, verify its Gherkin acceptance test fails before implementing the Service use case; then verify it passes against an isolated real GitHub sandbox repository and least-privilege App credentials.
- [ ] 4.2 Implement immutable-preview confirmation, safe path handling, review-branch file changes, issue creation, pull requests, and allowlisted workflow dispatch; verify actual sandbox side effects, denied access, conflicts, cleanup, and accurate response results without mocks/fakes.
- [ ] 4.3 Add Minimal API wiring for GitHub actions and verify end-to-end request, approval, authorization, and result behavior against the running API and GitHub sandbox; block release verification if required sandbox credentials are unavailable.

## 5. Implement OpenSpec and worker orchestration test-first

- [ ] 5.1 For every OpenSpec inspection, proposal, validation, approval, and rejection scenario, verify the Gherkin acceptance test fails before implementation; then implement allowlisted Service operations and verify workspace containment, command bounds, and expected response objects in integration tests.
- [ ] 5.2 For each supported worker task category and task outcome scenario, verify a failing acceptance test before implementing typed payload validation, dispatch, and status; verify real queue delivery, persistence, retry, and status behavior with Testcontainers.
- [ ] 5.3 Wire Workers as thin Service consumers and Api endpoints as Minimal API transport only; verify worker acknowledgement/failure behavior and API results through running components and real queue/database infrastructure.

## 6. Implement and verify the independently deployed UI

- [ ] 6.1 For every UI acceptance scenario, verify the corresponding Playwright test fails before implementing the React/TypeScript view; then build message history, streaming/cancellation, errors, code previews, approval, OpenSpec, and worker controls in the standalone Ui project.
- [ ] 6.2 Run Playwright through xUnit against the independently hosted UI and running API with real configured PostgreSQL/queue dependencies; verify authenticated streaming, confirmed actions, cancellation, and errors, and verify Api neither bundles nor serves UI assets.

## 7. Close acceptance coverage and production-readiness checks

- [ ] 7.1 Run the complete Gherkin/xUnit acceptance suite and verify every scenario in each capability spec has a passing test or an explicit blocked status; do not waive or relabel missing/failed sandbox scenarios as passing.
- [ ] 7.2 Run architecture tests, Testcontainers integration tests, Playwright tests, AI harness checks, formatting/static analysis, and all applicable build/tests in CI; verify release gates report failures and skipped/blocked checks explicitly without relying on coverage-percentage targets.
- [ ] 7.3 Document the five-project architecture, stable acceptance contract change policy, red-green-refactor workflow, response/result and domain-event conventions, real-container/sandbox setup, Playwright execution, AI harness checks, deployment smoke tests, monitoring, and rollback; verify documented commands work in a clean staging environment.
