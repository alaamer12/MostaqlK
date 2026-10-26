namespace MostaqlK.Sources.Nafezly.Parsing;

/// <summary>
/// Thrown internally by the nafezly parsers when the page structure does not match the expected
/// shape. Kept module-local (not reused from the Mostaql module) so neither source names the other.
/// Callers translate it into a <see cref="MostaqlK.Core.DomainError"/> via the PARSE error domain.
/// </summary>
public sealed class NafezlyParseException : Exception
{
    public NafezlyParseException(string message) : base(message)
    {
    }

    public NafezlyParseException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
