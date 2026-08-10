using MostaqlK.Core;

namespace MostaqlK.Infrastructure.Database;

/// <summary>
/// Error factory for SQLite access failures. Codes use the "DB" domain
/// (see <see cref="ErrorCodeRegistry"/>).
/// </summary>
public static class DatabaseErrors
{
    public static DomainError ConnectionFailed(Exception cause) => new(
        Code: "DB-001",
        InternalMessage: $"Failed to open SQLite connection: {cause.Message}",
        ExternalMessage: "تعذر الوصول إلى قاعدة البيانات المحلية.",
        Cause: cause);

    public static DomainError QueryFailed(string operation, Exception cause) => new(
        Code: "DB-002",
        InternalMessage: $"Query '{operation}' failed: {cause.Message}",
        ExternalMessage: "حدث خطأ أثناء الوصول إلى البيانات المحفوظة.",
        Cause: cause);

    public static DomainError SchemaInvalid(string details) => new(
        Code: "DB-003",
        InternalMessage: $"Database schema is invalid or out of date: {details}",
        ExternalMessage: "قاعدة البيانات غير متوافقة مع هذا الإصدار من التطبيق.");

    internal static DatabaseSchemaException SchemaVersionMismatch(long currentVersion, int expectedVersion) =>
        new($"Database schema version {currentVersion} does not match the version expected " +
            $"by this build ({expectedVersion}) and no migration path exists yet.");
}
