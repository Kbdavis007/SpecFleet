using Xunit;

// Every test in this assembly shares one real PostgreSQL container (SharedPostgreSql) and
// truncates the tables it uses. The Reqnroll-generated features already disable
// parallelization among themselves, but a separate non-parallel collection still runs
// concurrently with them, which lets one test's reset delete another test's in-flight rows.
// Serializing the whole assembly keeps the shared database consistent.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
