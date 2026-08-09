namespace MostaqlK.Infrastructure.Database;

/// <summary>
/// Thrown when the SQLite database file exists but its schema does not match the
/// version expected by this build of MostaqlK (missing/renamed tables, columns, etc).
/// </summary>
public sealed class DatabaseSchemaException : Exception
{
    public DatabaseSchemaException(string message) : base(message)
    {
    }

    public DatabaseSchemaException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
