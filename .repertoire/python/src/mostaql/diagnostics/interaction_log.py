"""Process-wide interaction log sink, line-compatible with the C# ``InteractionLogger``.

Mirrors ``Services/Diagnostics/InteractionLogger.cs``: one line per event of the form

    <timestamp> | KIND | checkpoint[ | variant=X][ | data=<json>][ | exception=Type: msg]

with kinds MARK / FAULT / ERROR, a process-wide write lock, open-append-close UTF-8 writes
that never raise, and a local-offset timestamp shaped like .NET's "O" format (7 fraction
digits, ``+HH:MM`` offset with colon). ``failure()`` emits kind ERROR with
``variant=<error.code>`` and ``data={Code, InternalMessage, ExternalMessage, FixMessage,
Detail}`` exactly like the C# original; the exception segment renders the cause when one is
present.

Failures are accepted through the structural :class:`FailureLike` protocol so this module
stays pure-stdlib and imports nothing from ``mostaql``; ``mostaql.errors.DomainError``
(Wave B1) will satisfy it without introducing a dependency edge here.
"""

import contextlib
import json
import threading
from collections.abc import Mapping
from datetime import datetime
from pathlib import Path
from typing import Protocol

KIND_MARK = "MARK"
KIND_FAULT = "FAULT"
KIND_ERROR = "ERROR"

_WRITE_LOCK = threading.Lock()

_DEFAULT_LOG_PATH: Path = (
    Path(__file__).resolve().parents[3] / "data" / "logs" / "interaction-log.txt"
)


class FailureLike(Protocol):
    """Structural stand-in for ``mostaql.errors.DomainError`` (defined in Wave B1)."""

    code: str
    internal_message: str
    external_message: str
    fix_message: str | None
    cause: BaseException | None


class InteractionLogger:
    """Append-only diagnostics sink that never raises (see the C# InteractionLogger)."""

    def __init__(self, log_path: Path) -> None:
        self._log_path = Path(log_path)
        self._parent_created = False

    @property
    def log_path(self) -> Path:
        """Bound destination file (useful for tests and tailing tools)."""
        return self._log_path

    def mark(
        self,
        checkpoint: str,
        variant: str,
        data: Mapping[str, object] | object | None = None,
    ) -> None:
        """Bracket-style A/B marker (kind MARK)."""
        self._write(KIND_MARK, checkpoint, variant, data, None)

    def fault(
        self,
        checkpoint: str,
        exc: BaseException,
        data: Mapping[str, object] | object | None = None,
    ) -> None:
        """Unexpected-exception sink (kind FAULT)."""
        self._write(KIND_FAULT, checkpoint, None, data, exc)

    def failure(
        self,
        checkpoint: str,
        error: FailureLike,
        data: Mapping[str, object] | object | None = None,
    ) -> None:
        """Domain-failure sink (kind ERROR), payload layout identical to C# ``Failure``."""
        payload: dict[str, object] = {
            "Code": error.code,
            "InternalMessage": error.internal_message,
            "ExternalMessage": error.external_message,
            "FixMessage": error.fix_message,
            "Detail": data,
        }
        self._write(KIND_ERROR, checkpoint, error.code, payload, error.cause)

    def _write(
        self,
        kind: str,
        checkpoint: str,
        variant: str | None,
        data: Mapping[str, object] | object | None,
        exception: BaseException | None,
    ) -> None:
        line = _format_line(kind, checkpoint, variant, data, exception)
        with _WRITE_LOCK, contextlib.suppress(Exception):
            if not self._parent_created:
                self._log_path.parent.mkdir(parents=True, exist_ok=True)
                self._parent_created = True
            with self._log_path.open("a", encoding="utf-8") as handle:
                handle.write(line)


def _format_line(
    kind: str,
    checkpoint: str,
    variant: str | None,
    data: Mapping[str, object] | object | None,
    exception: BaseException | None,
) -> str:
    parts = [_timestamp(), kind, checkpoint]
    if variant is not None:
        parts.append(f"variant={variant}")
    if data is not None:
        parts.append(f"data={_serialize(data)}")
    if exception is not None:
        parts.append(f"exception={type(exception).__name__}: {exception}")
    return " | ".join(parts) + "\n"


def _timestamp() -> str:
    now = datetime.now().astimezone()
    fraction = f"{now.microsecond * 10:07d}"
    raw_offset = now.strftime("%z")
    offset = f"{raw_offset[:3]}:{raw_offset[3:]}"
    return f"{now:%Y-%m-%dT%H:%M:%S}.{fraction}{offset}"


def _serialize(data: object) -> str:
    try:
        return json.dumps(data, separators=(",", ":"))
    except (TypeError, ValueError):
        return str(data)


_instance: InteractionLogger | None = None


def get_interaction_logger(log_path: Path | None = None) -> InteractionLogger:
    """Return the process-wide singleton.

    The first call binds the destination path (falling back to the project-local default);
    later calls return the bound instance and ignore any argument.
    """
    global _instance
    with _WRITE_LOCK:
        if _instance is None:
            _instance = InteractionLogger(log_path if log_path is not None else _DEFAULT_LOG_PATH)
        return _instance
