using Npgsql;
using Testcontainers.PostgreSql;

namespace SpecFleet.Test.Infrastructure;

public sealed class PostgreSqlContainerTests
{
    [Fact]
    public async Task Containers_start_connect_and_keep_databases_isolated()
    {
        await using var first = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("specfleet_test")
            .WithUsername("postgres")
            .WithPassword("specfleet_test")
            .Build();
        await using var second = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("specfleet_test")
            .WithUsername("postgres")
            .WithPassword("specfleet_test")
            .Build();

        await Task.WhenAll(first.StartAsync(), second.StartAsync());

        await using var firstConnection = new NpgsqlConnection(first.GetConnectionString());
        await firstConnection.OpenAsync();
        await using (var createTable = new NpgsqlCommand(
            "CREATE TABLE isolation_probe (id integer PRIMARY KEY);",
            firstConnection))
        {
            await createTable.ExecuteNonQueryAsync();
        }

        await using var secondConnection = new NpgsqlConnection(second.GetConnectionString());
        await secondConnection.OpenAsync();
        await using var checkTable = new NpgsqlCommand(
            "SELECT to_regclass('public.isolation_probe') IS NOT NULL;",
            secondConnection);

        var tableExistsInSecondContainer = (bool)(await checkTable.ExecuteScalarAsync())!;

        Assert.False(tableExistsInSecondContainer);
    }
}
