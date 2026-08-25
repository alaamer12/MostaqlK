"""Frozen domain models mirroring C# Models/* (plan §8 contracts).

Pure leaf layer: imports only sibling ``mostaql.models`` modules (import-linter
``pure-leaves`` contract). All dataclasses are frozen with slots.
"""

from mostaql.models.enrichment_status import EnrichmentStatus
from mostaql.models.field_resolution import FieldMismatch, FieldResolution
from mostaql.models.owner import Owner
from mostaql.models.project_details import ProjectDetails
from mostaql.models.project_skill import ProjectSkill
from mostaql.models.project_summary import ProjectSummary

__all__ = [
    "EnrichmentStatus",
    "FieldMismatch",
    "FieldResolution",
    "Owner",
    "ProjectDetails",
    "ProjectSkill",
    "ProjectSummary",
]
