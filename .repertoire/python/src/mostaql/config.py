"""Application settings: loading order is defaults < optional TOML file < environment.

Default database and log paths are anchored to the Python project root (the directory
containing ``pyproject.toml``), resolved via ``Path(__file__)`` parents so behaviour never
depends on the current working directory. Invalid values from any source raise
:class:`ConfigError` (a ``ValueError`` subclass); nothing here prints.
"""

import logging
import tomllib
from collections.abc import Mapping
from dataclasses import dataclass, field
from pathlib import Path

PROJECT_ROOT: Path = Path(__file__).resolve().parents[2]
DEFAULT_DB_PATH: Path = PROJECT_ROOT / "data" / "mostaqlk.db"
DEFAULT_LOG_FILE_PATH: Path = PROJECT_ROOT / "data" / "logs" / "interaction-log.txt"

POLL_INTERVAL_MINIMUM_SECONDS = 10
POLL_INTERVAL_MAXIMUM_SECONDS = 3600
MIN_REQUESTS_PER_MINUTE = 1

ENV_DB_PATH = "MOSTAQL_DB_PATH"
ENV_POLL_INTERVAL_SECONDS = "MOSTAQL_POLL_INTERVAL_SECONDS"
ENV_MAX_REQUESTS_PER_MINUTE = "MOSTAQL_MAX_REQUESTS_PER_MINUTE"
ENV_SAFE_REQUESTS = "MOSTAQL_SAFE_REQUESTS"
ENV_QUERY_PARAMS = "MOSTAQL_QUERY_PARAMS"
ENV_LOG_FILE = "MOSTAQL_LOG_FILE"
ENV_LOG_LEVEL = "MOSTAQL_LOG_LEVEL"

_ENV_FIELDS: tuple[tuple[str, str], ...] = (
    (ENV_DB_PATH, "db_path"),
    (ENV_POLL_INTERVAL_SECONDS, "poll_interval_seconds"),
    (ENV_MAX_REQUESTS_PER_MINUTE, "max_requests_per_minute"),
    (ENV_SAFE_REQUESTS, "safe_requests"),
    (ENV_QUERY_PARAMS, "query_params"),
    (ENV_LOG_FILE, "log_file_path"),
    (ENV_LOG_LEVEL, "log_level"),
)

_KNOWN_KEYS: frozenset[str] = frozenset(
    {
        "db_path",
        "poll_interval_seconds",
        "max_requests_per_minute",
        "safe_requests",
        "start_paused",
        "query_params",
        "log_file_path",
        "log_level",
    }
)

_FALSE_TOKENS: frozenset[str] = frozenset({"0", "false", "no"})
_TRUE_TOKENS: frozenset[str] = frozenset({"1", "true", "yes"})


class ConfigError(ValueError):
    """Raised when a configuration source supplies an unusable value."""


@dataclass(slots=True)
class Settings:
    """Effective backbone configuration (plan §8 contract)."""

    db_path: Path = field(default_factory=lambda: DEFAULT_DB_PATH)
    poll_interval_seconds: int = 30
    max_requests_per_minute: int = 2
    safe_requests: bool = True
    start_paused: bool = False
    query_params: str = ""
    log_file_path: Path = field(default_factory=lambda: DEFAULT_LOG_FILE_PATH)
    log_level: str = "INFO"


def load_settings(env: Mapping[str, str], config_file: Path | None = None) -> Settings:
    """Build :class:`Settings`: defaults first, then TOML ``config_file``, then ``env``."""
    values: dict[str, object] = {}
    if config_file is not None:
        values.update(_load_config_file(config_file))
    values.update(_load_env_values(env))
    return _build_settings(values)


def _load_config_file(config_file: Path) -> dict[str, object]:
    try:
        with config_file.open("rb") as handle:
            parsed = tomllib.load(handle)
    except FileNotFoundError as exc:
        msg = f"configuration file not found: {config_file}"
        raise ConfigError(msg) from exc
    except tomllib.TOMLDecodeError as exc:
        msg = f"invalid TOML in configuration file {config_file}: {exc}"
        raise ConfigError(msg) from exc
    return {key: value for key, value in parsed.items() if key in _KNOWN_KEYS}


def _load_env_values(env: Mapping[str, str]) -> dict[str, object]:
    values: dict[str, object] = {}
    for env_key, field_name in _ENV_FIELDS:
        raw = env.get(env_key)
        if raw is not None:
            values[field_name] = raw
    return values


def _build_settings(values: Mapping[str, object]) -> Settings:
    interval = _parse_int(values.get("poll_interval_seconds", 30), "poll_interval_seconds")
    requests_per_minute = _parse_int(
        values.get("max_requests_per_minute", 2), "max_requests_per_minute"
    )
    if requests_per_minute < MIN_REQUESTS_PER_MINUTE:
        msg = (
            f"max_requests_per_minute must be >= {MIN_REQUESTS_PER_MINUTE}, "
            f"got {requests_per_minute}"
        )
        raise ConfigError(msg)
    level = _parse_str(values.get("log_level", "INFO"), "log_level").upper()
    if level not in logging.getLevelNamesMapping():
        msg = f"log_level must be a standard logging level name, got {level!r}"
        raise ConfigError(msg)
    return Settings(
        db_path=_parse_path(values.get("db_path", DEFAULT_DB_PATH), "db_path"),
        poll_interval_seconds=max(
            POLL_INTERVAL_MINIMUM_SECONDS,
            min(POLL_INTERVAL_MAXIMUM_SECONDS, interval),
        ),
        max_requests_per_minute=requests_per_minute,
        safe_requests=_parse_bool(values.get("safe_requests", True), "safe_requests"),
        start_paused=_parse_bool(values.get("start_paused", False), "start_paused"),
        query_params=_parse_str(values.get("query_params", ""), "query_params"),
        log_file_path=_parse_path(
            values.get("log_file_path", DEFAULT_LOG_FILE_PATH), "log_file_path"
        ),
        log_level=level,
    )


def _parse_int(raw: object, key: str) -> int:
    if isinstance(raw, bool):
        msg = f"{key}: boolean is not a valid integer value"
        raise ConfigError(msg)
    if isinstance(raw, int):
        return raw
    if isinstance(raw, str):
        try:
            return int(raw.strip())
        except ValueError as exc:
            msg = f"{key}: cannot parse {raw!r} as an integer"
            raise ConfigError(msg) from exc
    msg = f"{key}: expected an integer, got {type(raw).__name__}"
    raise ConfigError(msg)


def _parse_bool(raw: object, key: str) -> bool:
    if isinstance(raw, bool):
        return raw
    if isinstance(raw, str):
        token = raw.strip().lower()
        if token in _FALSE_TOKENS:
            return False
        if token in _TRUE_TOKENS:
            return True
        msg = f"{key}: cannot parse {raw!r} as a boolean (accepted: 0/false/no or 1/true/yes)"
        raise ConfigError(msg)
    msg = f"{key}: expected a boolean, got {type(raw).__name__}"
    raise ConfigError(msg)


def _parse_str(raw: object, key: str) -> str:
    if isinstance(raw, str):
        return raw
    msg = f"{key}: expected a string, got {type(raw).__name__}"
    raise ConfigError(msg)


def _parse_path(raw: object, key: str) -> Path:
    if isinstance(raw, Path):
        return raw
    if isinstance(raw, str):
        return Path(raw)
    msg = f"{key}: expected a path string, got {type(raw).__name__}"
    raise ConfigError(msg)
