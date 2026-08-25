"""Contract suite over TWO ProjectStore implementations (plan §13).

Parameterized across the real SQLiteStore and an InMemoryStore fake so any
future remote store can reuse the same behavioral guarantees. White-box raw-
sqlite introspection blocks are gated on the implementation (allowed per task
spec) for byte-level checks the Protocol cannot express.
"""

import sqlite3
from collections.abc import AsyncIterator
from dataclasses import replace
from datetime import UTC, datetime, timedelta
from typing import Any

import pytest
from fakes import InMemoryStore

from mostaql.models import (
    EnrichmentStatus,
    Owner,
    ProjectDetails,
    ProjectSkill,
    ProjectSummary,
)
from mostaql.storage.sqlite_store import SQLiteStore
from mostaql.storage.timestamps import dotnet_o_format

T0 = datetime(2026, 8, 25, 12, 0, 0, tzinfo=UTC)


def make_summary(project_id: int, *, at: datetime = T0, **over: Any) -> ProjectSummary:
    base = ProjectSummary(
        project_id=project_id,
        title=f"مشروع {project_id}",
        url=f"https://mostaql.com/project/{project_id}",
        description="",
        discovered_at=at,
    )
    return replace(base, **over)


def make_details(
    summary: ProjectSummary, *, enriched_at: datetime | None = None, **over: Any
) -> ProjectDetails:
    base = ProjectDetails(
        project_id=summary.project_id,
        title=summary.title,
        url=summary.url,
        client_name="مالك",
        description="وصف المشروع بالكامل",
        enrichment_status=EnrichmentStatus.ENRICHED,
        discovered_at=summary.discovered_at,
        enriched_at=enriched_at or summary.discovered_at,
        owner=Owner(owner_id=7, name="مالك"),
        skills=[ProjectSkill(name="PHP")],
    )
    return replace(base, **over)


@pytest.fixture(params=["sqlite", "memory"])
async def store(
    request: pytest.FixtureRequest, tmp_path
) -> AsyncIterator[InMemoryStore | SQLiteStore]:
    if request.param == "sqlite":
        impl: SQLiteStore = SQLiteStore(tmp_path / "store.db")
        yield impl
        impl.close()
    else:
        yield InMemoryStore()


async def _seed_enriched_pair(impl, first_id: int, second_id: int) -> None:
    await impl.insert_summary(make_summary(first_id, at=T0))
    await impl.insert_summary(make_summary(second_id, at=T0 + timedelta(seconds=30)))
    await impl.upsert_details(
        make_details(
            make_summary(first_id, at=T0),
            enriched_at=T0 + timedelta(seconds=10),
        )
    )


async def test_insert_summary_new_then_duplicate(store) -> None:
    assert await store.insert_summary(make_summary(1)) is True
    assert await store.insert_summary(make_summary(1)) is False
    tracked, unread = await store.count_tracked()
    assert (tracked, unread) == (1, 1)


async def test_insert_summary_is_write_once(store) -> None:
    await store.insert_summary(make_summary(1, title="الأصل"))
    await store.upsert_details(make_details(make_summary(1, title="الأصل"), enriched_at=T0))
    recent = await store.get_recent(10)
    assert len(recent) == 1


async def test_insert_summary_populates_fts_row(tmp_path) -> None:
    db_path = tmp_path / "fts.db"
    impl = SQLiteStore(db_path)
    try:
        assert await impl.insert_summary(make_summary(9)) is True
    finally:
        impl.close()
    con = sqlite3.connect(db_path)
    try:
        row = con.execute(
            "SELECT project_id, title, description, skills FROM projects_fts WHERE project_id = 9"
        ).fetchone()
    finally:
        con.close()
    assert row is not None
    assert row[0] == 9
    assert row[3] == ""


