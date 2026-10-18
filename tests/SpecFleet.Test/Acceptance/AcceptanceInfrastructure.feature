Feature: Acceptance test infrastructure

  Scenario: Gherkin scenarios execute with xUnit
    Given the acceptance test runner is configured
    When the scenario runs
    Then the scenario completes successfully
