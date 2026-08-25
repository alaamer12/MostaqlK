"""Tests for mostaql.config: defaults, precedence (env > file > defaults), and validation."""

from pathlib import Path

import pytest

from mostaql import config
from mostaql.config import (
    DEFAULT_DB_PATH,
    DEFAULT_LOG_FILE_PATH,
    ConfigError,
    Settings,
    load_settings,
)

PROJECT_ROOT = Path(config.__file__).resolve().parents[2]


def test_defaults_match_contract() -> None:
    settings = load_settings({})
    assert settings == Settings()
    assert settings.db_path == DEFAULT_DB_PATH
    assert settings.db_path == PROJECT_ROOT / "data" / "mostaqlk.db"
    assert settings.log_file_path == DEFAULT_LOG_FILE_PATH
    assert settings.log_file_path == PROJECT_ROOT / "data" / "logs" / "interaction-log.txt"
    assert settings.poll_interval_seconds == 30
    assert settings.max_requests_per_minute == 2
    assert settings.safe_requests is True
    assert settings.start_paused is False
    assert settings.query_params == ""
    assert settings.log_level == "INFO"


def test_env_overrides_all_documented_variables() -> None:
    settings = load_settings(
        {
            "MOSTAQL_DB_PATH": "custom/db.sqlite",
            "MOSTAQL_POLL_INTERVAL_SECONDS": "45",
            "MOSTAQL_MAX_REQUESTS_PER_MINUTE": "7",
            "MOSTAQL_SAFE_REQUESTS": "NO",
            "MOSTAQL_QUERY_PARAMS": "?sort=latest",
            "MOSTAQL_LOG_FILE": "custom/log.txt",
            "MOSTAQL_LOG_LEVEL": "debug",
        }
    )
    assert settings.db_path == Path("custom/db.sqlite")
    assert settings.poll_interval_seconds == 45
    assert settings.max_requests_per_minute == 7
    assert settings.safe_requests is False
    assert settings.query_params == "?sort=latest"
    assert settings.log_file_path == Path("custom/log.txt")
    assert settings.log_level == "DEBUG"


def test_poll_interval_clamped_to_range() -> None:
    low = load_settings({"MOSTAQL_POLL_INTERVAL_SECONDS": "5"})
    high = load_settings({"MOSTAQL_POLL_INTERVAL_SECONDS": "9999"})
    assert low.poll_interval_seconds == 10
    assert high.poll_interval_seconds == 3600


def test_garbage_interval_raises_config_error() -> None:
    with pytest.raises(ConfigError, match="poll_interval_seconds"):
        load_settings({"MOSTAQL_POLL_INTERVAL_SECONDS": "abc"})


def test_max_requests_below_minimum_raises_config_error() -> None:
    with pytest.raises(ConfigError, match="max_requests_per_minute"):
        load_settings({"MOSTAQL_MAX_REQUESTS_PER_MINUTE": "0"})


def test_unparseable_boolean_raises_config_error() -> None:
    with pytest.raises(ConfigError, match="safe_requests"):
        load_settings({"MOSTAQL_SAFE_REQUESTS": "maybe"})


def test_invalid_log_level_raises_config_error() -> None:
    with pytest.raises(ConfigError, match="log_level"):
        load_settings({"MOSTAQL_LOG_LEVEL": "LOUD"})


def test_toml_file_applies_and_env_wins(tmp_path: Path) -> None:
    config_file = tmp_path / "mostaql.toml"
    config_file.write_text(
        "\n".join(
            [
                "poll_interval_seconds = 45",
                "max_requests_per_minute = 5",
                "safe_requests = false",
                "start_paused = true",
                'query_params = "?tag=x"',
                'log_level = "warning"',
                'db_path = "from-file/db.sqlite"',
                'log_file_path = "from-file/log.txt"',
            ]
        ),
        encoding="utf-8",
    )
    from_file = load_settings({}, config_file=config_file)
    assert from_file.poll_interval_seconds == 45
    assert from_file.max_requests_per_minute == 5
    assert from_file.safe_requests is False
    assert from_file.start_paused is True
    assert from_file.query_params == "?tag=x"
    assert from_file.log_level == "WARNING"
    assert from_file.db_path == Path("from-file/db.sqlite")
    assert from_file.log_file_path == Path("from-file/log.txt")

    env_wins = load_settings({"MOSTAQL_POLL_INTERVAL_SECONDS": "99"}, config_file=config_file)
    assert env_wins.poll_interval_seconds == 99
    assert env_wins.safe_requests is False


def test_missing_config_file_raises_config_error(tmp_path: Path) -> None:
    with pytest.raises(ConfigError, match="not found"):
        load_settings({}, config_file=tmp_path / "absent.toml")


def test_invalid_toml_raises_config_error(tmp_path: Path) -> None:
    config_file = tmp_path / "broken.toml"
    config_file.write_text("poll_interval_seconds = ", encoding="utf-8")
    with pytest.raises(ConfigError, match="TOML"):
        load_settings({}, config_file=config_file)


def test_unknown_toml_keys_are_ignored(tmp_path: Path) -> None:
    config_file = tmp_path / "extra.toml"
    config_file.write_text('future_setting = "x"\npoll_interval_seconds = 60\n', encoding="utf-8")
    settings = load_settings({}, config_file=config_file)
    assert settings.poll_interval_seconds == 60
