# Spec Delta

## Purpose

Let authorized users inspect and prepare OpenSpec changes, validate them, and dispatch supported worker tasks through a constrained and observable orchestration flow.

## ADDED Requirements

### Requirement: Users can inspect and prepare OpenSpec changes
The system SHALL let an authorized user review available OpenSpec context and prepare a proposal using the repository's configured OpenSpec change workflow.

#### Scenario: User requests an OpenSpec proposal
- **WHEN** a user asks to create an OpenSpec slice
- **THEN** the system presents proposal content under the standard change structure for review before writing it

#### Scenario: User reviews existing OpenSpec content
- **WHEN** a user asks about existing OpenSpec capabilities or changes
- **THEN** the system returns information read from the selected project workspace and identifies when no relevant content exists

### Requirement: OpenSpec CLI operations are constrained and approved
The system SHALL run only supported OpenSpec operations in the authorized project workspace and SHALL require explicit user approval before an operation can modify project files or start implementation work.

#### Scenario: User requests a read-only or validation operation
- **WHEN** an authorized user requests an allowlisted inspection or validation operation
- **THEN** the system runs it within the selected project workspace and displays its result

#### Scenario: User requests a state-changing OpenSpec operation
- **WHEN** an authorized user requests an operation that creates, edits, or applies a change
- **THEN** the system previews the operation and affected change, then runs it only after explicit confirmation

#### Scenario: User requests an unsupported command
- **WHEN** a request contains an unsupported CLI command, arbitrary arguments, or an executable outside the allowlist
- **THEN** the system rejects the request and executes no command

### Requirement: Worker tasks use a supported task contract
The system SHALL accept only validated task payloads for supported worker types and SHALL retain enough task status to report dispatch and completion outcomes.

#### Scenario: User confirms a supported worker task
- **WHEN** an authorized user confirms a valid task for a supported worker
- **THEN** the system dispatches the validated payload and displays its task status

#### Scenario: Task type or payload is unsupported
- **WHEN** a task type is not supported or its payload fails validation
- **THEN** the system reports the validation error and does not dispatch the task

### Requirement: The assistant supports the configured SpecFleet worker task types
The system SHALL support validated requests for worker generation, UI page generation, Terraform module generation, Cloudflare Tunnel configuration, RabbitMQ configuration, and control-plane logic.

#### Scenario: User requests a supported worker task category
- **WHEN** a user selects or requests one of the supported worker task categories
- **THEN** the system creates a typed task preview with the category and validated inputs

#### Scenario: User requests an unconfigured worker category
- **WHEN** a user requests a worker category that is not configured
- **THEN** the system identifies it as unsupported and does not dispatch a task

### Requirement: Orchestration results are observable
The system SHALL report the result of each OpenSpec operation and worker task, including failures, without representing an incomplete operation as successful.

#### Scenario: An operation or task fails
- **WHEN** an OpenSpec operation or worker task returns a failure
- **THEN** the system displays the failure and current task status to the user

#### Scenario: A task remains in progress
- **WHEN** a dispatched task has not completed
- **THEN** the system identifies it as in progress and allows the user to retrieve its updated status
