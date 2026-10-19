# Acceptance Scenario Traceability

These Gherkin scenarios are behavior contracts derived from the OpenSpec capability specifications. Update a scenario only when its corresponding specification behavior is approved to change; do not change the contract to fit an implementation detail.

| Spec requirement | Spec scenario | Acceptance scenario | Tag |
|---|---|---|---|
| `ai-chat` / Users can converse with the project assistant | Assistant response streams | `Features/AiChat.feature` / Assistant response streams | `@spec_ai_chat_users_can_converse_assistant_response_streams` |
| `ai-chat` / Users can converse with the project assistant | Provider request fails | `Features/AiChat.feature` / Provider request fails | `@spec_ai_chat_users_can_converse_provider_request_fails` |
| `ai-chat` / Users can converse with the project assistant | User cancels a response | `Features/AiChat.feature` / User cancels a response | `@spec_ai_chat_users_can_converse_user_cancels_response` |
| `ai-chat` / Generated artifacts are reviewable before use | User requests a supported artifact | `Features/AiChat.feature` / User requests a supported artifact | `@spec_ai_chat_generated_artifacts_supported_artifact` |
| `ai-chat` / Generated artifacts are reviewable before use | Generated content fails validation | `Features/AiChat.feature` / Generated content fails validation | `@spec_ai_chat_generated_artifacts_validation_fails` |
| `ai-chat` / Generated artifacts are reviewable before use | User reviews generated code | `Features/AiChat.feature` / User reviews generated code | `@spec_ai_chat_generated_artifacts_code_preview` |
| `ai-chat` / AI credentials remain server-side | Browser requests an AI response | `Features/AiChat.feature` / Browser requests an AI response | `@spec_ai_chat_credentials_browser_request` |
| `ai-chat` / AI credentials remain server-side | Provider credential is unavailable | `Features/AiChat.feature` / Provider credential is unavailable | `@spec_ai_chat_credentials_unavailable` |
| `ai-chat` / Users can apply a reviewed artifact through an explicit action | User confirms an artifact action | `Features/AiChat.feature` / User confirms an artifact action | `@spec_ai_chat_apply_confirmed` |
| `ai-chat` / Users can apply a reviewed artifact through an explicit action | User declines an artifact action | `Features/AiChat.feature` / User declines an artifact action | `@spec_ai_chat_apply_declined` |
| `github-repository-actions` / GitHub actions enforce user and repository authorization | User is not authorized for the repository | `Features/GitHubRepositoryActions.feature` / User is not authorized for the repository | `@spec_github_authorization_user_not_authorized` |
| `github-repository-actions` / GitHub actions enforce user and repository authorization | GitHub credentials are unavailable or insufficient | `Features/GitHubRepositoryActions.feature` / GitHub credentials are unavailable or insufficient | `@spec_github_authorization_credentials_unavailable_or_insufficient` |
| `github-repository-actions` / Users can create GitHub issues from reviewed content | User confirms issue creation | `Features/GitHubRepositoryActions.feature` / User confirms issue creation | `@spec_github_issue_user_confirms` |
| `github-repository-actions` / Users can create GitHub issues from reviewed content | User edits or cancels issue content | `Features/GitHubRepositoryActions.feature` / User edits issue content before confirmation; User cancels issue creation | `@spec_github_issue_user_edits_or_cancels` |
| `github-repository-actions` / Repository file changes are isolated from the default branch | User confirms new and updated files | `Features/GitHubRepositoryActions.feature` / User confirms new and updated files | `@spec_github_file_changes_confirmed` |
| `github-repository-actions` / Repository file changes are isolated from the default branch | User confirms pull request creation | `Features/GitHubRepositoryActions.feature` / User confirms pull request creation | `@spec_github_file_changes_pull_request` |
| `github-repository-actions` / Repository file changes are isolated from the default branch | Repository path is invalid or outside the workspace policy | `Features/GitHubRepositoryActions.feature` / Repository path is invalid or outside policy | `@spec_github_file_changes_invalid_path` |
| `github-repository-actions` / Workflow dispatch is allowlisted and confirmed | User confirms an allowed workflow | `Features/GitHubRepositoryActions.feature` / User confirms an allowed workflow | `@spec_github_workflow_allowed` |
| `github-repository-actions` / Workflow dispatch is allowlisted and confirmed | Workflow is not allowlisted | `Features/GitHubRepositoryActions.feature` / Workflow is not allowlisted | `@spec_github_workflow_unallowlisted` |
| `github-repository-actions` / GitHub action outcomes are reported accurately | GitHub rejects a mutation | `Features/GitHubRepositoryActions.feature` / GitHub rejects a mutation | `@spec_github_outcomes_mutation_rejected` |
| `openspec-worker-orchestration` / Users can inspect and prepare OpenSpec changes | User requests an OpenSpec proposal | `Features/OpenSpecWorkerOrchestration.feature` / User requests an OpenSpec proposal | `@spec_orchestration_proposal` |
| `openspec-worker-orchestration` / Users can inspect and prepare OpenSpec changes | User reviews existing OpenSpec content | `Features/OpenSpecWorkerOrchestration.feature` / User reviews existing OpenSpec content | `@spec_orchestration_inspection_existing_content` |
| `openspec-worker-orchestration` / OpenSpec CLI operations are constrained and approved | User requests a read-only or validation operation | `Features/OpenSpecWorkerOrchestration.feature` / User requests a read-only or validation operation | `@spec_orchestration_cli_read_only` |
| `openspec-worker-orchestration` / OpenSpec CLI operations are constrained and approved | User requests a state-changing OpenSpec operation | `Features/OpenSpecWorkerOrchestration.feature` / User requests a state-changing OpenSpec operation | `@spec_orchestration_cli_state_changing` |
| `openspec-worker-orchestration` / OpenSpec CLI operations are constrained and approved | User requests an unsupported command | `Features/OpenSpecWorkerOrchestration.feature` / User requests an unsupported command | `@spec_orchestration_cli_unsupported` |
| `openspec-worker-orchestration` / Worker tasks use a supported task contract | User confirms a supported worker task | `Features/OpenSpecWorkerOrchestration.feature` / User confirms a supported worker task | `@spec_orchestration_worker_supported` |
| `openspec-worker-orchestration` / Worker tasks use a supported task contract | Task type or payload is unsupported | `Features/OpenSpecWorkerOrchestration.feature` / Task type or payload is unsupported | `@spec_orchestration_worker_unsupported_or_invalid` |
| `openspec-worker-orchestration` / The assistant supports the configured SpecFleet worker task types | User requests a supported worker task category | `Features/OpenSpecWorkerOrchestration.feature` / User requests a supported worker task category | `@spec_orchestration_supported_categories` |
| `openspec-worker-orchestration` / The assistant supports the configured SpecFleet worker task types | User requests an unconfigured worker category | `Features/OpenSpecWorkerOrchestration.feature` / User requests an unconfigured worker category | `@spec_orchestration_unsupported_category` |
| `openspec-worker-orchestration` / Orchestration results are observable | An operation or task fails | `Features/OpenSpecWorkerOrchestration.feature` / An operation or task fails | `@spec_orchestration_results_failure` |
| `openspec-worker-orchestration` / Orchestration results are observable | A task remains in progress | `Features/OpenSpecWorkerOrchestration.feature` / A task remains in progress | `@spec_orchestration_results_in_progress` |

