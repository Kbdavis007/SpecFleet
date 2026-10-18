@acceptance @capability_openspec_worker_orchestration
Feature: OpenSpec and worker orchestration
  OpenSpec operations and worker tasks are constrained, authorized, validated,
  and observable.

  @spec_orchestration_proposal
  Scenario: User requests an OpenSpec proposal
    Given an authorized user requests an OpenSpec slice
    When proposal content is prepared
    Then it is presented for review under the standard change structure
    And no files are written before approval

  @spec_orchestration_inspection_existing_content
  Scenario: User reviews existing OpenSpec content
    Given an authorized user selects a project workspace
    When the user asks about existing OpenSpec capabilities or changes
    Then the system returns information read from that workspace
    And identifies when no relevant content exists

  @spec_orchestration_cli_read_only
  Scenario: User requests a read-only or validation operation
    Given an authorized user requests an allowlisted inspection or validation
    When the operation runs in the selected workspace
    Then the system displays its result

  @spec_orchestration_cli_state_changing
  Scenario: User requests a state-changing OpenSpec operation
    Given an authorized user requests an operation that modifies project files
    When the system previews the operation and affected change
    Then it runs only after the user explicitly confirms

  @spec_orchestration_cli_unsupported
  Scenario: User requests an unsupported command
    Given a request contains an unsupported command, arbitrary arguments, or executable
    When the system evaluates the request
    Then it rejects the request
    And executes no command

  @spec_orchestration_worker_supported
  Scenario: User confirms a supported worker task
    Given an authorized user confirms a valid supported worker task
    When the task is dispatched
    Then the system displays its task status

  @spec_orchestration_worker_unsupported_or_invalid
  Scenario: Task type or payload is unsupported
    Given a task type is unsupported or its payload fails validation
    When the user requests task dispatch
    Then the system reports the validation error
    And does not dispatch the task

  @spec_orchestration_supported_categories
  Scenario: User requests a supported worker task category
    Given a user requests worker generation, UI page, Terraform, Cloudflare Tunnel, RabbitMQ, or control-plane logic
    When the task inputs are validated
    Then the system creates a typed task preview with the category and inputs

  @spec_orchestration_unsupported_category
  Scenario: User requests an unconfigured worker category
    Given a user requests a worker category that is not configured
    When the request is evaluated
    Then the system identifies it as unsupported
    And does not dispatch a task

  @spec_orchestration_results_failure
  Scenario: An operation or task fails
    Given an OpenSpec operation or worker task has been dispatched
    When the operation or task returns a failure
    Then the system displays the failure and current task status

  @spec_orchestration_results_in_progress
  Scenario: A task remains in progress
    Given a worker task has been dispatched and has not completed
    When the user requests its status
    Then the system identifies the task as in progress
    And allows the user to retrieve updated status
