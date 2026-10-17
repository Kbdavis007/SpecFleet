# Spec Delta

## Purpose

Allow authorized users to turn reviewed assistant output into traceable GitHub repository actions without exposing credentials or granting the model unrestricted repository access.

## ADDED Requirements

### Requirement: GitHub actions enforce user and repository authorization
The system SHALL verify the user's authorization for the selected repository and requested action before making any GitHub API request that changes state.

#### Scenario: User is not authorized for the repository
- **WHEN** a user requests a write against a repository they cannot access
- **THEN** the system rejects the action and makes no GitHub mutation

#### Scenario: GitHub credentials are unavailable or insufficient
- **WHEN** credentials are missing, expired, or lack permission for the requested action
- **THEN** the system reports the authorization failure and does not report success

### Requirement: Users can create GitHub issues from reviewed content
The system SHALL allow an authorized user to create an issue in a selected repository only after the title and body have been presented for explicit confirmation.

#### Scenario: User confirms issue creation
- **WHEN** an authorized user confirms a reviewed issue title and body
- **THEN** the system creates the issue in the selected repository and returns its GitHub URL

#### Scenario: User edits or cancels issue content
- **WHEN** the user changes the issue content or cancels before confirmation
- **THEN** the system uses only the confirmed content or creates no issue, respectively

### Requirement: Repository file changes are isolated from the default branch
The system SHALL apply confirmed file additions and updates to a review branch and SHALL NOT write assistant-generated changes directly to the repository's default branch.

#### Scenario: User confirms new and updated files
- **WHEN** an authorized user approves a preview containing new or existing repository paths
- **THEN** the system writes the confirmed file contents on a review branch and returns the changed paths

#### Scenario: User confirms pull request creation
- **WHEN** a review branch contains the confirmed changes and the user approves opening a pull request
- **THEN** the system creates a pull request targeting the selected base branch and returns its URL

#### Scenario: Repository path is invalid or outside the workspace policy
- **WHEN** a proposed path is invalid or disallowed by repository policy
- **THEN** the system rejects that change and does not write the disallowed path

### Requirement: Workflow dispatch is allowlisted and confirmed
The system SHALL dispatch only configured GitHub Actions workflows and SHALL require explicit confirmation of the target repository, workflow, and inputs before dispatch.

#### Scenario: User confirms an allowed workflow
- **WHEN** an authorized user confirms an allowlisted workflow and valid inputs
- **THEN** the system dispatches that workflow and reports the dispatch result

#### Scenario: Workflow is not allowlisted
- **WHEN** a user or generated content requests an unconfigured workflow
- **THEN** the system rejects the request without dispatching a workflow

### Requirement: GitHub action outcomes are reported accurately
The system SHALL show the status and resulting GitHub URL or actionable error for each requested repository action.

#### Scenario: GitHub rejects a mutation
- **WHEN** a GitHub API request fails after user confirmation
- **THEN** the system displays the failure and does not present the action as completed
