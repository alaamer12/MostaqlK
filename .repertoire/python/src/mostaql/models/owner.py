"""The Mostaql client/employer who posted a project (C# Models/Owner)."""

from dataclasses import dataclass

__all__ = ["Owner"]


@dataclass(frozen=True, slots=True)
class Owner:
    """Mostaql client/employer model.

    Defaults mirror C#: identity fields default-valued, all profile stats nullable.
    """

    owner_id: int = 0
    name: str = ""
    profile_url: str | None = None
    avatar_url: str | None = None
    rating: float | None = None
    completed_projects_count: int | None = None
    hiring_rate_percent: float | None = None
    registered_at: str | None = None
    open_projects_count: int | None = None
    in_progress_projects_count: int | None = None
    ongoing_communications_count: int | None = None
