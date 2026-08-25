"""A single skill tag associated with a project (C# Models/ProjectSkill)."""

from dataclasses import dataclass

__all__ = ["ProjectSkill"]


@dataclass(frozen=True, slots=True)
class ProjectSkill:
    """A single skill tag associated with a project (e.g. "PHP", "تصميم شعارات")."""

    name: str
    url: str | None = None
