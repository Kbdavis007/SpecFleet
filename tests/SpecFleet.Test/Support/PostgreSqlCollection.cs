namespace SpecFleet.Test.Support;

/// <summary>
/// Serializes every test class that shares the single real PostgreSQL container. The container
/// is process-wide, and each test truncates the Service-owned tables it uses, so classes must
/// not run concurrently or one test's reset would delete another test's in-flight rows.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgreSqlCollection
{
    public const string Name = "SpecFleet.PostgreSql";
}
