namespace IsoSwitch.IntegrationTests.Infrastructure;

/// <summary>
/// Single xunit collection so all integration test classes share one PostgreSQL server
/// (one container start per run) while each test still gets its own database.
/// </summary>
[CollectionDefinition(Name)]
public sealed class IntegrationTestCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}

/// <summary>
/// Trait values used to separate integration tests from the solution-wide unit test run.
/// </summary>
public static class Category
{
    public const string TraitName = "Category";
    public const string Integration = "Integration";
}
