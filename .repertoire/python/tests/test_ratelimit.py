"""TokenBucketRateLimiter unit tests against the C# behavioral spec."""

import asyncio
from collections.abc import Callable

import pytest

import mostaql.pipeline.ratelimit as ratelimit_module
from mostaql.pipeline.ratelimit import TokenBucketRateLimiter


class FakeClock:
    """Deterministic monotonic clock advanced explicitly (or by the patched sleeper)."""

    def __init__(self, start: float = 1000.0) -> None:
        self.now = start

    def __call__(self) -> float:
        return self.now

    def advance(self, seconds: float) -> None:
        self.now += seconds


def patch_sleep(monkeypatch: pytest.MonkeyPatch, clock: FakeClock) -> list[float]:
    """Replace the limiter's sleep seam: record every computed wait, advance the clock."""
    sleeps: list[float] = []

    async def recording_sleep(delay: float) -> None:
        sleeps.append(delay)
        clock.advance(delay)

    monkeypatch.setattr(ratelimit_module, "_SLEEP", recording_sleep)
    return sleeps


@pytest.fixture
def clock() -> FakeClock:
    return FakeClock()


async def test_first_grant_consumes_full_bucket_without_waiting(
    monkeypatch: pytest.MonkeyPatch, clock: FakeClock
) -> None:
    sleeps = patch_sleep(monkeypatch, clock)
    limiter = TokenBucketRateLimiter(requests_per_minute=2, safe_requests=True, clock=clock)

    await limiter.wait_for_token()

    assert sleeps == []
    assert limiter.available_tokens == 1.0


async def test_safe_mode_spaces_consecutive_grants_by_one_second(
    monkeypatch: pytest.MonkeyPatch, clock: FakeClock
) -> None:
    sleeps = patch_sleep(monkeypatch, clock)
    limiter = TokenBucketRateLimiter(requests_per_minute=60, safe_requests=True, clock=clock)

    await limiter.wait_for_token()
    await limiter.wait_for_token()

    # Second grant had tokens available but still owed the minimum spacing.
    assert len(sleeps) == 1
    assert sleeps[0] == pytest.approx(1.0)


async def test_fast_mode_has_zero_spacing(
    monkeypatch: pytest.MonkeyPatch, clock: FakeClock
) -> None:
    sleeps = patch_sleep(monkeypatch, clock)
    limiter = TokenBucketRateLimiter(requests_per_minute=60, safe_requests=False, clock=clock)

    await limiter.wait_for_token()
    await limiter.wait_for_token()

    assert sleeps == []


async def test_exhaustion_waits_computed_from_fractional_refill(
    monkeypatch: pytest.MonkeyPatch, clock: FakeClock
) -> None:
    """rpm=2 -> refill 1/30 per second; the ladder ends owing ~28s of refill."""
    sleeps = patch_sleep(monkeypatch, clock)
    limiter = TokenBucketRateLimiter(requests_per_minute=2, safe_requests=True, clock=clock)

    await limiter.wait_for_token()
    await limiter.wait_for_token()  # spacing sleep (1s), which refills 1/30
    await limiter.wait_for_token()  # spacing again (1s), then the computed refill wait

    assert sleeps[0] == pytest.approx(1.0)
    assert sleeps[1] == pytest.approx(1.0)
    # tokens after two spaced grants = 2 - 1 + 2/30 - 1 = 1/15; missing 14/15 at 1/30/s.
    assert sleeps[-1] == pytest.approx(28.0, abs=0.5)


async def test_fast_mode_refills_ten_times_quicker(
    monkeypatch: pytest.MonkeyPatch, clock: FakeClock
) -> None:
    """rpm=2 unsafe -> refill 1/3 per second; third grant owes ~3s instead of ~29s."""
    sleeps = patch_sleep(monkeypatch, clock)
    limiter = TokenBucketRateLimiter(requests_per_minute=2, safe_requests=False, clock=clock)

    await limiter.wait_for_token()
    await limiter.wait_for_token()
    await limiter.wait_for_token()

    assert sleeps[-1] == pytest.approx(3.0, abs=0.2)


async def test_reconfigure_shrink_clamps_tokens_immediately(
    monkeypatch: pytest.MonkeyPatch, clock: FakeClock
) -> None:
    patch_sleep(monkeypatch, clock)
    limiter = TokenBucketRateLimiter(requests_per_minute=5, safe_requests=True, clock=clock)

    assert limiter.available_tokens == 5.0
    limiter.reconfigure(2, True)
    assert limiter.available_tokens == 2.0
    # Growing back does NOT mint free tokens beyond what survived the clamp.
    limiter.reconfigure(50, True)
    assert limiter.available_tokens == 2.0


async def test_available_tokens_refills_lazily_and_caps_at_capacity(
    monkeypatch: pytest.MonkeyPatch, clock: FakeClock
) -> None:
    patch_sleep(monkeypatch, clock)
    limiter = TokenBucketRateLimiter(requests_per_minute=2, safe_requests=True, clock=clock)

    await limiter.wait_for_token()
    await limiter.wait_for_token()
    await limiter.wait_for_token()  # bucket drained; this grant slept ~29s via the clock
    assert limiter.available_tokens == pytest.approx(0.0, abs=1e-9)

    clock.advance(120.0)
    assert limiter.available_tokens == 2.0


async def test_rpm_is_clamped_to_at_least_one(clock: FakeClock) -> None:
    limiter = TokenBucketRateLimiter(requests_per_minute=0, safe_requests=True, clock=clock)
    assert limiter.available_tokens == 1.0
    limiter.reconfigure(-7, True)
    assert limiter.available_tokens == 1.0


async def test_sub_ten_millisecond_waits_floored_to_ten_ms(
    monkeypatch: pytest.MonkeyPatch, clock: FakeClock
) -> None:
    sleeps = patch_sleep(monkeypatch, clock)
    limiter = TokenBucketRateLimiter(requests_per_minute=60, safe_requests=True, clock=clock)

    await limiter.wait_for_token()
    clock.advance(0.9995)  # spacing almost elapsed; remainder is 0.5ms
    await limiter.wait_for_token()

    assert sleeps[-1] == pytest.approx(0.010)


async def test_wait_for_token_honors_cancellation(clock: FakeClock) -> None:
    limiter = TokenBucketRateLimiter(requests_per_minute=1, safe_requests=True, clock=clock)
    await limiter.wait_for_token()  # drains the bucket; next grant needs ~60s

    task = asyncio.ensure_future(limiter.wait_for_token())
    await asyncio.sleep(0)
    task.cancel()
    with pytest.raises(asyncio.CancelledError):
        await task


async def test_default_constructor_matches_documented_defaults(clock: FakeClock) -> None:
    limiter = TokenBucketRateLimiter(clock=cast_clock(clock))
    assert limiter.available_tokens == 2.0
    assert TokenBucketRateLimiter.DEFAULT_REQUESTS_PER_MINUTE == 2
    assert TokenBucketRateLimiter.FAST_MODE_REFILL_MULTIPLIER == 10.0
    assert TokenBucketRateLimiter.SAFE_MODE_MINIMUM_SPACING.total_seconds() == 1.0


def cast_clock(clock: FakeClock) -> Callable[[], float]:
    return clock
