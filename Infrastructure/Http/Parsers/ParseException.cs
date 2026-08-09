namespace MostaqlK.Infrastructure.Http.Parsers;

/// <summary>
/// Thrown internally by parsers when the Mostaql page structure does not match the
/// expected shape (e.g. selector not found). Callers should catch this and translate
/// it into a <see cref="MostaqlK.Core.DomainError"/> via the PARSE error domain.
/// </summary>
public sealed class ParseException : Exception
{
    public ParseException(string message) : base(message)
    {
    }

    public ParseException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
