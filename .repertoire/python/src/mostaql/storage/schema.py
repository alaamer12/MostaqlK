"""DDL, PRAGMAs, user_version bootstrap, and FTS backfill (C# SqliteConnectionFactory).

Schema and pragmas are byte-parity with the C# factory: ``assets``/``app_secrets``
tables dropped per plan §2 scope lock; FKs declared but never enforced (storage
trap 9 — ``PRAGMA foreign_keys`` is never enabled); version tracked via
``PRAGMA user_version`` with a single bootstrap migration.
"""

import sqlite3
import threading
from pathlib import Path

from mostaql.errors import SchemaMismatchError, schema_mismatch

__all__ = [
    "CURRENT_SCHEMA_VERSION",
    "INITIAL_SCHEMA_SQL",
    "SCHEMA_VERSION",
    "SchemaVerificationFlag",
    "backfill_missing_fts_rows",
    "connect",
    "ensure_schema",
]

SCHEMA_VERSION = 1

CURRENT_SCHEMA_VERSION = SCHEMA_VERSION

INITIAL_SCHEMA_SQL = """
CREATE TABLE IF NOT EXISTS projects (
    project_id INTEGER PRIMARY KEY,
    title TEXT NOT NULL,
    url TEXT NOT NULL,
    client_name TEXT,
    publish_time_number INTEGER,
    publish_time_text TEXT,
    proposal_count INTEGER,
    proposal_count_text TEXT,
    description TEXT,
    budget TEXT,
    delivery_days INTEGER,
    project_status TEXT,
    owner_id INTEGER,
    is_unread INTEGER NOT NULL DEFAULT 1,
    enrichment_status TEXT NOT NULL DEFAULT 'Pending',
    discovered_at TEXT NOT NULL,
    enriched_at TEXT
);

CREATE TABLE IF NOT EXISTS owners (
    owner_id INTEGER PRIMARY KEY,
    name TEXT NOT NULL,
    profile_url TEXT,
    avatar_url TEXT,
    rating REAL,
    completed_projects_count INTEGER,
    hiring_rate_percent REAL,
    registered_at TEXT,
    open_projects_count INTEGER,
    in_progress_projects_count INTEGER,
    ongoing_communications_count INTEGER,
    last_seen_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS project_skills (
    project_id INTEGER NOT NULL,
    name TEXT NOT NULL,
    url TEXT,
    FOREIGN KEY (project_id) REFERENCES projects (project_id)
);

CREATE TABLE IF NOT EXISTS discovery_backlog (
    project_id INTEGER PRIMARY KEY,
    discovered_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    retry_count INTEGER DEFAULT 0
);

CREATE VIRTUAL TABLE IF NOT EXISTS projects_fts USING fts5(
    project_id UNINDEXED,
    title,
    description,
    skills,
    tokenize = 'unicode61 remove_diacritics 2'
);
"""

_MIGRATION_LOCK = threading.Lock()


class SchemaVerificationFlag:
    """Box for the once-per-process verified marker (C# ``_schemaVerified``)."""

    __slots__ = ("verified",)

    def __init__(self) -> None:
        self.verified = False


def connect(db_path: Path | str) -> sqlite3.Connection:
    """Open a configured connection: WAL + busy_timeout on EVERY open (mirror C#).

    ``check_same_thread=False`` supports one shared connection driven through
    the store's lock from asyncio worker threads; explicit autocommit mode
    mirrors C#'s default-autocommit plus explicit ``BeginTransaction``.
    """
    connection = sqlite3.connect(str(db_path), check_same_thread=False)
    connection.row_factory = sqlite3.Row
    connection.isolation_level = None
    connection.execute("PRAGMA journal_mode=WAL;")
    connection.execute("PRAGMA busy_timeout=5000;")
    return connection


def ensure_schema(
    conn: sqlite3.Connection, *, schema_verified_flag: SchemaVerificationFlag
) -> None:
    """Bootstrap or verify the schema once per process (C# ``EnsureSchema``).

    Reads ``PRAGMA user_version``; on a fresh database runs the DDL inside a
    transaction then sets ``user_version=1``; any other non-current version
    raises :class:`SchemaMismatchError` (DB-003). Idempotent via the flag box;
    the FTS backfill runs under the same once-per-process gate as C#.
    """
    with _MIGRATION_LOCK:
        if schema_verified_flag.verified:
            return
        current_version = get_user_version(conn)
        if current_version == 0:
            run_initial_migration(conn)
            set_user_version(conn, CURRENT_SCHEMA_VERSION)
        elif current_version != CURRENT_SCHEMA_VERSION:
            raise SchemaMismatchError(schema_mismatch(current_version, CURRENT_SCHEMA_VERSION))
        backfill_missing_fts_rows(conn)
        schema_verified_flag.verified = True


def get_user_version(conn: sqlite3.Connection) -> int:
    row = conn.execute("PRAGMA user_version;").fetchone()
    return int(row[0])


def set_user_version(conn: sqlite3.Connection, version: int) -> None:
    conn.execute(f"PRAGMA user_version = {int(version)};")


def run_initial_migration(conn: sqlite3.Connection) -> None:
    conn.execute("BEGIN IMMEDIATE;")
    try:
        for statement in _ddl_statements():
            conn.execute(statement)
    except BaseException:
        conn.rollback()
        raise
    else:
        conn.commit()


def backfill_missing_fts_rows(conn: sqlite3.Connection) -> None:
    """One-time-per-process backfill (C# ``BackfillMissingFtsRows``, secrets-free)."""
    conn.execute(
        """
        INSERT INTO projects_fts (project_id, title, description, skills)
        SELECT p.project_id, p.title, COALESCE(p.description, ''),
               COALESCE((SELECT group_concat(name, ' ') FROM project_skills s
                         WHERE s.project_id = p.project_id), '')
        FROM projects p
        WHERE p.project_id NOT IN (SELECT project_id FROM projects_fts);
        """
    )


def _ddl_statements() -> tuple[str, ...]:
    return tuple(s.strip() for s in INITIAL_SCHEMA_SQL.split(";") if s.strip())