async def test_upsert_details_sentinel_matrix(store, tmp_path) -> None:
    listing = make_summary(
        1,
        publish_time_number=2024,
        publish_time_text="منذ يومين",
        proposal_count=3,
        proposal_count_text="3 عروض",
        description="قديم",
    )
    assert await store.insert_summary(listing) is True

    details = make_details(
        listing,
        enriched_at=T0 + timedelta(seconds=5),
        publish_time_number=0,
        publish_time_text="",
        proposal_count=0,
        proposal_count_text="",
        description="جديد",
        budget="50 $",
        delivery_days=7,
        project_status="مكتمل",
        owner=Owner(owner_id=0, name="مالك"),
        skills=[ProjectSkill(name="PHP"), ProjectSkill(name="Laravel", url="/skills/laravel")],
    )
    await store.upsert_details(details)

    recent = {s.project_id: s for s in await store.get_recent(10)}[1]
    assert recent.publish_time_number == 2024
    assert recent.publish_time_text == "منذ يومين"
    assert recent.proposal_count == 3
    assert recent.proposal_count_text == "3 عروض"
    assert recent.description == "جديد"
    assert recent.budget == "50 $"
    assert recent.delivery_days == 7
    assert recent.project_status == "مكتمل"
    assert recent.enrichment_status is EnrichmentStatus.ENRICHED
    assert recent.enriched_at is not None
    assert recent.discovered_at == T0

    if isinstance(store, SQLiteStore):
        con = sqlite3.connect(store.db_path)
        try:
            owner_id, discovered_text = con.execute(
                "SELECT owner_id, discovered_at FROM projects WHERE project_id = 1"
            ).fetchone()
            skill_rows = con.execute(
                "SELECT name FROM project_skills WHERE project_id = 1 ORDER BY name"
            ).fetchall()
        finally:
            con.close()
        assert owner_id is None
        assert discovered_text == dotnet_o_format(T0)
        assert [r[0] for r in skill_rows] == ["Laravel", "PHP"]

    overwritten = make_details(
        listing,
        enriched_at=T0 + timedelta(seconds=6),
        publish_time_number=1999,
        publish_time_text="منذ ساعة",
        proposal_count=9,
        proposal_count_text="9 عروض",
        owner=Owner(owner_id=42, name="مالك ثانٍ"),
        skills=[ProjectSkill(name="Django")],
    )
    await store.upsert_details(overwritten)
    recent = {s.project_id: s for s in await store.get_recent(10)}[1]
    assert recent.publish_time_number == 1999
    assert recent.publish_time_text == "منذ ساعة"
    assert recent.proposal_count == 9
    assert recent.proposal_count_text == "9 عروض"
    assert recent.skills_text == "Django"


async def test_skills_replace_on_reenrichment(store) -> None:
    listing = make_summary(2)
    await store.insert_summary(listing)
    first = make_details(
        listing,
        enriched_at=T0,
        skills=[ProjectSkill(name="PHP"), ProjectSkill(name="CSS", url="/skills/css")],
    )
    second = make_details(listing, enriched_at=T0, skills=[ProjectSkill(name="Vue")])
    await store.upsert_details(first)
    await store.upsert_details(second)
    recent = (await store.get_recent(10))[0]
    assert recent.skills_text == "Vue"


async def test_owner_upsert_identity_vs_stats(store) -> None:
    original = Owner(
        owner_id=5,
        name="أحمد",
        profile_url="/u/a",
        avatar_url="a.png",
        registered_at="2020",
    )
    await store.upsert_owner(original)
    updated = replace(
        original,
        name="أحمد المعدَّل",
        profile_url="/u/new",
        avatar_url="new.png",
        rating=4.5,
        completed_projects_count=10,
        hiring_rate_percent=88.5,
        registered_at="2021",
        open_projects_count=2,
        in_progress_projects_count=1,
        ongoing_communications_count=3,
    )
    await store.upsert_owner(updated)

    if isinstance(store, SQLiteStore):
        con = sqlite3.connect(store.db_path)
        try:
            row = con.execute(
                """
                SELECT name, profile_url, avatar_url, rating, completed_projects_count,
                       hiring_rate_percent, registered_at, open_projects_count,
                       in_progress_projects_count, ongoing_communications_count, last_seen_at
                FROM owners WHERE owner_id = 5
                """
            ).fetchone()
        finally:
            con.close()
        assert row[0] == "أحمد"
        assert row[1] == "/u/a"
        assert row[2] == "a.png"
        assert row[3] == 4.5
        assert row[4] == 10
        assert row[5] == 88.5
        assert row[6] == "2021"
        assert row[7] == 2
        assert row[8] == 1
        assert row[9] == 3
        assert row[10].startswith("20")
    else:
        stored = store.owners[5]
        assert stored.name == "أحمد"
        assert stored.profile_url == "/u/a"
        assert stored.avatar_url == "a.png"
        assert stored.rating == 4.5
        assert stored.completed_projects_count == 10
        assert stored.registered_at == "2021"
        assert store.owner_last_seen[5]


