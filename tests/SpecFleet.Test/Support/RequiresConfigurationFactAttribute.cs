namespace SpecFleet.Test.Support;

/// <summary>
/// A <see cref="FactAttribute"/> that reports a credential-dependent test as skipped, never
/// as passing, when its required configuration is absent. Evaluation happens at discovery, so
/// a blocked clause is visible in the run summary as skipped rather than silently counted as
/// a pass.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequiresConfigurationFactAttribute : FactAttribute
{
    public RequiresConfigurationFactAttribute(string description, params string[] environmentVariables)
    {
        var missing = environmentVariables
            .Where(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))
            .ToArray();

        if (missing.Length > 0)
        {
            Skip = $"BLOCKED: {description} (missing {string.Join(", ", missing)}).";
        }
    }
}
