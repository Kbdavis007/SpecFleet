@acceptance @capability_ai_chat
Feature: AI chat and artifact generation
  The assistant provides authenticated, reviewable AI conversations and generation
  without exposing provider credentials or performing unapproved mutations.

  @spec_ai_chat_users_can_converse_assistant_response_streams
  Scenario: Assistant response streams
    Given an authenticated user has submitted a valid prompt
    When the configured AI provider streams an answer
    Then the dashboard displays the answer incrementally
    And the response is marked complete when streaming ends

  @spec_ai_chat_users_can_converse_provider_request_fails
  Scenario: Provider request fails
    Given an authenticated user has submitted a valid prompt
    When the AI provider cannot complete the request
    Then the dashboard displays an actionable error
    And the partial response is not presented as complete

  @spec_ai_chat_users_can_converse_user_cancels_response
  Scenario: User cancels a response
    Given an assistant response is in progress
    When the user stops the response
    Then the system stops or abandons that response
    And the response is identified as cancelled

  @spec_ai_chat_generated_artifacts_supported_artifact
  Scenario: User requests a supported artifact
    Given an authenticated user requests a supported artifact type
    When the assistant generates the requested artifact
    Then the system returns a typed reviewable preview
    And the preview includes its proposed path or artifact category

  @spec_ai_chat_generated_artifacts_validation_fails
  Scenario: Generated content fails validation
    Given the assistant has generated artifact content
    When worker-side validation rejects that content
    Then the system reports the validation failures
    And the content is not offered as ready to apply

  @spec_ai_chat_generated_artifacts_code_preview
  Scenario: User reviews generated code
    Given generated content contains source code or configuration
    When the user opens its preview
    Then the dashboard renders it in a readable code block
    And syntax highlighting is used when the language is recognized

  @spec_ai_chat_credentials_browser_request
  Scenario: Browser requests an AI response
    Given a provider credential is configured on the server
    When the browser submits a prompt
    Then the server uses the configured credential
    And the credential is absent from the response and client-visible state

  @spec_ai_chat_credentials_unavailable
  Scenario: Provider credential is unavailable
    Given no usable provider credential is configured
    When an authenticated user requests AI generation
    Then the system reports that AI generation is unavailable
    And no unauthenticated provider request is attempted

  @spec_ai_chat_apply_confirmed
  Scenario: User confirms an artifact action
    Given a validated artifact preview and target are displayed
    When an authorized user confirms the displayed changes
    Then only the reviewed artifact and confirmed action are submitted

  @spec_ai_chat_apply_declined
  Scenario: User declines an artifact action
    Given a validated artifact preview is awaiting confirmation
    When the user declines or dismisses the confirmation
    Then no external repository or workflow is changed
