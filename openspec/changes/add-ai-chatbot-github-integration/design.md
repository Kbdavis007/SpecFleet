# Design

## Context

See proposal.md for motivation and specs/ for externally visible behavior. The repository currently has no application source, package manifests, API gateway, database schema, queue, worker, or deployment configuration. The requested React UI, Postgres queue, orchestrator, bootstrap worker, and AI pipeline therefore need to be integrated with or established by prerequisite work before deployment.

## Goals / Non-Goals

**Goals:**
- Keep the browser as a presentation and confirmation surface; all provider, GitHub, OpenSpec, and queue credentials and operations remain server-side.
- Separate conversational generation from actions that mutate GitHub or dispatch implementation work.
- Make generated outputs and every action traceable through previews, validation results, explicit approval, and durable status.
- Fit the planned foundation services without presuming they already exist in this checkout.

**Non-Goals:**
- Automatically merge pull requests or write generated files directly to the default branch.
- Execute arbitrary model-generated shell commands, scripts, or OpenSpec CLI arguments.
- Replace the prerequisite queue, orchestrator, bootstrap worker, or Free AI Pipeline.

## Decisions

### Use a server-mediated chat and action architecture

The React `SpecFleetChatBot` communicates with authenticated chat endpoints. Use a streaming HTTP response (SSE where supported by the API gateway) for assistant tokens and ordinary REST requests for generation, previews, confirmations, and status retrieval. The server owns conversation/task state and calls provider and orchestration adapters. This keeps provider and GitHub secrets out of client code and avoids requiring a bidirectional socket for one-way generated text. WebSockets remain an alternative only if the chosen gateway cannot reliably support streaming HTTP.

### Separate proposal and action lifecycles

Treat generation as a non-mutating phase that produces typed artifacts and validation results. A user then confirms the exact repository, paths, content, workflow, or task shown in the preview. Mutations go through distinct authorized endpoints and produce durable action records; edits after confirmation require a new confirmation. GitHub file changes go to a review branch and pull request, never straight to the default branch. Issue creation, PR creation, workflow dispatch, and worker dispatch are separately confirmable operations.

### Use least-privilege GitHub credentials and scoped operations

Use a GitHub App installation (preferred over a long-lived personal access token) for repository-scoped permissions. Mint short-lived installation tokens server-side for the selected repository and action. Restrict paths to the selected repository, validate path syntax and policy, use branch/contents/pull-request/issues/workflow permissions only as needed, and allow workflow dispatch only for configured workflow IDs. Handle conflicts and permission errors explicitly; do not retry non-idempotent writes as if they were safe.

### Use a provider adapter with deployment-configured Gemini models

Keep model invocation behind a server-side provider adapter. Configure a currently supported Gemini model identifier for text and code/artifact generation rather than hard-coding the legacy `gemini-pro` and `gemini-pro-code` names from the request. Verify current model availability, API shape, and free-tier limits during implementation. Store any provider API key encrypted at rest using an encryption key supplied separately by deployment secret management; never store the decryption key alongside ciphertext in Postgres or return credentials to the UI. If provider calls fail, surface the error rather than substituting an unverified response.

### Persist conversations and actions separately from secrets

Use Postgres for conversation metadata, messages, generated artifact previews, approvals, and task/action status, following the prerequisite queue/orchestrator data model where one exists. Keep provider secrets in the deployment's protected secret path or encrypted secret record, not ordinary conversation or task rows. Record actor, target repository/workspace, confirmed content identity, action type, timestamps, and outcome for auditability. Apply retention and access policy before storing sensitive prompts or generated code.

### Constrain OpenSpec and worker execution

Expose a fixed set of OpenSpec operations through the orchestrator: read/list, proposal preparation, validation, and explicitly confirmed change/apply actions. Invoke the CLI without a shell, from the selected workspace, with operation-specific argument validation, bounded time and output, and workspace path checks. Worker tasks use a typed allowlist for `generate-worker`, `generate-ui-page`, `generate-terraform-module`, `generate-cloudflare-tunnel-config`, `generate-rabbitmq-config`, and `generate-control-plane-logic`; validate payloads at both API and worker boundaries before dispatch. Generated source/configuration is data to review, never executable input to the orchestrator.

### Deliver the dashboard integration through the existing UI foundation

Add the requested React chat component to the dashboard supplied by the UI Wizard prerequisite. Present message history, streaming state, errors, syntax-highlighted previews, and separate controls for generating an OpenSpec slice, running an approved OpenSpec operation, and applying reviewed changes. If no usable React dashboard exists when implementation starts, establish or complete that prerequisite rather than embedding application scaffolding in the chat component.

## Risks / Trade-offs

- [The listed foundation proposals are not present in this repository] → Gate deployment on verifying the UI, Postgres/queue, orchestrator, bootstrap worker, and AI pipeline contracts; implement or sequence missing prerequisites before integrating the bot.
- [Gemini model names and free-tier quotas may change] → Resolve active model IDs from the supported provider API/configuration, make model selection deploy-time configurable, and expose quota/rate-limit errors.
- [A compromised account or deceptive generated preview could cause repository changes] → Bind approval to an immutable preview digest and target, enforce repository permissions server-side, isolate writes to review branches, and log outcomes.
- [OpenSpec CLI or worker input could escape intended scope] → Use allowlisted operations and task types, no shell evaluation, strict argument/schema validation, workspace containment checks, and bounded execution.
- [Prompts and generated artifacts can contain sensitive project data] → Restrict access, define retention, avoid logging credentials, and document provider data handling before enabling production use.
- [Streaming and long-running worker tasks can be interrupted] → Persist action/task status independently from the stream and provide status retrieval and explicit failure states.

## Migration Plan

1. Verify or complete the UI Wizard, Postgres/queue, orchestrator, bootstrap worker, and Free AI Pipeline prerequisites and agree their API/task contracts.
2. Provision repository-scoped GitHub App permissions, provider secret encryption, database schema, model configuration, and workflow/task allowlists.
3. Deploy server-side chat and validation APIs, then expose the React dashboard in a feature-flagged environment with read/generate functionality first.
4. Enable issue, review-branch/PR, workflow, and worker actions only after authorization, preview confirmation, audit records, and validation are verified.
5. Roll back by disabling the feature flag and action endpoints, draining or cancelling queued tasks where safe, and retaining completed action/audit records. Revoke installation/provider credentials if compromise is suspected; do not automatically revert already-created GitHub issues or pull requests.

## Open Questions

- Which deployment secret-management mechanism and key-rotation policy will provide the encryption key for persisted provider credentials?
- What retention period and access policy should apply to conversation prompts, generated previews, and action audit records?
- Which Gemini model IDs and request quotas are available to the target deployment when implementation begins?
