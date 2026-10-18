using Reqnroll;

namespace SpecFleet.Test.Acceptance;

[Binding]
public sealed class AcceptanceInfrastructureSteps
{
    private bool _runnerConfigured;
    private bool _scenarioExecuted;

    [Given("the acceptance test runner is configured")]
    public void GivenTheAcceptanceTestRunnerIsConfigured()
    {
        _runnerConfigured = true;
    }

    [When("the scenario runs")]
    public void WhenTheScenarioRuns()
    {
        _scenarioExecuted = _runnerConfigured;
    }

    [Then("the scenario completes successfully")]
    public void ThenTheScenarioCompletesSuccessfully()
    {
        Assert.True(_scenarioExecuted);
    }
}
