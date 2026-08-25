"""Persistence layer: ProjectStore Protocol plus the SQLite implementation and helpers.

Business-facing surface is :class:`ProjectStore`; pipeline code must never
import ``mostaql.storage.sqlite_store`` (plan §10 import-linter contract).
"""

from mostaql.storage.protocol import ProjectStore
from mostaql.storage.schema import (
    CURRENT_SCHEMA_VERSION,
    INITIAL_SCHEMA_SQL,
    SCHEMA_VERSION,
    SchemaVerificationFlag,
    backfill_missing_fts_rows,
    connect,
    ensure_schema,
)
from mostaql.storage.search import build_fts_query
from mostaql.storage.sqlite_store import SQLiteStore
from mostaql.storage.timestamps import current_utc, dotnet_o_format, parse_dotnet_o

__all__ = [
    "CURRENT_SCHEMA_VERSION",
    "INITIAL_SCHEMA_SQL",
    "SCHEMA_VERSION",
    "ProjectStore",
    "SQLiteStore",
    "SchemaVerificationFlag",
    "backfill_missing_fts_rows",
    "build_fts_query",
    "connect",
    "current_utc",
    "dotnet_o_format",
    "ensure_schema",
    "parse_dotnet_o",
]