## Implementation status

Status is reported per scenario; a scenario is only `passing` when its acceptance test executes and succeeds. `pending` scenarios have no step bindings yet. `blocked` scenarios have an implemented behavior but cannot be verified in this environment.

| Scenario | Status | Evidence |
|---|---|---|
| Assistant response streams | passing | `Features/Chat/AiChatSteps.cs`; real `ChatAskHandler` + real PostgreSQL + real Gemini-protocol server |
| Provider request fails | passing | same |
| User cancels a response | passing | same |
| User requests a supported artifact | passing | same; `ArtifactCatalog`/`ArtifactValidator` |
| Generated content fails validation | passing | same |
| User reviews generated code | passing | same |
| Browser requests an AI response | passing | same; credential never leaves the server |
| Provider credential is unavailable | passing | same; `UnconfiguredChatCompletionProvider` |
| User confirms an artifact action | passing | same; `ArtifactActionHandler` + real action sink |
| User declines an artifact action | passing | same |
| ai-chat with the hosted Gemini provider (task 3.2 verification clause) | blocked | no `GEMINI_API_KEY` in this environment; reported blocked, not passing |
| AI harness provider-generated case (task 3.3) | blocked | no `GEMINI_API_KEY`; harness structural/boundary/compilation checks pass |
| GitHub authorization (both scenarios) | passing | `Features/GitHub/GitHubActionSteps.cs`; real `GitHubActionHandler` + real `HttpGitHubClient` over real HTTP |
| GitHub issue creation (confirm, edit, cancel) | passing | same; confirmation bound to the reviewed preview digest |
| GitHub file changes (apply, pull request, invalid path) | passing | same; `GitHubRepositoryPolicy` path rules |
| GitHub workflow dispatch (allowed, unallowlisted) | passing | same; workflow allowlist enforced before dispatch |
| GitHub mutation rejected by the API | passing | same; failure reported, never success-shaped |
| GitHub actions against a live sandbox repository (tasks 4.1–4.3 verification clause) | blocked | no least-privilege GitHub App credential for an isolated sandbox repo; reported blocked, not passing |
| All `openspec-worker-orchestration` scenarios | pending | section 5 not implemented |
