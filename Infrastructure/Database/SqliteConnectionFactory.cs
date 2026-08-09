using Microsoft.Data.Sqlite;

namespace MostaqlK.Infrastructure.Database;

/// <summary>
/// Creates configured <see cref="SqliteConnection"/> instances pointing at the app's
/// local database file under app data, ensuring the schema/migrations have been applied.
/// </summary>
public sealed class SqliteConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory()
    {
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "mostaqlk.db");
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
    }

    public SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        // TODO: open the connection, run pending migrations from Migrations/, and verify schema.
        return connection;
    }
}
