"""Async page fetching over one injected :class:`httpx.AsyncClient`.

Python-side port of C# ``MostaqlScraper.GetStringAsync`` (linked-CTS 15s timeout,
``EnsureSuccessStatusCode``, redirect-following ``HttpClient``). The C# side built
error messages inline without stable codes; this module consolidates the taxonomy
into three Python-side codes that conceptually extend the C# ``HTTP`` family:

- ``HTTP-001`` -- request failure: transport error or non-2xx status
  (C# ``EnsureSuccessStatusCode`` -> ``RequestFailed``).
- ``HTTP-002`` -- request exceeded the per-request timeout
  (C# linked-CTS ``CancelAfter(15s)`` -> ``Timeout``).
- ``HTTP-003`` -- unexpected failure (C# ``Unexpected``).

Caller cancellation is never swallowed: it propagates naturally as
``asyncio.CancelledError`` (mirrors the C# rethrow branch that distinguishes
caller-cancel from the linked-timeout firing).
"""

import httpx

from mostaql.errors import DomainError, HttpRequestError, HttpUnexpectedError, NetworkTimeoutError

__all__ = ["PageFetcher", "build_default_client"]

_DEFAULT_TIMEOUT_SECONDS = 15.0

# Byte-identical to the C# HttpClient singleton registered in MauiProgram.cs.
_USER_AGENT = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
    "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36"
)
_ACCEPT = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"
_ACCEPT_LANGUAGE = "ar,en-US;q=0.9,en;q=0.8"


def build_default_client() -> httpx.AsyncClient:
    """Anonymous client carrying the exact browser headers of the C# singleton.

    The three headers are LOAD-BEARING against mostaql.com's bot filter, which
    answers header-less requests with HTTP 403 before producing any HTML
    (verified against the endpoint; see MauiProgram.cs:86-94) -- do not trim,
    reorder, or "modernize" them. Cookies are deliberately unsupported
    (sessions are out of scope for the Python backbone).
    """
    return httpx.AsyncClient(
        headers={
            "User-Agent": _USER_AGENT,
            "Accept": _ACCEPT,
            "Accept-Language": _ACCEPT_LANGUAGE,
        }
    )


class PageFetcher:
    """Fetches page HTML through ONE injected client, mapping failures onto the
    typed error hierarchy (:class:`NetworkTimeoutError`, :class:`HttpRequestError`,
    :class:`HttpUnexpectedError`).
    """

    def __init__(
        self, client: httpx.AsyncClient, timeout_seconds: float = _DEFAULT_TIMEOUT_SECONDS
    ) -> None:
        self._client = client
        self._timeout_seconds = timeout_seconds

    async def get_html(self, url: str) -> str:
        """GET ``url`` and return the decoded response body text.

        The per-request timeout mirrors the C# linked-CTS ``CancelAfter(15s)``;
        redirects are followed per request (``HttpClient``'s auto-redirect default).
        Raises ``NetworkTimeoutError`` (HTTP-002), ``HttpRequestError`` (HTTP-001),
        or ``HttpUnexpectedError`` (HTTP-003). ``asyncio.CancelledError`` from the
        caller propagates untouched.
        """
        try:
            response = await self._client.get(
                url,
                timeout=httpx.Timeout(self._timeout_seconds),
                follow_redirects=True,
            )
        except httpx.TimeoutException as exc:
            raise _timeout_error(url, self._timeout_seconds, exc) from exc
        except httpx.TransportError as exc:
            raise _request_failed(url, str(exc), cause=exc) from exc
        except Exception as exc:
            raise _unexpected_error(url, exc) from exc

        if not response.is_success:
            detail = f"non-success status code {response.status_code}"
            raise _request_failed(url, detail)

        return response.text


def _request_failed(url: str, detail: str, cause: BaseException | None = None) -> HttpRequestError:
    """HTTP-001 -- transport failure or non-2xx response."""
    return HttpRequestError(
        DomainError(
            code="HTTP-001",
            internal_message=f"Request to '{url}' failed: {detail}",
            external_message="تعذر الاتصال بالموقع.",
            cause=cause,
        )
    )


def _timeout_error(url: str, timeout_seconds: float, cause: BaseException) -> NetworkTimeoutError:
    """HTTP-002 -- request exceeded its per-request timeout."""
    return NetworkTimeoutError(
        DomainError(
            code="HTTP-002",
            internal_message=f"Request to '{url}' exceeded {timeout_seconds}s.",
            external_message="انتهت مهلة الاتصال بالموقع.",
            cause=cause,
        )
    )


def _unexpected_error(url: str, cause: BaseException) -> HttpUnexpectedError:
    """HTTP-003 -- failure outside the transport/status taxonomy."""
    return HttpUnexpectedError(
        DomainError(
            code="HTTP-003",
            internal_message=f"Unexpected error requesting '{url}': {cause}",
            external_message="حدث خطأ غير متوقع أثناء الاتصال بموقع مستقل.",
            cause=cause,
        )
    )
