"""Unit tests for .NET "O" timestamp helpers (storage trap 1)."""

from datetime import UTC, datetime, timedelta, timezone

import pytest

from mostaql.storage.timestamps import current_utc, dotnet_o_format, parse_dotnet_o

UTC = UTC


def test_format_emits_seven_fraction_digits_and_offset() -> None:
    dt = datetime(2026, 8, 25, 14, 30, 12, 123456, tzinfo=UTC)
    assert dotnet_o_format(dt) == "2026-08-25T14:30:12.1234560+00:00"


def test_format_pads_fraction_generically() -> None:
    cases = {
        0: "0000000",
        5: "0000050",
        5000: "0050000",
        123456: "1234560",
        999999: "9999990",
    }
    for micro, fraction in cases.items():
        dt = datetime(2026, 1, 1, 0, 0, 0, micro, tzinfo=UTC)
        assert dotnet_o_format(dt) == f"2026-01-01T00:00:00.{fraction}+00:00"


def test_format_utc_uses_plus_zero_offset_not_z() -> None:
    dt = datetime(2026, 8, 25, 14, 30, 12, tzinfo=UTC)
    formatted = dotnet_o_format(dt)
    assert formatted.endswith("+00:00")
    assert not formatted.endswith("Z")


def test_format_non_utc_offset() -> None:
    offset = timezone(timedelta(hours=3))
    dt = datetime(2026, 8, 25, 14, 30, 12, 1, tzinfo=offset)
    assert dotnet_o_format(dt) == "2026-08-25T14:30:12.0000010+03:00"


def test_format_negative_offset() -> None:
    offset = timezone(timedelta(hours=-5, minutes=-30))
    dt = datetime(2026, 8, 25, 14, 30, 12, tzinfo=offset)
    assert dotnet_o_format(dt) == "2026-08-25T14:30:12.0000000-05:30"


def test_format_rejects_naive_datetime() -> None:
    with pytest.raises(ValueError):
        dotnet_o_format(datetime(2026, 8, 25, 14, 30, 12))


@pytest.mark.parametrize(
    ("text", "expected"),
    [
        (
            "2026-08-25T14:30:12.1234567+03:00",
            datetime(2026, 8, 25, 14, 30, 12, 123456, tzinfo=timezone(timedelta(hours=3))),
        ),
        ("2026-08-25T14:30:12.123456+00:00", datetime(2026, 8, 25, 14, 30, 12, 123456, tzinfo=UTC)),
        ("2026-08-25T14:30:12.1234567Z", datetime(2026, 8, 25, 14, 30, 12, 123456, tzinfo=UTC)),
        ("2026-08-25T14:30:12Z", datetime(2026, 8, 25, 14, 30, 12, tzinfo=UTC)),
        (
            "2026-08-25T14:30:12-05:30",
            datetime(2026, 8, 25, 14, 30, 12, tzinfo=timezone(timedelta(hours=-5, minutes=-30))),
        ),
    ],
)
def test_parse_accepts_dotnet_shapes(text: str, expected: datetime) -> None:
    assert parse_dotnet_o(text) == expected


def test_round_trip_preserves_instant_and_offset() -> None:
    original = datetime(2026, 8, 25, 9, 15, 33, 987654, tzinfo=timezone(timedelta(hours=2)))
    parsed = parse_dotnet_o(dotnet_o_format(original))
    assert parsed == original
    assert parsed.utcoffset() == original.utcoffset()
    assert dotnet_o_format(parsed) == dotnet_o_format(original)


def test_current_utc_is_tz_aware() -> None:
    now = current_utc()
    assert now.tzinfo is not None
    assert now.utcoffset() == timedelta(0)
