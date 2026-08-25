"""Field-provenance records (declared beside C# Models/ProjectDetails)."""

from dataclasses import dataclass

__all__ = ["FieldMismatch", "FieldResolution"]


@dataclass(frozen=True, slots=True)
class FieldResolution:
    """Resolved value plus provenance for a single meta-row field.

    Mirrors C# ``FieldResolution(string? Value, string Source, double Confidence)``;
    ``source`` carries the structural/inference strategy label that produced it.
    """

    value: str | None
    source: str
    confidence: float


@dataclass(frozen=True, slots=True)
class FieldMismatch:
    """A structural/inference disagreement recorded for a single field.

    Mirrors C# ``FieldMismatch(string Field, string? StructuralValue, string? InferenceValue)``;
    kept for drift detection only — inference overrides only on failed sanity.
    """

    field: str
    structural_value: str | None
    inference_value: str | None
