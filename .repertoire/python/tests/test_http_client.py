"""PageFetcher + default-client tests (C# GetStringAsync taxonomy; plan phase 6)."""

import asyncio

import httpx
import pytest

from mostaql.errors import HttpRequestError, HttpUnexpectedError, NetworkTimeoutError
from mostaql.http import PageFetcher, build_default_client

EXPECTED_USER_AGENT = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
    "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36"
)
EXPECTED_ACCEPT = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"
EXPECTED_ACCEPT_LANGUAGE = "ar,en-US;q=0.9,en;q=0.8"


def _client_with(handler):
    return httpx.AsyncClient(transport=httpx.MockTransport(handler))


async def test_success_returns_body():
    def handler(_request: httpx.Request) -> httpx.Response:
        return httpx.Response(200, text="<html>ok</html>")

    async with _client_with(handler) as client:
        fetcher = PageFetcher(client)
        assert await fetcher.get_html("https://mostaql.test/projects") == "<html>ok</html>"


@pytest.mark.parametrize("status", [403, 500])
async def test_non_2xx_maps_to_request_error(status):
    def handler(_request: httpx.Request) -> httpx.Response:
        return httpx.Response(status, text="denied")

    async with _client_with(handler) as client:
        fetcher = PageFetcher(client)
        with pytest.raises(HttpRequestError) as excinfo:
            await fetcher.get_html("https://mostaql.test/projects")

        error = excinfo.value.error
        assert error.code == "HTTP-001"
        assert error.external_message == "تعذر الاتصال بالموقع."
        assert "https://mostaql.test/projects" in error.internal_message
        assert str(status) in error.internal_message


async def test_transport_timeout_maps_to_network_timeout_error():
    def handler(request: httpx.Request) -> httpx.Response:
        raise httpx.ConnectTimeout("too slow", request=request)

    async with _client_with(handler) as client:
        fetcher = PageFetcher(client)
        with pytest.raises(NetworkTimeoutError) as excinfo:
            await fetcher.get_html("https://mostaql.test/projects")

        error = excinfo.value.error
        assert error.code == "HTTP-002"
        assert error.external_message == "انتهت مهلة الاتصال بالموقع."
        assert error.internal_message == (
            "Request to 'https://mostaql.test/projects' exceeded 15.0s."
        )
        assert isinstance(error.cause, httpx.TimeoutException)


async def test_custom_timeout_appears_in_error_message():
    def handler(request: httpx.Request) -> httpx.Response:
        raise httpx.ReadTimeout("stalled", request=request)

    async with _client_with(handler) as client:
        fetcher = PageFetcher(client, timeout_seconds=2.5)
        with pytest.raises(NetworkTimeoutError) as excinfo:
            await fetcher.get_html("https://mostaql.test/projects")

        assert "exceeded 2.5s." in excinfo.value.error.internal_message


async def test_transport_failure_maps_to_request_error():
    def handler(request: httpx.Request) -> httpx.Response:
        raise httpx.ConnectError("connection refused", request=request)

    async with _client_with(handler) as client:
        fetcher = PageFetcher(client)
        with pytest.raises(HttpRequestError) as excinfo:
            await fetcher.get_html("https://mostaql.test/projects")

        error = excinfo.value.error
        assert error.code == "HTTP-001"
        assert "connection refused" in error.internal_message
        assert isinstance(error.cause, httpx.TransportError)


async def test_generic_exception_maps_to_http_unexpected_error():
    def handler(_request: httpx.Request) -> httpx.Response:
        raise ValueError("boom")

    async with _client_with(handler) as client:
        fetcher = PageFetcher(client)
        with pytest.raises(HttpUnexpectedError) as excinfo:
            await fetcher.get_html("https://mostaql.test/projects")

        error = excinfo.value.error
        assert error.code == "HTTP-003"
        assert "https://mostaql.test/projects" in error.internal_message
        assert isinstance(error.cause, ValueError)


async def test_redirects_are_followed_per_request():
    def handler(request: httpx.Request) -> httpx.Response:
        if request.url.path == "/start":
            return httpx.Response(301, headers={"Location": "https://redirect.test/finish"})
        return httpx.Response(200, text="final-body")

    async with _client_with(handler) as client:
        fetcher = PageFetcher(client)
        assert await fetcher.get_html("https://redirect.test/start") == "final-body"


async def test_default_client_carries_bot_filter_headers_verbatim():
    client = build_default_client()
    try:
        assert client.headers["User-Agent"] == EXPECTED_USER_AGENT
        assert client.headers["Accept"] == EXPECTED_ACCEPT
        assert client.headers["Accept-Language"] == EXPECTED_ACCEPT_LANGUAGE
    finally:
        await client.aclose()


async def test_caller_cancellation_propagates_as_cancelled_error():
    async def handler(_request: httpx.Request) -> httpx.Response:
        await asyncio.sleep(30)
        raise AssertionError("handler should have been cancelled")

    async with _client_with(handler) as client:
        fetcher = PageFetcher(client)
        task = asyncio.create_task(fetcher.get_html("https://mostaql.test/projects"))
        await asyncio.sleep(0.05)
        task.cancel()
        with pytest.raises(asyncio.CancelledError):
            await task
