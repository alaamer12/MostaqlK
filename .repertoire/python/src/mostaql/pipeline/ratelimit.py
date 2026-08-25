"""Async shared token-bucket rate limiter on the monotonic clock (plan §8 contract).

Port of C# ``Services/Pipeline/TokenBucketRateLimiter.cs``. The bucket is defined by a
single number, ``requests_per_minute``: capacity equals that per-minute budget and tokens
refill at ``rpm / 60`` per second, so a long idle period never lets the app burst its
entire minute's budget in one second. Safe mode additionally spaces consecutive grants by
one second; fast mode (``safe_requests=False``) refills ten times quicker with no spacing,
always derived from the configured ``rpm`` so it can never diverge from what was set.
"""

import asyncio
import time
from collections.abc import Callable
from datetime import timedelta

__all__ = ["TokenBucketRateLimiter"]

_SLEEP = asyncio.sleep

_MIN_WAIT_SECONDS = 0.010


class TokenBucketRateLimiter:
    """Shared token bucket across every outbound request of poller and workers.

    C# origin: ``Services/Pipeline/TokenBucketRateLimiter.cs``. Ledger #2: timing uses
    :func:`time.monotonic` instead of wall clock -- immune to system clock changes,
    externally identical spacing/refill behavior.
    """

    DEFAULT_REQUESTS_PER_MINUTE = 2
    FAST_MODE_REFILL_MULTIPLIER = 10.0
    SAFE_MODE_MINIMUM_SPACING = timedelta(seconds=1)

    def __init__(
        self,
        requests_per_minute: int = DEFAULT_REQUESTS_PER_MINUTE,
        safe_requests: bool = True,
        clock: Callable[[], float] = time.monotonic,
    ) -> None:
        self._clock = clock
        self._lock = asyncio.Lock()
        self._tokens = 0.0
        self._last_refill = clock()
        # -inf mirrors C# DateTimeOffset.MinValue: the first grant never waits for spacing.
        self._last_grant = float("-inf")
        self._safe_requests = safe_requests
        self._capacity = 1.0
        self._refill_per_second = 1.0
        self._minimum_spacing = 0.0
        self._apply(requests_per_minute, safe_requests, fill_bucket=True)

    @property
    def available_tokens(self) -> float:
        """Current token count, refilled lazily on read (C# ``AvailableTokens``).

        Exposed so a UI can show a live rate-budget indicator without any background
        timer plumbing inside the limiter itself. Single-loop read: the critical
        section below contains no await points, so no interleaving can occur.
        """
        now = self._clock()
        self._refill(now)
        return self._tokens

    def reconfigure(self, requests_per_minute: int, safe_requests: bool) -> None:
        """Live-reconfigure the bucket; clamps current tokens to the new capacity.

        C# ``Reconfigure`` (called from the settings view-model when the user changes
        either setting while the pipeline is running).
        """
        self._apply(requests_per_minute, safe_requests, fill_bucket=False)

    async def wait_for_token(self) -> None:
        """Wait until one token is available, then consume it (C# ``WaitForTokenAsync``).

        Lazy refill-on-acquire: every call tops up the bucket based on elapsed time since
        the last refill (capped at capacity), then either consumes immediately or sleeps
        exactly as long as the next fractional token needs -- simpler than a background
        timer and equally correct since every acquisition path goes through here.
        Caller cancellation propagates naturally via :class:`asyncio.CancelledError`.
        """
        while True:
            wait_seconds = self._acquire_or_compute_wait()
            if wait_seconds <= 0.0:
                return
            await _SLEEP(max(wait_seconds, _MIN_WAIT_SECONDS))

    def _acquire_or_compute_wait(self) -> float:
        """Single-loop critical section (C# holds ``_gate`` here): no await points."""
        now = self._clock()
        self._refill(now)
        since_grant = now - self._last_grant
        if since_grant < self._minimum_spacing:
            return self._minimum_spacing - since_grant
        if self._tokens >= 1.0:
            self._tokens -= 1.0
            self._last_grant = now
            return 0.0
        return (1.0 - self._tokens) / self._refill_per_second

    def _refill(self, now: float) -> None:
        elapsed = now - self._last_refill
        if elapsed <= 0:
            return
        self._tokens = min(self._capacity, self._tokens + elapsed * self._refill_per_second)
        self._last_refill = now

    def _apply(self, requests_per_minute: int, safe_requests: bool, fill_bucket: bool) -> None:
        rpm = max(1, requests_per_minute)
        self._safe_requests = safe_requests
        self._capacity = float(rpm)
        multiplier = 1.0 if safe_requests else self.FAST_MODE_REFILL_MULTIPLIER
        self._refill_per_second = rpm / 60.0 * multiplier
        self._minimum_spacing = (
            self.SAFE_MODE_MINIMUM_SPACING.total_seconds() if safe_requests else 0.0
        )
        self._tokens = self._capacity if fill_bucket else min(self._tokens, self._capacity)
