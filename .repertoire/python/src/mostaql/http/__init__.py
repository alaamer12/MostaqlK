"""HTTP fetching layer; the only place ``httpx`` may be imported (plan §10 contract)."""

from mostaql.http.client import PageFetcher, build_default_client

__all__ = ["PageFetcher", "build_default_client"]
