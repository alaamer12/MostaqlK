namespace MostaqlK.Models;

/// <summary>
/// A single skill tag associated with a project (e.g. "PHP", "تصميم شعارات").
/// </summary>
public sealed class ProjectSkill
{
    public string Name { get; set; } = string.Empty;

    public string? Url { get; set; }
}