async def test_get_all_known_project_ids(store) -> None:
    await store.insert_summary(make_summary(1))
    await store.insert_summary(make_summary(2))
    known = await store.get_all_known_project_ids()
    assert known == {1, 2}


def _age_backlog_entry(impl, project_id: int, *, seconds: int) -> None:
    if isinstance(impl, SQLiteStore):
        con = sqlite3.connect(impl.db_path, timeout=5.0)
        try:
            con.execute(
                "UPDATE discovery_backlog SET discovered_at = datetime('now', ?) "
                "WHERE project_id = ?",
                (f"{seconds} seconds", project_id),
            )
            con.commit()
        finally:
            con.close()
    else:
        aged = (datetime.now(UTC) + timedelta(seconds=seconds)).strftime("%Y-%m-%d %H:%M:%S")
        impl.backlog[project_id] = aged


async def test_backlog_lifecycle_and_ordering(store) -> None:
    await store.add_to_backlog(1)
    await store.add_to_backlog(2)
    await store.add_to_backlog(3)
    ids = await store.get_backlog_ids()
    assert sorted(ids) == [1, 2, 3]

    _age_backlog_entry(store, 1, seconds=-7200)
    ids_after_ageing = await store.get_backlog_ids()
    assert ids_after_ageing[0] == 1
    assert sorted(ids_after_ageing) == [1, 2, 3]

    await store.remove_from_backlog(2)
    remaining = await store.get_backlog_ids()
    assert 2 not in remaining


async def test_clean_old_backlog_removes_only_stale(store) -> None:
    await store.add_to_backlog(40)
    await store.add_to_backlog(41)
    _age_backlog_entry(store, 40, seconds=-40 * 24 * 3600)

    deleted = await store.clean_old_backlog(days=30)

    assert deleted == 1
    remaining = await store.get_backlog_ids()
    assert 40 not in remaining
    assert 41 in remaining


async def test_get_recent_ordering_pending_last_then_recency(store) -> None:
    await _seed_enriched_pair(store, first_id=1, second_id=2)
    third = make_summary(3, at=T0 + timedelta(seconds=5))
    await store.insert_summary(third)
    await store.upsert_details(make_details(third, enriched_at=T0 + timedelta(seconds=20)))
    fourth = make_summary(4, at=T0 + timedelta(seconds=60))
    await store.insert_summary(fourth)

    order = [s.project_id for s in await store.get_recent(10)]

    assert order == [3, 1, 4, 2]


async def test_mark_as_read_guarded(store) -> None:
    await store.insert_summary(make_summary(1))
    await store.insert_summary(make_summary(2))

    await store.mark_as_read(1)
    assert await store.count_tracked() == (2, 1)
    await store.mark_as_read(1)
    assert await store.count_tracked() == (2, 1)

    await store.mark_all_as_read()
    assert await store.count_tracked() == (2, 0)


async def test_count_added_today(store) -> None:
    await store.insert_summary(make_summary(1, at=datetime.now(UTC)))
    await store.insert_summary(make_summary(2, at=datetime.now(UTC) - timedelta(hours=1)))
    assert await store.count_added_today() == 2


async def test_search_prefix_arabic_term(store) -> None:
    listing = make_summary(1, title="تصميم موقع متجر إلكتروني")
    await store.insert_summary(listing)
    other = make_summary(2, title="برمجة تطبيق جوال")
    await store.insert_summary(other)
    await store.upsert_details(
        make_details(
            listing,
            enriched_at=T0,
            description="بناء واجهة متجر",
            skills=[ProjectSkill(name="PHP"), ProjectSkill(name="Laravel")],
        )
    )

    hits = store.search("تصمي")
    assert [s.project_id for s in hits] == [1]

    hits_multi = store.search("تصميم موقع")
    assert [s.project_id for s in hits_multi] == [1]
    assert hits_multi[0].skills_text != ""
    assert hits_multi[0].enriched_at is None

    assert store.search("غيرموجود_إطلاقاً") == []
    assert store.search("") == []
