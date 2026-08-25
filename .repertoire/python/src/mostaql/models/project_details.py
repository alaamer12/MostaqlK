"""Fully enriched project model (C# Models/ProjectDetails + provenance records).

Design decision — INHERITANCE over composition: ``ProjectDetails`` extends
``ProjectSummary`` (both frozen+slots, which composes cleanly). This keeps a flat
constructor mirroring C# DetailParser's object-initializer usage and lets details
flow through any summary-shaped consumer. Documented deviation: C# duplicates the
fields instead of inheriting and omits ClientName/SkillsText/IsUnread; the Python
subclass carries them with safe defaults (storage binds owner.name for client_name
in the details path, exactly like C# UpsertDetailsAsync).
"""

from dataclasses import dataclass, field

from mostaql.models.field_resolution import FieldMismatch, FieldResolution
from mostaql.models.owner import Owner
from mostaql.models.project_skill import ProjectSkill
from mostaql.models.project_summary import ProjectSummary

__all__ = ["ProjectDetails"]


@dataclass(frozen=True, slots=True)
class ProjectDetails(ProjectSummary):
    """Fully enriched project from its own detail page.

    DetailParser stamps ``discovered_at`` == ``enriched_at`` to the same instant;
    both stay plain tz-aware UTC datetimes.
    """

    owner: Owner = field(default_factory=Owner)
    skills: list[ProjectSkill] = field(default_factory=list)
    field_provenance: dict[str, FieldResolution] = field(default_factory=dict)
    mismatches: list[FieldMismatch] = field(default_factory=list)
