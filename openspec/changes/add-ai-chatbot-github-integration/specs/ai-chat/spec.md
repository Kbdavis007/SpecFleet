# Spec Delta

## Purpose

Provide an authenticated dashboard conversation for asking questions and generating reviewable project artifacts through a configured AI provider. Keep model credentials and generated output under server-side control.

## ADDED Requirements

### Requirement: Users can converse with the project assistant
The dashboard SHALL let an authenticated user submit prompts, review the current conversation history, and receive assistant responses incrementally.

#### Scenario: Assistant response streams
- **WHEN** a user submits a valid prompt and the AI provider returns a response
- **THEN** the dashboard displays the response as it arrives and marks the response complete when streaming ends

#### Scenario: Provider request fails
- **WHEN** the AI provider cannot complete a request
- **THEN** the dashboard displays an actionable error and does not present a partial response as a completed answer

#### Scenario: User cancels a response
- **WHEN** a user stops an in-progress response
- **THEN** the system stops or abandons that response and identifies it as cancelled rather than completed

### Requirement: Generated artifacts are reviewable before use
The assistant SHALL support generating code, documentation, worker definitions, Terraform modules, Cloudflare Tunnel configuration, RabbitMQ configuration, and OpenSpec proposal content, and SHALL present generated content for review before any external action.

#### Scenario: User requests a supported artifact
- **WHEN** a user requests one of the supported artifact types
- **THEN** the assistant returns a typed, reviewable artifact preview with its proposed path or artifact category

#### Scenario: Generated content fails validation
- **WHEN** generated content is malformed or fails the applicable worker-side validation
- **THEN** the system reports validation failures and does not offer the invalid content as ready to apply

#### Scenario: User reviews generated code
- **WHEN** generated content contains source code or configuration
- **THEN** the dashboard renders it in a readable code block with syntax highlighting when the language is recognized

### Requirement: AI credentials remain server-side
The system SHALL keep provider credentials inaccessible to browser clients and protect any persisted credential using encrypted-at-rest secret storage.

#### Scenario: Browser requests an AI response
- **WHEN** the browser submits a prompt
- **THEN** the server uses the configured provider credential without returning that credential in the response or client-visible state

#### Scenario: Provider credential is unavailable
- **WHEN** no usable credential is configured
- **THEN** the system reports that AI generation is unavailable and does not attempt an unauthenticated provider request

### Requirement: Users can apply a reviewed artifact through an explicit action
The dashboard SHALL provide an apply action only for a validated artifact preview and SHALL obtain explicit user confirmation before dispatching that artifact to an external repository or state-changing workflow.

#### Scenario: User confirms an artifact action
- **WHEN** an authorized user confirms the displayed target and changes
- **THEN** the system submits only the reviewed artifact and confirmed action to the corresponding authorized workflow

#### Scenario: User declines an artifact action
- **WHEN** the user declines or dismisses the confirmation
- **THEN** the system leaves external repositories and workflows unchanged
