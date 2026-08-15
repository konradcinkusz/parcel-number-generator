using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using ParcelNumberGenerator.Data;

namespace ParcelNumberGenerator.Tests;

/// <summary>
/// The schema guarantee the whole concurrency design rests on.
/// </summary>
/// <remarks>
/// The behavioural tests in <see cref="EfUsedNumberStoreTests"/> run against the in-memory
/// provider, which enforces key uniqueness in its own change tracker rather than in a
/// database engine. So those tests prove the store <em>reacts</em> correctly to a rejected
/// duplicate; these prove the thing that will do the rejecting in production is actually in
/// the committed DDL.
/// </remarks>
public sealed class SchemaTests
{
    [Fact]
    public void The_number_is_the_primary_key()
    {
        using ParcelNumbersDbContext context = new(
            new DbContextOptionsBuilder<ParcelNumbersDbContext>()
                .UseInMemoryDatabase(nameof(SchemaTests))
                .Options);

        IKey? key = context.Model.FindEntityType(typeof(UsedNumber))?.FindPrimaryKey();

        Assert.NotNull(key);
        Assert.Equal(nameof(UsedNumber.Number), Assert.Single(key.Properties).Name);
    }

    [Fact]
    public void The_application_chooses_the_number_rather_than_the_database()
    {
        using ParcelNumbersDbContext context = new(
            new DbContextOptionsBuilder<ParcelNumbersDbContext>()
                .UseInMemoryDatabase(nameof(SchemaTests))
                .Options);

        IProperty number = context.Model
            .FindEntityType(typeof(UsedNumber))!
            .FindProperty(nameof(UsedNumber.Number))!;

        // A generated key would hand number selection to the database and make the pool
        // configuration decorative.
        Assert.Equal(ValueGenerated.Never, number.ValueGenerated);
    }

    [Theory]
    [InlineData("PostgreSQL")]
    [InlineData("SqlServer")]
    public void Both_committed_migration_sets_create_the_primary_key(string provider)
    {
        string directory = Path.Combine(
            RepositoryRoot(), "src", $"ParcelNumberGenerator.Migrations.{provider}", "Migrations");

        string[] migrations = [.. Directory.EnumerateFiles(directory, "*_InitialCreate.cs")];
        string source = File.ReadAllText(Assert.Single(migrations));

        Assert.Contains("used_numbers", source, StringComparison.Ordinal);
        Assert.Contains("table.PrimaryKey", source, StringComparison.Ordinal);
        Assert.Contains("x.number", source, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ParcelNumberGenerator.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }
}
