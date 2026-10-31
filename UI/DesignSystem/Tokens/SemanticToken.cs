namespace MostaqlK.UI.DesignSystem.Tokens;

/// <summary>
/// One semantic color token: a name plus its exact light/dark rendering values. Values are
/// golden-tested by <c>tools/ParserTests</c>; a change here is a deliberate design change.
/// </summary>
public sealed record SemanticToken(string Name, string Light, string Dark);
