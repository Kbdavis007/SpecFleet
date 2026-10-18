@acceptance @capability_github_repository_actions
Feature: Authorized GitHub repository actions
  GitHub mutations are authorized, scoped, explicitly confirmed, and reported.

  @spec_github_authorization_user_not_authorized
  Scenario: User is not authorized for the repository
    Given a user cannot access the selected repository
    When the user requests a repository write
    Then the system rejects the action
    And no GitHub mutation occurs

  @spec_github_authorization_credentials_unavailable_or_insufficient
  Scenario: GitHub credentials are unavailable or insufficient
    Given GitHub credentials are missing, expired, or lack required permission
    When the user requests the corresponding GitHub action
    Then the system reports the authorization failure
    And the action is not reported as successful

  @spec_github_issue_user_confirms
  Scenario: User confirms issue creation
    Given an authorized user is shown a reviewed issue title and body
    When the user confirms issue creation
    Then the issue is created in the selected repository
    And its GitHub URL is returned

  @spec_github_issue_user_edits_or_cancels @case_user_edits
  Scenario: User edits issue content before confirmation
    Given an authorized user is shown a reviewed issue title and body
    When the user edits the issue content and confirms
    Then the issue is created using only the confirmed content

  @spec_github_issue_user_edits_or_cancels @case_user_cancels
  Scenario: User cancels issue creation
    Given an authorized user is shown a reviewed issue title and body
    When the user cancels before confirmation
    Then no issue is created

  @spec_github_file_changes_confirmed
  Scenario: User confirms new and updated files
    Given an authorized user is shown a file preview for a review branch
    When the user confirms the file additions and updates
    Then the confirmed contents are written on a review branch
    And the changed paths are returned

  @spec_github_file_changes_pull_request
  Scenario: User confirms pull request creation
    Given a review branch contains the confirmed changes
    When the user approves opening a pull request
    Then a pull request is created against the selected base branch
    And its GitHub URL is returned

  @spec_github_file_changes_invalid_path
  Scenario: Repository path is invalid or outside policy
    Given a proposed repository path is invalid or disallowed
    When the user attempts to apply the file change
    Then the system rejects that change
    And the disallowed path is not written

  @spec_github_workflow_allowed
  Scenario: User confirms an allowed workflow
    Given an authorized user is shown an allowlisted workflow and valid inputs
    When the user confirms the workflow dispatch
    Then the system dispatches that workflow
    And reports the dispatch result

  @spec_github_workflow_unallowlisted
  Scenario: Workflow is not allowlisted
    Given a request names an unconfigured workflow
    When the user or generated content requests its dispatch
    Then the system rejects the request
    And no workflow is dispatched

  @spec_github_outcomes_mutation_rejected
  Scenario: GitHub rejects a mutation
    Given an authorized user has confirmed a GitHub mutation
    When the GitHub API rejects the request
    Then the system displays the failure
    And the action is not presented as completed
