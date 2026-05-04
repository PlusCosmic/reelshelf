using EvolveDb;
using EvolveDb.Migration;
using Npgsql;

const string metadataTableName = "changelog";
const string defaultMigrationLocation = "db/migrations";
const string baselineVersion = "16";

MigrationOptions options = MigrationOptions.Parse(args);

if (options.ShowHelp)
{
    MigrationOptions.PrintHelp();
    return 0;
}

string? connectionString = options.ConnectionString
                           ?? Environment.GetEnvironmentVariable("DatabaseConnectionString")
                           ?? Environment.GetEnvironmentVariable("ConnectionStrings__DatabaseConnectionString");

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Missing database connection string.");
    Console.Error.WriteLine("Pass --connection-string, or set DatabaseConnectionString / ConnectionStrings__DatabaseConnectionString.");
    return 2;
}

await using NpgsqlConnection connection = new(connectionString);
await connection.OpenAsync();

bool hasMetadataTable = await HasMetadataTable(connection);
bool hasApplicationTables = await HasApplicationTables(connection);
bool hasBaselineVersion = hasMetadataTable && await HasSuccessfulVersion(connection, baselineVersion);

if (hasApplicationTables && !hasMetadataTable && !options.AdoptExisting)
{
    Console.Error.WriteLine("Refusing to run migrations against a non-empty database with no Evolve metadata.");
    Console.Error.WriteLine("For the first deployment to an existing production database, run once with --adopt-existing.");
    Console.Error.WriteLine($"That records the current database as already being at V{baselineVersion} and does not execute V{baselineVersion}__baseline_schema.sql.");
    return 3;
}

Evolve evolve = new(connection, Console.WriteLine)
{
    Locations = [options.Location ?? defaultMigrationLocation],
    IsEraseDisabled = true,
    MetadataTableName = metadataTableName
};

if (options.AdoptExisting && hasApplicationTables && !hasMetadataTable)
{
    Console.WriteLine($"Adopting existing database at migration version {baselineVersion}; baseline SQL will not be executed.");
    evolve.StartVersion = new MigrationVersion(baselineVersion);
}
else if (hasMetadataTable && !hasBaselineVersion)
{
    Console.WriteLine($"Existing Evolve metadata found without V{baselineVersion}; continuing from legacy history at V{baselineVersion}.");
    evolve.StartVersion = new MigrationVersion(baselineVersion);
}

evolve.Migrate();
return 0;

static async Task<bool> HasMetadataTable(NpgsqlConnection connection)
{
    await using NpgsqlCommand command = new("""
        SELECT EXISTS (
            SELECT 1
            FROM information_schema.tables
            WHERE table_schema = current_schema()
              AND table_name = @tableName
        );
        """, connection);
    command.Parameters.AddWithValue("tableName", metadataTableName);
    return (bool)(await command.ExecuteScalarAsync() ?? false);
}

static async Task<bool> HasApplicationTables(NpgsqlConnection connection)
{
    await using NpgsqlCommand command = new("""
        SELECT EXISTS (
            SELECT 1
            FROM information_schema.tables
            WHERE table_schema = current_schema()
              AND table_type = 'BASE TABLE'
              AND table_name <> @metadataTableName
        );
        """, connection);
    command.Parameters.AddWithValue("metadataTableName", metadataTableName);
    return (bool)(await command.ExecuteScalarAsync() ?? false);
}

static async Task<bool> HasSuccessfulVersion(NpgsqlConnection connection, string version)
{
    await using NpgsqlCommand command = new("""
        SELECT EXISTS (
            SELECT 1
            FROM changelog
            WHERE type = 0
              AND version = @version
              AND success = TRUE
        );
        """, connection);
    command.Parameters.AddWithValue("version", version);
    return (bool)(await command.ExecuteScalarAsync() ?? false);
}

internal sealed record MigrationOptions(
    string? ConnectionString,
    string? Location,
    bool AdoptExisting,
    bool ShowHelp)
{
    public static MigrationOptions Parse(string[] args)
    {
        string? connectionString = null;
        string? location = null;
        bool adoptExisting = false;
        bool showHelp = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            switch (arg)
            {
                case "--connection-string":
                    connectionString = ReadValue(args, ref i, arg);
                    break;
                case "--location":
                    location = ReadValue(args, ref i, arg);
                    break;
                case "--adopt-existing":
                    adoptExisting = true;
                    break;
                case "-h":
                case "--help":
                    showHelp = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        return new MigrationOptions(connectionString, location, adoptExisting, showHelp);
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
            Nucleus database migration runner

            Usage:
              dotnet run --project tools/Nucleus.Migrations -- [options]

            Options:
              --connection-string <value>  PostgreSQL connection string. Defaults to DatabaseConnectionString or ConnectionStrings__DatabaseConnectionString.
              --location <path>            Migration folder. Defaults to db/migrations copied beside the tool output.
              --adopt-existing             First-run production adoption mode for an existing non-empty database.
              --help                       Show this help.

            Safety:
              Normal migrate refuses a non-empty database with no Evolve changelog.
              Use --adopt-existing once for an existing production database that already matches V16.
            """);
    }

    private static string ReadValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"{optionName} requires a value.");
        }

        index++;
        return args[index];
    }
}
