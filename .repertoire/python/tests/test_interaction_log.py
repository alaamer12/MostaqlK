"""Tests for the interaction log sink: line format, resilience, and singleton binding."""

import json
import re
from collections.abc import Iterator
from pathlib import Path

import pytest

from mostaql.diagnostics import interaction_log
from mostaql.diagnostics.interaction_log import InteractionLogger, get_interaction_logger

TIMESTAMP_PATTERN = r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}[+-]\d{2}:\d{2}"


class FakeDomainError:
    """Structurally satisfies FailureLike; mostaql.errors does not exist yet (Wave B1)."""

    def __init__(
        self,
        code: str = "HTTP-101",
        internal: str = "internal message",
        external: str = "external message",
        fix: str | None = None,
        cause: BaseException | None = None,
    ) -> None:
        self.code = code
        self.internal_message = internal
        self.external_message = external
        self.fix_message = fix
        self.cause = cause


class Weird:
    def __str__(self) -> str:
        return "WEIRD"


@pytest.fixture()
def bound_log(tmp_path: Path) -> Iterator[tuple[InteractionLogger, Path]]:
    interaction_log._instance = None
    path = tmp_path / "interaction-log.txt"
    logger = get_interaction_logger(path)
    yield logger, path
    interaction_log._instance = None


def read_lines(path: Path) -> list[str]:
    return path.read_text(encoding="utf-8").splitlines()


def parts_of(path: Path) -> list[list[str]]:
    return [line.split(" | ") for line in read_lines(path)]


def test_mark_line_format(bound_log: tuple[InteractionLogger, Path]) -> None:
    logger, path = bound_log
    logger.mark("pipeline.poll", "A", {"seen": 3})
    (segments,) = parts_of(path)
    assert len(segments) == 5
    assert re.fullmatch(TIMESTAMP_PATTERN, segments[0])
    assert segments[1] == "MARK"
    assert segments[2] == "pipeline.poll"
    assert segments[3] == "variant=A"
    assert segments[4] == 'data={"seen":3}'


def test_mark_without_data_omits_optional_segments(
    bound_log: tuple[InteractionLogger, Path],
) -> None:
    logger, path = bound_log
    logger.mark("checkpoint", "B")
    (segments,) = parts_of(path)
    assert re.fullmatch(TIMESTAMP_PATTERN, segments[0])
    assert segments[1:] == ["MARK", "checkpoint", "variant=B"]


def test_fault_line_includes_exception_and_data(
    bound_log: tuple[InteractionLogger, Path],
) -> None:
    logger, path = bound_log
    logger.fault("worker.run", ValueError("boom"), {"attempt": 2})
    (segments,) = parts_of(path)
    assert segments[1] == "FAULT"
    assert segments[2] == "worker.run"
    assert segments[3] == 'data={"attempt":2}'
    assert segments[4] == "exception=ValueError: boom"


def test_failure_emits_error_payload_with_cause(
    bound_log: tuple[InteractionLogger, Path],
) -> None:
    logger, path = bound_log
    error = FakeDomainError(
        code="ENRICH-001",
        internal="gave up",
        external="تعذر إثراء المشروع",
        fix="retry later",
        cause=RuntimeError("socket died"),
    )
    logger.failure("enrich.project", error, "detail-value")
    (segments,) = parts_of(path)
    assert segments[1] == "ERROR"
    assert segments[2] == "enrich.project"
    assert segments[3] == "variant=ENRICH-001"
    payload = json.loads(segments[4].removeprefix("data="))
    assert payload == {
        "Code": "ENRICH-001",
        "InternalMessage": "gave up",
        "ExternalMessage": "تعذر إثراء المشروع",
        "FixMessage": "retry later",
        "Detail": "detail-value",
    }
    assert segments[5] == "exception=RuntimeError: socket died"


def test_failure_without_cause_has_no_exception_segment(
    bound_log: tuple[InteractionLogger, Path],
) -> None:
    logger, path = bound_log
    logger.failure("checkpoint", FakeDomainError())
    (segments,) = parts_of(path)
    assert len(segments) == 5
    assert segments[3] == "variant=HTTP-101"
    payload = json.loads(segments[4].removeprefix("data="))
    assert payload["FixMessage"] is None
    assert payload["Detail"] is None


def test_non_serializable_data_falls_back_to_str(
    bound_log: tuple[InteractionLogger, Path],
) -> None:
    logger, path = bound_log
    logger.mark("checkpoint", "A", Weird())
    (segments,) = parts_of(path)
    assert segments[-1] == "data=WEIRD"


def test_never_raises_on_unwritable_path(tmp_path: Path) -> None:
    blocker = tmp_path / "blocker"
    blocker.write_text("occupied", encoding="utf-8")
    logger = InteractionLogger(blocker / "nested" / "log.txt")
    logger.mark("checkpoint", "A")
    logger.fault("checkpoint", ValueError("x"))
    logger.failure("checkpoint", FakeDomainError(), {"k": 1})
    assert blocker.read_text(encoding="utf-8") == "occupied"
    assert not (blocker / "nested").exists()


def test_singleton_binds_first_path_and_ignores_later_args(tmp_path: Path) -> None:
    interaction_log._instance = None
    try:
        first = tmp_path / "first.txt"
        second = tmp_path / "second.txt"
        initial = get_interaction_logger(first)
        again = get_interaction_logger(second)
        assert again is initial
        again.mark("checkpoint", "A")
        assert first.exists()
        assert not second.exists()
    finally:
        interaction_log._instance = None


def test_get_interaction_logger_returns_same_instance(
    bound_log: tuple[InteractionLogger, Path],
) -> None:
    logger, _ = bound_log
    assert get_interaction_logger() is logger
