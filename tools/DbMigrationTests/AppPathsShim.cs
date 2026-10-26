namespace MostaqlK.Core.Platform;

/// <summary>
/// Schema-level harness stand-in for the real <c>AppPaths</c> (which depends on MAUI's
/// FileSystem). <c>SqliteConnectionFactory</c>'s parameterized constructor is what this
/// harness uses; the default constructor only needs the type to exist to compile.
/// </summary>
public static class AppPaths
{
    public static string DatabasePath => string.Empty;
}
